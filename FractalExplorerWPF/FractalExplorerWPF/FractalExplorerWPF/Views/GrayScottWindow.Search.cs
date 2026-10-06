using System.Windows;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

/// <summary>
/// Поиск узоров: «Случайный узор» и «Вариация» перебирают F/K с пробным прогоном
/// (<see cref="GrayScottRandomizer"/>), найденный вид запускается с новой затравкой.
/// «Вернуть предыдущий» восстанавливает поле и параметры до последней находки.
/// </summary>
public partial class GrayScottWindow
{
    private const int SearchHistoryDepth = 4;
    private readonly List<GrayScottState> _searchHistory = [];
    private CancellationTokenSource? _searchCts;
    private bool _searchVariation;

    private void SearchRandom_OnClick(object sender, RoutedEventArgs e) => _ = RunSearchAsync(false);

    private void SearchVariation_OnClick(object sender, RoutedEventArgs e) => _ = RunSearchAsync(true);

    private async Task RunSearchAsync(bool variation)
    {
        // Кнопка запущенного поиска работает как «Остановить».
        if (_searchCts is not null) { _searchCts.Cancel(); return; }
        if (_closed || _resetting || _activeState is null) return;
        var current = _activeState.Clone(includeCheckpoint: false);
        current.Palette = _paletteManager.ActivePalette.Clone();
        current.ReversePalette = ReversePaletteBox.IsChecked == true;
        var target = (GrayScottPatternTarget)Math.Clamp(SearchTargetBox.SelectedIndex, 0, 4);
        bool pickPalette = SearchPaletteBox.IsChecked == true;
        var cts = new CancellationTokenSource(); _searchCts = cts; _searchVariation = variation;
        UpdateSearchButtons();
        SearchStatus.Text = variation ? "Ищем вариацию рядом с текущими F/K…" : "Ищем узор…";
        var progress = new Progress<GrayScottSearchProgress>(p =>
        {
            if (ReferenceEquals(_searchCts, cts)) SearchStatus.Text = $"Проверено вариантов: {p.Checked}, живых: {p.Alive}…";
        });
        try
        {
            var found = await Task.Run(() => GrayScottRandomizer.Search2D(current, target, variation, progress, cts.Token), cts.Token);
            if (_closed) return;
            cts.Token.ThrowIfCancellationRequested();
            if (found is null)
            {
                SearchStatus.Text = variation
                    ? "Рядом с текущими F/K живых узоров не нашлось. Попробуйте «Случайный узор»."
                    : "Живых узоров не нашлось — попробуйте ещё раз.";
                return;
            }
            GrayScottState state = found.State;
            // Движок, сетку и буфер могли сменить во время поиска — берём действующие.
            if (_activeState is { } latest)
            {
                state.GridSize = latest.GridSize; state.Backend = latest.Backend;
                state.AutoFrameSize = latest.AutoFrameSize; state.FrameWidth = latest.FrameWidth; state.FrameHeight = latest.FrameHeight;
            }
            state.Palette = pickPalette ? PickSearchPalette(_paletteManager.ActivePalette) : _paletteManager.ActivePalette.Clone();
            RememberForSearchUndo();
            _searchCts = null;
            await InstallEngineAsync(state, true);
            if (_closed) return;
            string note = found.MatchesTarget ? string.Empty : " Узор выбранного вида не встретился — показан лучший из живых.";
            SearchStatus.Text = $"{found.Metrics.Describe(false)} · F {Format(state.Feed)} · K {Format(state.Kill)} · проверено вариантов: {found.Checked}.{note}";
        }
        catch (OperationCanceledException)
        {
            if (!_closed) SearchStatus.Text = "Поиск остановлен.";
        }
        catch (Exception exception)
        {
            if (!_closed) SearchStatus.Text = $"Поиск не удался: {exception.Message}";
        }
        finally
        {
            if (ReferenceEquals(_searchCts, cts)) _searchCts = null;
            cts.Dispose();
            if (!_closed) UpdateSearchButtons();
        }
    }

    private GrayScottPalette PickSearchPalette(GrayScottPalette active)
    {
        var choices = _paletteManager.Palettes.Where(p => p.IsBuiltIn && p.Colors.Count > 1 && p.Name != active.Name).ToList();
        return choices.Count == 0 ? active.Clone() : choices[Random.Shared.Next(choices.Count)].Clone();
    }

    private void RememberForSearchUndo()
    {
        if (_presented is null || _resetting || _activeState is null) return;
        var state = CaptureState("undo");
        state.Palette = _paletteManager.ActivePalette.Clone();
        state.ReversePalette = ReversePaletteBox.IsChecked == true;
        _searchHistory.Add(state);
        if (_searchHistory.Count > SearchHistoryDepth) _searchHistory.RemoveAt(0);
    }

    private async void SearchUndo_OnClick(object sender, RoutedEventArgs e)
    {
        if (_searchCts is not null || _resetting || _searchHistory.Count == 0) return;
        GrayScottState state = _searchHistory[^1];
        _searchHistory.RemoveAt(_searchHistory.Count - 1);
        UpdateSearchButtons();
        await InstallEngineAsync(state, _running);
        if (!_closed) SearchStatus.Text = $"Возвращён предыдущий вид · F {Format(state.Feed)} · K {Format(state.Kill)}.";
    }

    /// <summary>Пресет, перезапуск или загрузка отменяют поиск: находка не должна их перекрыть.</summary>
    private void CancelSearch() => _searchCts?.Cancel();

    private void UpdateSearchButtons()
    {
        bool searching = _searchCts is not null;
        SearchButton.Content = searching && !_searchVariation ? "Остановить поиск" : "Случайный узор";
        VariationButton.Content = searching && _searchVariation ? "Остановить поиск" : "Вариация";
        SearchButton.IsEnabled = !searching || !_searchVariation;
        VariationButton.IsEnabled = !searching || _searchVariation;
        SearchTargetBox.IsEnabled = SearchPaletteBox.IsEnabled = !searching;
        SearchUndoButton.IsEnabled = !searching && _searchHistory.Count > 0;
    }
}
