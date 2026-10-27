using System.Numerics;
using System.Windows;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class BasinExplorerWindow
{
    private CancellationTokenSource? _preparationCts, _analysisCts;
    private Task<bool> _preparationTask = Task.FromResult(false);
    private bool _closed;
    private int _analysisRequest;

    internal Task<bool> EnsurePreparedAsync()
    {
        if (_closed) return Task.FromResult(false);
        if (_preparationCts is { IsCancellationRequested: false }) return _preparationTask;
        if (!_formulaDirty && _engine.IsReady) return Task.FromResult(true);
        ApplyFormula(false, scheduleRender: false);
        return _preparationTask;
    }

    private void CancelPreparation()
    {
        _preparationCts?.Cancel();
    }

    private void CancelAnalysis()
    {
        _analysisRequest++;
        _analysisCts?.Cancel();
    }

    private void UpdateComputationState() =>
        CancelButton.IsEnabled = _isRendering || _preparationCts is not null || _analysisCts is not null;

    // Редактирование формулы само по себе не применяет её, но результат старого поиска уже не подходит.
    private void FormulaInput_OnChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_updatingUi) return;
        CancelPreparation();
        CancelAnalysis();
    }

    private void ApplyFormula(bool showMessage, IReadOnlyList<Complex>? suppliedRoots = null,
        IReadOnlyList<BasinAttractor>? suppliedAttractors = null, bool scheduleRender = true,
        IReadOnlyList<PlanarBasinAttractor>? suppliedPlanar = null)
    {
        if (showMessage) NumericInput.CommitAll(this); else NumericInput.Prepare(this);
        _preparationTask = PrepareFormulaAsync(showMessage, suppliedRoots, suppliedAttractors, scheduleRender, suppliedPlanar);
    }

    private async Task<bool> PrepareFormulaAsync(bool showMessage, IReadOnlyList<Complex>? suppliedRoots,
        IReadOnlyList<BasinAttractor>? suppliedAttractors, bool scheduleRender,
        IReadOnlyList<PlanarBasinAttractor>? suppliedPlanar)
    {
        CancelPreparation();
        CancelAnalysis();
        _renderCts?.Cancel();
        _renderTimer.Stop();
        _formulaDirty = true;
        var cts = new CancellationTokenSource();
        _preparationCts = cts;
        UpdateComputationState();
        try
        {
            BasinExplorerState state = ReadState("prepare", applying: true);
            if (suppliedRoots is not null) state.Roots = [.. suppliedRoots];
            if (suppliedAttractors is not null)
            {
                state.Attractors = suppliedAttractors.Select(a => a.Clone()).ToList();
                state.UseSavedAttractors = true;
            }
            if (suppliedPlanar is not null)
            {
                state.PlanarAttractors = suppliedPlanar.Select(a => a.Clone()).ToList();
                state.UseSavedPlanarAttractors = true;
            }
            StatusText.Text = UsesRoots ? "Поиск корней…" : "Подготовка и поиск аттракторов…";
            BasinExplorerEngine engine = await Task.Run(() => CreateEngine(state, cts.Token), cts.Token);
            if (_closed || cts.IsCancellationRequested || !ReferenceEquals(_preparationCts, cts)) return false;
            _preparationCts = null;
            _engine = engine;
            _appliedFormula = state.Formula;
            _appliedNumerator = state.Numerator;
            _appliedDenominator = state.Denominator;
            _appliedParameterC = state.ParameterC;
            _formulaDirty = false;
            _orbitTrace = [];
            DebugOutput.Text = engine.DebugInfo;
            if (UsesPlanar)
            {
                PlanarTargetsBox.ItemsSource = engine.PlanarAttractors.Select((a, i) => a.Describe(i)).ToArray();
                PlanarTargetCountText.Text = $"Найдено аттракторов: {engine.TargetCount}";
                UpdatePlanarLayout();
            }
            else RefreshTargetsUi();
            UpdateMethodControls();
            UpdateOverlay();
            StatusText.Text = UsesRoots ? $"Формула применена. Корней: {engine.Roots.Count}. {engine.RootSearchStrategy}"
                : IsLogisticParameter ? "Плоскость λ: цвет обозначает период притягивающего цикла. Двойной щелчок открывает его бассейны."
                : $"Параметры применены. Аттракторов: {engine.TargetCount}. Правая кнопка — результат и траектория.";
            if (scheduleRender) ScheduleRender(cancelAnalysis: false);
            return true;
        }
        catch (OperationCanceledException)
        {
            if (!_closed && ReferenceEquals(_preparationCts, cts)) StatusText.Text = "Подготовка отменена";
            return false;
        }
        catch (Exception ex)
        {
            if (!_closed && !cts.IsCancellationRequested && ReferenceEquals(_preparationCts, cts))
            {
                StatusText.Text = DebugOutput.Text = ex.Message;
                if (showMessage) MessageBox.Show(this, ex.Message, "Формула или параметры", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return false;
        }
        finally
        {
            if (ReferenceEquals(_preparationCts, cts)) _preparationCts = null;
            cts.Dispose();
            if (!_closed) UpdateComputationState();
        }
    }

    internal async Task SearchAttractorsAsync(Complex? seed = null)
    {
        NumericInput.CommitAll(this);
        Task<bool> preparation = EnsurePreparedAsync();
        CancelAnalysis();
        int request = _analysisRequest;
        if (!await preparation || _closed || request != _analysisRequest) return;
        var cts = new CancellationTokenSource();
        _analysisCts = cts;
        _renderCts?.Cancel();
        _renderTimer.Stop();
        UpdateComputationState();
        try
        {
            BasinExplorerState state = CaptureState("search");
            RenderSurfaceMetrics surface = RenderSurfaceMetrics.Measure(CanvasHost);
            int width = surface.PixelWidth, height = surface.PixelHeight;
            IProgress<int> progress = new Progress<int>(p =>
            {
                if (!_closed && !cts.IsCancellationRequested && ReferenceEquals(_analysisCts, cts))
                    StatusText.Text = $"Поиск аттракторов: {p}%";
            });
            StatusText.Text = "Поиск аттракторов…";
            var result = await Task.Run(() =>
            {
                BasinExplorerEngine engine = CreateEngine(state, cts.Token);
                int added = seed is Complex point
                    ? engine.DiscoverAttractors([point], true, false, progress.Report)
                    : engine.DiscoverAttractorsInView(width, height, reportProgress: progress.Report);
                return (Engine: engine, Added: added);
            }, cts.Token);
            if (_closed || cts.IsCancellationRequested || !ReferenceEquals(_analysisCts, cts)) return;
            _engine = result.Engine;
            _analysisCts = null;
            RefreshTargetsUi();
            StatusText.Text = $"Найдено новых циклов: {result.Added}.";
            if (result.Added > 0) ScheduleRender();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!_closed && !cts.IsCancellationRequested && ReferenceEquals(_analysisCts, cts)) StatusText.Text = ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_analysisCts, cts)) _analysisCts = null;
            cts.Dispose();
            if (!_closed) UpdateComputationState();
        }
    }

    internal async Task ShowOrbitAtAsync(System.Windows.Point canvasPoint)
    {
        NumericInput.Prepare(this);
        Task<bool> preparation = EnsurePreparedAsync();
        CancelAnalysis();
        int request = _analysisRequest;
        if (!await preparation || _closed || request != _analysisRequest) return;
        var cts = new CancellationTokenSource();
        _analysisCts = cts;
        UpdateComputationState();
        try
        {
            var (planeX, planeY) = ScreenToPlane(canvasPoint);
            BasinExplorerState state = CaptureState("orbit");
            int maxPoints = UsesPhysics || UsesPlanar ? 512 : 160;
            StatusText.Text = "Вычисление орбиты…";
            var result = await Task.Run(() =>
            {
                var engine = CreateEngine(state, cts.Token);
                return (Engine: engine, Orbit: engine.AnalyzePoint(planeX, planeY), Trace: engine.TraceOrbit(planeX, planeY, maxPoints));
            }, cts.Token);
            if (_closed || cts.IsCancellationRequested || !ReferenceEquals(_analysisCts, cts)) return;
            _orbitTrace = result.Trace;
            UpdateOverlay();
            StatusText.Text = DescribeOrbit(result.Engine, result.Orbit, planeX, planeY) +
                (SelectedMarkerMode == BasinMarkerMode.Hidden ? " Траектория скрыта: включите маркеры и линии вверху панели." : " Esc — скрыть орбиту.");
            if (!UsesRoots) SeedBox.Text = FormatComplexInput(new Complex(planeX, planeY));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!_closed && !cts.IsCancellationRequested && ReferenceEquals(_analysisCts, cts)) StatusText.Text = ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_analysisCts, cts)) _analysisCts = null;
            cts.Dispose();
            if (!_closed) UpdateComputationState();
        }
    }
}
