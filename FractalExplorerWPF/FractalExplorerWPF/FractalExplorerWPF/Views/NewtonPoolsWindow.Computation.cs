using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.Rendering;

namespace FractalExplorerWPF.Views;

public partial class NewtonPoolsWindow
{
    private CancellationTokenSource? _formulaCts;
    private Task<bool> _formulaTask = Task.FromResult(false);
    private bool _needsFormulaPreparation = true, _closed;

    internal Task<bool> EnsurePreparedAsync()
    {
        if (_closed) return Task.FromResult(false);
        if (_formulaCts is { IsCancellationRequested: false }) return _formulaTask;
        if (!_needsFormulaPreparation) return Task.FromResult(true);
        ApplyFormula(false);
        return _formulaTask;
    }

    private void FormulaInput_OnChanged(object sender, TextChangedEventArgs e) => _formulaCts?.Cancel();

    private void ApplyFormula(bool showMessage, IReadOnlyList<Complex>? suppliedRoots = null)
    {
        if (showMessage) NumericInput.CommitAll(this); else NumericInput.Prepare(this);
        _formulaTask = PrepareFormulaAsync(showMessage, suppliedRoots);
    }

    private async Task<bool> PrepareFormulaAsync(bool showMessage, IReadOnlyList<Complex>? suppliedRoots)
    {
        _formulaCts?.Cancel();
        _renderCts?.Cancel();
        _renderTimer.Stop();
        _needsFormulaPreparation = true;
        var cts = new CancellationTokenSource();
        _formulaCts = cts;
        CancelButton.IsEnabled = true;
        try
        {
            if (!TryReadRootSettings(out double tolerance, out double radius, out string error))
                throw new InvalidOperationException(error);
            string formula = FormulaBox.Text.Trim();
            int order = ReadHouseholderOrder();
            var mode = SelectedRootSearchMode;
            Complex[]? roots = suppliedRoots?.ToArray();
            StatusText.Text = "Поиск корней…";
            var result = await Task.Run(() =>
            {
                var engine = new NewtonPoolsEngine { HouseholderOrder = order, RootTolerance = tolerance,
                    RootSearchRadius = radius, RootSearchMode = mode };
                if (!engine.SetFormula(formula, out string debug, roots is null, cts.Token))
                    throw new InvalidOperationException(debug);
                if (roots is not null) engine.ReplaceRoots(roots);
                return (Engine: engine, Debug: debug);
            }, cts.Token);
            if (_closed || cts.IsCancellationRequested || !ReferenceEquals(_formulaCts, cts)) return false;
            _formulaCts = null;
            _formulaEngine = result.Engine;
            _appliedFormula = formula;
            _needsFormulaPreparation = false;
            DebugOutput.Text = result.Debug;
            RefreshRootUi();
            StatusText.Text = $"Формула применена. Корней: {_formulaEngine.Roots.Count}. {_formulaEngine.RootSearchStrategy}";
            ScheduleRender();
            return true;
        }
        catch (OperationCanceledException)
        {
            if (!_closed && ReferenceEquals(_formulaCts, cts)) StatusText.Text = "Поиск корней отменён";
            return false;
        }
        catch (Exception ex)
        {
            if (!_closed && !cts.IsCancellationRequested && ReferenceEquals(_formulaCts, cts))
            {
                StatusText.Text = DebugOutput.Text = ex.Message;
                if (showMessage) MessageBox.Show(this, ex.Message, "Формула или параметры", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return false;
        }
        finally
        {
            if (ReferenceEquals(_formulaCts, cts)) _formulaCts = null;
            cts.Dispose();
            if (!_closed) CancelButton.IsEnabled = _isRendering || _formulaCts is not null;
        }
    }
}
