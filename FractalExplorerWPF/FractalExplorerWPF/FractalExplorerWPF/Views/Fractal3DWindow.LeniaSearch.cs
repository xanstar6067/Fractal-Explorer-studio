using System.Windows;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class Fractal3DWindow
{
    private readonly List<Lenia3DSettings> _leniaSearchHistory = [];
    private CancellationTokenSource? _leniaSearchCts;
    private bool _leniaSearchVariation;

    private void LeniaSearch_OnClick(object sender, RoutedEventArgs e) => _ = RunLeniaSearchAsync(false);
    private void LeniaVariation_OnClick(object sender, RoutedEventArgs e) => _ = RunLeniaSearchAsync(true);

    private async Task RunLeniaSearchAsync(bool variation)
    {
        if (_leniaSearchCts is not null) { CancelLeniaSearch(); return; }
        if (_isClosing || _suspended || Kind != Fractal3DKind.Lenia3D || _leniaPreparing || _leniaShown is null) return;
        int epoch = _leniaEpoch;
        var cts = new CancellationTokenSource(); _leniaSearchCts = cts; _leniaSearchVariation = variation;
        PauseLenia(); UpdateLeniaSearchButtons();
        LeniaSearchProgress.Value = 0;
        LeniaSearchStatus.Text = variation ? "Подбираем близкие параметры…" : "Ищем новую форму…";
        bool Current() => !_isClosing && epoch == _leniaEpoch && ReferenceEquals(_leniaSearchCts, cts);
        var progress = new Progress<LeniaSearchProgress>(p =>
        {
            if (Current() && !cts.IsCancellationRequested)
            {
                LeniaSearchProgress.Value = p.Finalizing ? 95 : 85.0 * p.Checked / p.Total;
                LeniaSearchStatus.Text = p.Finalizing ? "Проверяем длительное выживание…" : $"Проверено форм: {p.Checked} / {p.Total}";
            }
        });
        try
        {
            // Let an already submitted live batch finish; trials have their own buffers.
            while (_leniaBusy) await Task.Delay(10, cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            var current = variation ? CheckpointLenia(CaptureLenia()) : CaptureLenia() with { Field = null, Live = null };
            var host = _renderer.DeviceHost.AddRef();
            LeniaSearchResult? found;
            try { found = await Task.Run(() => Lenia3DRandomizer.Search(host, current, variation, progress, cts.Token), cts.Token); }
            finally { host.Release(); }
            cts.Token.ThrowIfCancellationRequested();
            if (!Current()) return;
            if (found is null) { LeniaSearchStatus.Text = "Устойчивых форм не встретилось. Попробуйте ещё раз или выберите готовый вид."; return; }
            var previous = CheckpointLenia(CaptureLenia());
            var settings = found.Settings with { StepsPerFrame = (int)LeniaSpeedSlider.Value,
                CutAxis = Math.Max(0, LeniaCutBox.SelectedIndex), CutPosition = LeniaCutSlider.Value };
            _leniaSearchCts = null;
            ApplyLeniaSearch(settings);
            _leniaSearchHistory.Add(previous);
            if (_leniaSearchHistory.Count > 4) _leniaSearchHistory.RemoveAt(0);
            LeniaSearchProgress.Value = 100;
            LeniaSearchStatus.Text = $"Найдена форма · проверено {found.Checked} вариантов. Выжила в пробном прогоне; продолжите развитие или попробуйте вариацию.";
        }
        catch (OperationCanceledException) { if (Current()) LeniaSearchStatus.Text = "Поиск остановлен. Поле сохранено."; }
        catch (Exception exception) { if (Current()) LeniaSearchStatus.Text = "Поиск не удался: " + exception.Message; }
        finally
        {
            if (ReferenceEquals(_leniaSearchCts, cts)) _leniaSearchCts = null;
            cts.Dispose();
            if (!_isClosing) { UpdateLeniaLabels(); UpdateLeniaSearchButtons(); }
        }
    }

    private void ApplyLeniaSearch(Lenia3DSettings settings)
    {
        _updatingUi = true;
        try { LoadLenia(settings); } finally { _updatingUi = false; }
        _renderCts?.Cancel(); ScheduleRender(immediate: true);
    }

    private void LeniaSearchUndo_OnClick(object sender, RoutedEventArgs e)
    {
        if (_leniaSearchCts is not null || _leniaSearchHistory.Count == 0) return;
        try
        {
            ApplyLeniaSearch(_leniaSearchHistory[^1]);
            _leniaSearchHistory.RemoveAt(_leniaSearchHistory.Count - 1);
            LeniaSearchStatus.Text = "Возвращена предыдущая форма и точное поле клеток.";
            LeniaSearchProgress.Value = 0;
        }
        catch (Exception exception) { LeniaSearchStatus.Text = "Не удалось вернуть форму: " + exception.Message; }
        UpdateLeniaSearchButtons();
    }

    private void CancelLeniaSearch() => _leniaSearchCts?.Cancel();

    private void UpdateLeniaSearchButtons()
    {
        if (LeniaSearchButton is null) return;
        bool searching = _leniaSearchCts is not null;
        bool ready = !_leniaPreparing && _leniaShown is not null && !_suspended && !_isClosing;
        LeniaSearchButton.Content = searching && !_leniaSearchVariation ? "Остановить поиск" : "Найти живую форму";
        LeniaVariationButton.Content = searching && _leniaSearchVariation ? "Остановить поиск" : "Вариация текущей";
        LeniaSearchButton.IsEnabled = searching ? !_leniaSearchVariation : ready;
        LeniaVariationButton.IsEnabled = searching ? _leniaSearchVariation : ready;
        LeniaSearchUndoButton.IsEnabled = !searching && !_leniaPreparing && _leniaSearchHistory.Count > 0;
        LeniaSearchProgress.Visibility = searching ? Visibility.Visible : Visibility.Collapsed;
    }
}
