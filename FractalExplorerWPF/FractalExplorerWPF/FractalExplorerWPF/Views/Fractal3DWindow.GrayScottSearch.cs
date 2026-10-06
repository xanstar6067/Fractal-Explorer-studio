using System.Windows;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

/// <summary>
/// Поиск узоров Gray–Scott 3D: кандидаты проходят пробный прогон на ГП окна
/// (<see cref="GrayScottRandomizer.Search3D"/>) вперемежку с полосами кадра, найденный вид
/// запускается как «Применить и начать заново». «Вернуть предыдущий» восстанавливает
/// точное поле и параметры до последней находки.
/// </summary>
public partial class Fractal3DWindow
{
    private const int GraySearchHistoryDepth = 4;
    private readonly List<GrayScott3DSettings> _graySearchHistory = [];
    private CancellationTokenSource? _graySearchCts;
    private bool _graySearchVariation;

    private void GraySearchRandom_OnClick(object sender, RoutedEventArgs e) => _ = RunGraySearchAsync(false);

    private void GraySearchVariation_OnClick(object sender, RoutedEventArgs e) => _ = RunGraySearchAsync(true);

    private async Task RunGraySearchAsync(bool variation)
    {
        if (_graySearchCts is not null) { _graySearchCts.Cancel(); return; }
        if (_isClosing || Kind != Fractal3DKind.GrayScott3D) return;
        var current = _graySettings with { Field = null, Live = null };
        var target = (GrayScottPatternTarget)Math.Clamp(GraySearchTargetBox.SelectedIndex, 0, 4);
        var host = _renderer.DeviceHost;
        var cts = new CancellationTokenSource(); _graySearchCts = cts; _graySearchVariation = variation;
        UpdateGraySearchButtons();
        GraySearchStatus.Text = variation ? "Ищем вариацию рядом с текущими F/K…" : "Ищем узор…";
        var progress = new Progress<GrayScottSearchProgress>(p =>
        {
            if (ReferenceEquals(_graySearchCts, cts)) GraySearchStatus.Text = $"Проверено вариантов: {p.Checked}, живых: {p.Alive}…";
        });
        try
        {
            var found = await Task.Run(() => GrayScottRandomizer.Search3D(host, current, target, variation, progress, cts.Token), cts.Token);
            if (_isClosing) return;
            cts.Token.ThrowIfCancellationRequested();
            if (found is null)
            {
                GraySearchStatus.Text = variation
                    ? "Рядом с текущими F/K живых узоров не нашлось. Попробуйте «Случайный узор»."
                    : "Живых узоров не нашлось — попробуйте ещё раз.";
                return;
            }
            // Скорость и срез могли поменять во время поиска — берём действующие.
            var settings = found.State with
            {
                StepsPerFrame = (int)GraySpeedSlider.Value, CutAxis = Math.Max(0, GrayCutBox.SelectedIndex), CutPosition = GrayCutSlider.Value
            };
            RememberGrayForUndo();
            _graySearchCts = null;
            ApplyGraySearchSettings(settings);
            string note = found.MatchesTarget ? string.Empty : " Узор выбранного вида не встретился — показан лучший из живых.";
            GraySearchStatus.Text = $"{found.Metrics.Describe(true)} · F {Format(settings.Feed)} · K {Format(settings.Kill)} · " +
                $"уровень {Format(settings.Threshold)} · проверено вариантов: {found.Checked}.{note}";
        }
        catch (OperationCanceledException)
        {
            if (!_isClosing) GraySearchStatus.Text = "Поиск остановлен.";
        }
        catch (Exception exception)
        {
            if (!_isClosing) GraySearchStatus.Text = $"Поиск не удался: {exception.Message}";
        }
        finally
        {
            if (ReferenceEquals(_graySearchCts, cts)) _graySearchCts = null;
            cts.Dispose();
            if (!_isClosing) UpdateGraySearchButtons();
        }
    }

    private void ApplyGraySearchSettings(GrayScott3DSettings settings)
    {
        _updatingUi = true;
        try { LoadGrayScott(settings); } finally { _updatingUi = false; }
        _renderCts?.Cancel(); ScheduleRender(immediate: true);
    }

    /// <summary>Точное показанное поле (одно чтение с ГП); если кадр уже заменён — исходная затравка.</summary>
    private void RememberGrayForUndo()
    {
        GrayScott3DSettings previous;
        try { previous = CheckpointGrayScott(CaptureGrayScott()); }
        catch (InvalidOperationException) { previous = CaptureGrayScott() with { Live = null, Field = _graySettings.Field }; }
        _graySearchHistory.Add(previous);
        if (_graySearchHistory.Count > GraySearchHistoryDepth) _graySearchHistory.RemoveAt(0);
    }

    private void GraySearchUndo_OnClick(object sender, RoutedEventArgs e)
    {
        if (_graySearchCts is not null || _graySearchHistory.Count == 0) return;
        var settings = _graySearchHistory[^1];
        _graySearchHistory.RemoveAt(_graySearchHistory.Count - 1);
        try
        {
            ApplyGraySearchSettings(settings);
            GraySearchStatus.Text = $"Возвращён предыдущий вид · F {Format(settings.Feed)} · K {Format(settings.Kill)}.";
        }
        catch (Exception exception) { GraySearchStatus.Text = $"Не удалось вернуть вид: {exception.Message}"; }
        UpdateGraySearchButtons();
    }

    /// <summary>Загрузка, пресет или перезапуск отменяют поиск: находка не должна их перекрыть.</summary>
    private void CancelGraySearch() => _graySearchCts?.Cancel();

    private void UpdateGraySearchButtons()
    {
        if (GraySearchButton is null) return;
        bool searching = _graySearchCts is not null;
        GraySearchButton.Content = searching && !_graySearchVariation ? "Остановить поиск" : "Случайный узор";
        GrayVariationButton.Content = searching && _graySearchVariation ? "Остановить поиск" : "Вариация";
        GraySearchButton.IsEnabled = !searching || !_graySearchVariation;
        GrayVariationButton.IsEnabled = !searching || _graySearchVariation;
        GraySearchTargetBox.IsEnabled = !searching;
        GraySearchUndoButton.IsEnabled = !searching && _graySearchHistory.Count > 0;
    }
}
