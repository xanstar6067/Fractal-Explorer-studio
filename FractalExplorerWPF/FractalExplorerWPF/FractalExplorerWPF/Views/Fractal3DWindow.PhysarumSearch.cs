using System.Windows;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class Fractal3DWindow
{
    private readonly List<Physarum3DSettings> _physarumSearchHistory = [];
    private CancellationTokenSource? _physarumSearchCts;
    private bool _physarumSearchVariation;

    private void PhysarumSearch_OnClick(object sender, RoutedEventArgs e) => _ = RunPhysarumSearchAsync(false);
    private void PhysarumVariation_OnClick(object sender, RoutedEventArgs e) => _ = RunPhysarumSearchAsync(true);

    private async Task RunPhysarumSearchAsync(bool variation)
    {
        if (_physarumSearchCts is not null) { CancelPhysarumSearch(); return; }
        if (_isClosing || _suspended || Kind != Fractal3DKind.Physarum3D || _physarumPreparing || _physarumShown is null) return;
        int epoch = _physarumEpoch;
        var cts = new CancellationTokenSource(); _physarumSearchCts = cts; _physarumSearchVariation = variation;
        PausePhysarum(); UpdatePhysarumSearchButtons();
        PhysarumSearchProgress.Value = 0;
        PhysarumSearchStatus.Text = variation ? "Подбираем близкие параметры…" : "Ищем новую форму…";
        bool Current() => !_isClosing && epoch == _physarumEpoch && ReferenceEquals(_physarumSearchCts, cts);
        var progress = new Progress<PhysarumSearchProgress>(p =>
        {
            if (Current() && !cts.IsCancellationRequested)
            {
                PhysarumSearchProgress.Value = p.Finalizing ? 95 : 85.0 * p.Checked / p.Total;
                PhysarumSearchStatus.Text = p.Finalizing ? "Плетём выбранную сеть в полном размере…" : $"Проверено форм: {p.Checked} / {p.Total}";
            }
        });
        try
        {
            // Let an already submitted live batch finish; trials have their own buffers.
            while (_physarumBusy) await Task.Delay(10, cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            var current = CapturePhysarum() with { Field = null, Live = null };
            var host = _renderer.DeviceHost.AddRef();
            PhysarumSearchResult? found;
            try { found = await Task.Run(() => Physarum3DRandomizer.Search(host, current, variation, progress, cts.Token), cts.Token); }
            finally { host.Release(); }
            cts.Token.ThrowIfCancellationRequested();
            if (!Current()) return;
            if (found is null) { PhysarumSearchStatus.Text = "Выразительных сетей не встретилось. Попробуйте ещё раз."; return; }
            var previous = CheckpointPhysarum(CapturePhysarum());
            var settings = found.Settings with { StepsPerFrame = (int)PhysarumSpeedSlider.Value,
                CutAxis = Math.Max(0, PhysarumCutBox.SelectedIndex), CutPosition = PhysarumCutSlider.Value };
            _physarumSearchCts = null;
            ApplyPhysarumSearch(settings);
            _physarumSearchHistory.Add(previous);
            if (_physarumSearchHistory.Count > 4) _physarumSearchHistory.RemoveAt(0);
            PhysarumSearchProgress.Value = 100;
            PhysarumSearchStatus.Text = $"Найдена сеть · проверено {found.Checked} форм. Продолжите развитие или попробуйте вариацию.";
        }
        catch (OperationCanceledException) { if (Current()) PhysarumSearchStatus.Text = "Поиск остановлен. Сеть сохранена."; }
        catch (Exception exception) { if (Current()) PhysarumSearchStatus.Text = "Поиск не удался: " + exception.Message; }
        finally
        {
            if (ReferenceEquals(_physarumSearchCts, cts)) _physarumSearchCts = null;
            cts.Dispose();
            if (!_isClosing) { UpdatePhysarumLabels(); UpdatePhysarumSearchButtons(); }
        }
    }

    private void ApplyPhysarumSearch(Physarum3DSettings settings)
    {
        _updatingUi = true;
        try { LoadPhysarum(settings); } finally { _updatingUi = false; }
        _renderCts?.Cancel(); ScheduleRender(immediate: true);
    }

    private void PhysarumSearchUndo_OnClick(object sender, RoutedEventArgs e)
    {
        if (_physarumSearchCts is not null || _physarumSearchHistory.Count == 0) return;
        try
        {
            ApplyPhysarumSearch(_physarumSearchHistory[^1]);
            _physarumSearchHistory.RemoveAt(_physarumSearchHistory.Count - 1);
            PhysarumSearchStatus.Text = "Возвращена предыдущая сеть и точное состояние агентов.";
            PhysarumSearchProgress.Value = 0;
        }
        catch (Exception exception) { PhysarumSearchStatus.Text = "Не удалось вернуть сеть: " + exception.Message; }
        UpdatePhysarumSearchButtons();
    }

    private void CancelPhysarumSearch() => _physarumSearchCts?.Cancel();

    private void UpdatePhysarumSearchButtons()
    {
        if (PhysarumSearchButton is null) return;
        bool searching = _physarumSearchCts is not null;
        bool ready = !_physarumPreparing && _physarumShown is not null && !_suspended && !_isClosing;
        PhysarumSearchButton.Content = searching && !_physarumSearchVariation ? "Остановить поиск" : "Найти интересную форму";
        PhysarumVariationButton.Content = searching && _physarumSearchVariation ? "Остановить поиск" : "Вариация текущей";
        PhysarumSearchButton.IsEnabled = searching ? !_physarumSearchVariation : ready;
        PhysarumVariationButton.IsEnabled = searching ? _physarumSearchVariation : ready;
        PhysarumSearchUndoButton.IsEnabled = !searching && !_physarumPreparing && _physarumSearchHistory.Count > 0;
        PhysarumSearchProgress.Visibility = searching ? Visibility.Visible : Visibility.Collapsed;
    }
}
