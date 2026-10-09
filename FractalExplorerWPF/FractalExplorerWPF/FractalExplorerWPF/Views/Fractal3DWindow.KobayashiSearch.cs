using System.Numerics;
using System.Windows;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class Fractal3DWindow
{
    private readonly List<(Kobayashi3DSettings Settings, Fractal3DPose Pose)> _kobSearchHistory = [];
    private CancellationTokenSource? _kobSearchCts;
    private bool _kobSearchVariation;

    private void KobSearch_OnClick(object sender, RoutedEventArgs e) => _ = RunKobSearchAsync(false);
    private void KobVariation_OnClick(object sender, RoutedEventArgs e) => _ = RunKobSearchAsync(true);

    private async Task RunKobSearchAsync(bool variation)
    {
        if (_kobSearchCts is not null) { CancelKobSearch(); return; }
        if (_isClosing || _suspended || Kind != Fractal3DKind.Kobayashi3D || _kobShown is null ||
            _kobResetTo is not null || KobPreparationOverlay.Visibility == Visibility.Visible) return;
        int epoch = _kobEpoch;
        var cts = new CancellationTokenSource(); _kobSearchCts = cts; _kobSearchVariation = variation;
        PauseKobayashi(); UpdateKobLabels();
        KobSearchProgress.Value = 0;
        KobSearchStatus.Text = variation ? "Подбираем близкий кристалл…" : "Ищем новую форму кристалла…";
        bool Current() => !_isClosing && !_suspended && epoch == _kobEpoch && ReferenceEquals(_kobSearchCts, cts);
        var progress = new Progress<KobayashiSearchProgress>(p =>
        {
            if (!Current() || cts.IsCancellationRequested) return;
            KobSearchProgress.Value = p.Finalizing ? 85 + 15 * p.Fraction : 85.0 * (p.Checked + p.Fraction) / p.Total;
            KobSearchStatus.Text = p.Finalizing ? $"Проверка на выбранной сетке: {p.Fraction:P0}" :
                $"Проверка форм: {Math.Min(p.Checked + 1, p.Total)} / {p.Total} · {p.Fraction:P0}";
        });
        try
        {
            // Submitted steps can finish; trial simulations never touch the window's buffers.
            while (_kobBusy) await Task.Delay(10, cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            if (!Current()) return;
            var captured = CaptureKobayashi();
            var previous = await Task.Run(() => CheckpointKobayashi(captured), cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            var host = _renderer.DeviceHost.AddRef();
            KobayashiSearchResult? found;
            try { found = await Task.Run(() => Kobayashi3DRandomizer.Search(host,
                previous with { Field = null, Live = null }, variation, progress, cts.Token), cts.Token); }
            finally { host.Release(); }
            cts.Token.ThrowIfCancellationRequested();
            if (!Current()) return;
            if (found is null) { KobSearchStatus.Text = "Выразительных растущих кристаллов не встретилось. Попробуйте ещё раз."; return; }
            var settings = found.Settings with { StepsPerFrame = (int)KobSpeedSlider.Value,
                Threshold = KobThresholdSlider.Value, CutAxis = Math.Max(0, KobCutBox.SelectedIndex), CutPosition = KobCutSlider.Value };
            var previousPose = Pose;
            _kobSearchCts = null;
            ApplyKobSearch(settings, fit: KobSearchFitBox.IsChecked == true);
            _kobSearchHistory.Add((previous, previousPose));
            if (_kobSearchHistory.Count > 4) _kobSearchHistory.RemoveAt(0);
            KobSearchProgress.Value = 100;
            KobSearchStatus.Text = $"Найден кристалл · проверено {found.Checked} форм. Продолжите рост или попробуйте вариацию.";
        }
        catch (OperationCanceledException) { if (Current()) KobSearchStatus.Text = "Поиск остановлен. Кристалл сохранён."; }
        catch (Exception exception) { if (Current()) KobSearchStatus.Text = "Поиск не удался: " + exception.Message; }
        finally
        {
            if (ReferenceEquals(_kobSearchCts, cts))
            {
                _kobSearchCts = null;
                if (cts.IsCancellationRequested && !_isClosing)
                    KobSearchStatus.Text = epoch == _kobEpoch ? "Поиск остановлен. Кристалл сохранён." :
                        "Поиск остановлен: применён другой кристалл.";
            }
            cts.Dispose();
            if (!_isClosing) UpdateKobLabels();
        }
    }

    private void ApplyKobSearch(Kobayashi3DSettings settings, Fractal3DPose? pose = null, bool fit = false)
    {
        _updatingUi = true;
        try { LoadKobayashi(settings); } finally { _updatingUi = false; }
        if (pose is { } savedPose)
        { StopCameraMotion(); MoveCamera(savedPose); UpdateCameraText(); }
        else if (fit && settings.Field is { } field) FitKobField(field, settings.Threshold);
        _renderCts?.Cancel(); ScheduleRender(immediate: true);
    }

    private void KobSearchUndo_OnClick(object sender, RoutedEventArgs e)
    {
        if (_kobSearchCts is not null || _kobBusy || _kobSearchHistory.Count == 0 || _isClosing || _suspended) return;
        try
        {
            var previous = _kobSearchHistory[^1];
            ApplyKobSearch(previous.Settings, previous.Pose);
            _kobSearchHistory.RemoveAt(_kobSearchHistory.Count - 1);
            KobSearchStatus.Text = "Возвращены предыдущий кристалл, температура и точный шаг роста.";
            KobSearchProgress.Value = 0;
        }
        catch (Exception exception) { KobSearchStatus.Text = "Не удалось вернуть кристалл: " + exception.Message; }
        UpdateKobSearchButtons();
    }

    private void FitKobField(Kobayashi3DField field, double threshold)
    {
        int n = field.Size; var values = field.Concentrations;
        Vector3 min = new(float.PositiveInfinity), max = new(float.NegativeInfinity);
        for (int i = 0; i < n * n * n; i++)
        {
            if (values[i * 2] < threshold) continue;
            var p = new Vector3((i % n + .5f) * 2 / n - 1, (i / n % n + .5f) * 2 / n - 1,
                (i / (n * n) + .5f) * 2 / n - 1);
            min = Vector3.Min(min, p); max = Vector3.Max(max, p);
        }
        if (!float.IsFinite(min.X)) return;
        var center = (min + max) * .5f; double radius = 0;
        for (int i = 0; i < n * n * n; i++)
        {
            if (values[i * 2] < threshold) continue;
            var p = new Vector3((i % n + .5f) * 2 / n - 1, (i / n % n + .5f) * 2 / n - 1,
                (i / (n * n) + .5f) * 2 / n - 1);
            radius = Math.Max(radius, Vector3.Distance(p, center));
        }
        radius += 2 * Math.Sqrt(3) / n; // Trilinear boundary and half-cell margin.
        double aspect = Math.Max(.1, CanvasHost.ActualWidth / Math.Max(1, CanvasHost.ActualHeight));
        double halfFov = Math.Atan(Math.Tan(_fieldOfView * Math.PI / 360) * Math.Min(1, aspect));
        StopCameraMotion(); MoveCamera(new(_orientation, radius * 1.1 / Math.Sin(halfFov), center));
        UpdateCameraText();
    }

    private void CancelKobSearch()
    {
        if (_kobSearchCts is null) return;
        _kobSearchCts.Cancel();
        KobSearchStatus.Text = "Остановка поиска…";
    }

    private void UpdateKobSearchButtons()
    {
        if (KobSearchButton is null) return;
        bool searching = _kobSearchCts is not null;
        bool ready = _kobShown is not null && _kobResetTo is null &&
            KobPreparationOverlay.Visibility != Visibility.Visible && !_suspended && !_isClosing;
        KobSearchButton.Content = searching && !_kobSearchVariation ? "Остановить поиск" : "Найти интересную форму";
        KobVariationButton.Content = searching && _kobSearchVariation ? "Остановить поиск" : "Вариация текущей";
        KobSearchButton.IsEnabled = searching ? !_kobSearchVariation : ready;
        KobVariationButton.IsEnabled = searching ? _kobSearchVariation : ready;
        KobSearchUndoButton.IsEnabled = !searching && ready && !_kobBusy && _kobSearchHistory.Count > 0;
        KobSearchProgress.Visibility = searching ? Visibility.Visible : Visibility.Collapsed;
    }
}
