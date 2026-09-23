using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Theming;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Core.Rendering3D;

namespace FractalExplorerWPF;

/// <summary>
/// Каталог: меню разделов слева, сетка превью в центре, панель выбранного режима справа.
/// Состояние — плитки <see cref="CatalogTile"/> и пункты меню <see cref="CatalogScope"/>;
/// видимое в сетке — одно представление, которое фильтруется разделом и поиском.
/// </summary>
public partial class MainWindow : Window
{
    private readonly IReadOnlyList<FractalCatalogItem> _catalog = FractalCatalog.Create();
    private readonly List<CatalogTile> _tiles;
    private readonly ListCollectionView _galleryView;
    private readonly CatalogScope _allScope;
    private readonly HashSet<string> _favorites = FavoriteFractalsStore.Load();
    private readonly HashSet<CatalogTile> _searchMatches = [];
    private readonly CatalogPreviewLoader _previews = new();
    private readonly CancellationTokenSource _lifetime = new();
    private List<string> _recent;
    private IReadOnlyList<string> _queryTokens = [];
    private CatalogScope _scope;
    private CatalogScope? _scopeBeforeSearch;
    private CatalogTile? _selectedTile;
    private CatalogTile? _detailsTile;
    private Task? _previewRendering;
    private bool _syncingScope;
    private bool _syncingGallery;
    private bool _initializingRenderPattern = true;
    private bool _updatingThemes;

    public MainWindow()
    {
        InitializeComponent();
        CatalogLogo.Source = IconResourceLoader.LoadLargestFrame("Assets/Icons/FractalExplorer.ico");

        _tiles = CreateTiles(_catalog);
        Scopes = CatalogScope.Build(_catalog);
        _allScope = Scopes[0];
        _scope = _allScope;
        ScopeList.ItemsSource = Scopes;
        _galleryView = new ListCollectionView(_tiles) { Filter = item => item is CatalogTile tile && IsShown(tile) };
        CatalogGallery.ItemsSource = _galleryView;

        foreach (CatalogTile tile in _tiles) tile.IsFavorite = _favorites.Contains(tile.DisplayName);
        // Недавние режимы, которых больше нет в каталоге, не должны занимать места в списке.
        _recent = RecentFractalsStore.Load()
            .Where(name => _tiles.Any(tile => string.Equals(tile.DisplayName, name, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        ApplyRecentRanks();
        _previews.LoadThumbnails(_tiles);
        UpdateSearchMatches();

        int patternIndex = RenderPatternPreferenceStore.Load();
        RenderPatternSelector.SelectedIndex = patternIndex;
        RenderPatternSettings.SelectedPattern = (TileSchedulingStrategy)patternIndex;
        _initializingRenderPattern = false;

        ThemeManager.ThemeChanged += ThemeManager_OnThemeChanged;
        ThemeManager.ThemesChanged += ThemeManager_OnThemesChanged;
        Loaded += (_, _) => _previewRendering ??= RenderPendingPreviewsAsync();
        Closed += (_, _) =>
        {
            _lifetime.Cancel();
            ThemeManager.ThemeChanged -= ThemeManager_OnThemeChanged;
            ThemeManager.ThemesChanged -= ThemeManager_OnThemesChanged;
        };
        ReloadThemeSelector();

        // В деталях — последний запущенный режим, но каталог при запуске начинается сверху.
        _selectedTile = _tiles.FirstOrDefault(tile => tile.RecentRank == 0) ?? _tiles.FirstOrDefault();
        SelectScope(_allScope);
        RefreshGallery(scrollToSelection: false);
    }

    internal IReadOnlyList<CatalogTile> Tiles => _tiles;
    internal IReadOnlyList<CatalogScope> Scopes { get; }
    internal ListCollectionView GalleryView => _galleryView;
    internal CatalogScope CurrentScope => _scope;
    internal CatalogTile? SelectedTile => _detailsTile;

    // ---------- Разделы, поиск и сетка ----------

    private static List<CatalogTile> CreateTiles(IEnumerable<FractalCatalogItem> catalog)
    {
        var groups = new Dictionary<string, CatalogGroup>(StringComparer.Ordinal);
        var tiles = new List<CatalogTile>();
        foreach (FractalCatalogItem item in catalog)
        {
            if (!groups.TryGetValue(item.CategoryBreadcrumb, out CatalogGroup? group))
                groups[item.CategoryBreadcrumb] = group = new CatalogGroup(item.CategoryPath);
            tiles.Add(new CatalogTile(item, group));
        }
        return tiles;
    }

    private bool IsSearching => _queryTokens.Count > 0;

    private bool IsShown(CatalogTile tile) => _searchMatches.Contains(tile) && _scope.Includes(tile);

    private void UpdateSearchMatches()
    {
        _searchMatches.Clear();
        foreach (CatalogTile tile in _tiles)
        {
            if (CatalogSearch.Matches(tile.SearchText, _queryTokens)) _searchMatches.Add(tile);
        }
    }

    /// <summary>Выбор раздела из кода: меню подсвечивает его, сетка перестраивается отдельно.</summary>
    internal void SelectScope(CatalogScope scope)
    {
        _scope = scope;
        _syncingScope = true;
        try { ScopeList.SelectedItem = scope; }
        finally { _syncingScope = false; }
    }

    private void ScopeList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingScope) return;
        if (ScopeList.SelectedItem is not CatalogScope scope)
        {
            // Ctrl+щелчок снимает выделение — раздел при этом не меняется.
            SelectScope(_scope);
            return;
        }
        _scope = scope;
        _scopeBeforeSearch = null;
        RefreshGallery(scrollToTop: true);
    }

    /// <summary>
    /// Перестраивает сетку под текущие раздел и поиск: группы с заголовками, если видимые плитки
    /// принадлежат нескольким группам; порядок запуска в «Недавних»; сохранение выбора, если он виден.
    /// <paramref name="scrollToTop"/> — содержимое сетки сменилось целиком (другой раздел или запрос):
    /// прокрутка прежнего списка к нему не относится.
    /// </summary>
    internal void RefreshGallery(bool scrollToTop = false, bool scrollToSelection = true)
    {
        List<CatalogTile> shown = _tiles.Where(IsShown).ToList();
        bool recent = _scope.Kind == CatalogScopeKind.Recent;
        bool grouped = !recent && shown.Select(tile => tile.Group).Distinct().Skip(1).Any();

        CatalogTile? selection;
        _syncingGallery = true;
        try
        {
            using (_galleryView.DeferRefresh())
            {
                _galleryView.GroupDescriptions.Clear();
                _galleryView.SortDescriptions.Clear();
                if (grouped)
                    _galleryView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(CatalogTile.Group)));
                if (recent)
                    _galleryView.SortDescriptions.Add(new SortDescription(nameof(CatalogTile.RecentRank), ListSortDirection.Ascending));
            }
            selection = _selectedTile is not null && shown.Contains(_selectedTile)
                ? _selectedTile
                : _galleryView.Cast<CatalogTile>().FirstOrDefault();
            CatalogGallery.SelectedItem = selection;
        }
        finally { _syncingGallery = false; }

        ShowDetails(selection);
        if (scrollToTop) FindGalleryScrollViewer()?.ScrollToTop();
        if (scrollToSelection && selection is not null) BringTileIntoViewAfterLayout(selection);
        UpdateScopeCounts();
        UpdateGalleryHeader(shown.Count);
        UpdateEmptyState(shown.Count);
    }

    /// <summary>
    /// Плитки создаются при раскладке, поэтому прокрутка к выбранной — после неё. Со сменой выбора
    /// к этому моменту запрос устаревает.
    /// </summary>
    private void BringTileIntoViewAfterLayout(CatalogTile tile) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (!ReferenceEquals(_detailsTile, tile)) return;
            CatalogGallery.UpdateLayout();
            (CatalogGallery.ItemContainerGenerator.ContainerFromItem(tile) as FrameworkElement)?.BringIntoView();
        });

    private ScrollViewer? FindGalleryScrollViewer()
    {
        var pending = new Queue<DependencyObject>([CatalogGallery]);
        while (pending.Count > 0)
        {
            DependencyObject current = pending.Dequeue();
            if (current is ScrollViewer scrollViewer) return scrollViewer;
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(current); index++)
                pending.Enqueue(VisualTreeHelper.GetChild(current, index));
        }
        return null;
    }

    private void UpdateScopeCounts()
    {
        foreach (CatalogScope scope in Scopes)
        {
            int count = _tiles.Count(tile => _searchMatches.Contains(tile) && scope.Includes(tile));
            scope.Count = count;
            scope.IsDimmed = IsSearching && count == 0;
        }
    }

    private void UpdateGalleryHeader(int shownCount)
    {
        GalleryTitle.Text = _scope.Title;
        string parent = _scope.Kind == CatalogScopeKind.Category && _scope.Path.Count > 1
            ? string.Join(" › ", _scope.Path.Take(_scope.Path.Count - 1)) + " · "
            : _scope.Kind == CatalogScopeKind.Recent ? "Последние запущенные · " : string.Empty;
        string found = $"найдено {CatalogSearch.CountModes(shownCount)} по запросу «{SearchBox.Text.Trim()}»";
        GallerySubtitle.Text = IsSearching
            ? parent.Length == 0 ? char.ToUpperInvariant(found[0]) + found[1..] : parent + found
            : parent + CatalogSearch.CountModes(shownCount);
    }

    private void UpdateEmptyState(int shownCount)
    {
        bool empty = shownCount == 0;
        GalleryEmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        CatalogGallery.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        if (!empty) return;

        (string glyph, string title, string text, string? action) = (IsSearching, _scope.Kind) switch
        {
            (true, CatalogScopeKind.All) => ("\uE721", "Ничего не найдено",
                $"По запросу «{SearchBox.Text.Trim()}» режимов нет. Поиск идёт по названиям, описаниям и разделам.", "Сбросить поиск"),
            (true, _) => ("\uE721", "В этом разделе ничего не найдено",
                $"По запросу «{SearchBox.Text.Trim()}» в разделе «{_scope.Title}» режимов нет.", "Искать во всех режимах"),
            (false, CatalogScopeKind.Favorites) => ("\uE734", "В избранном пока пусто",
                "Отметьте режим звёздой в панели справа или через меню плитки по правой кнопке мыши.", null),
            (false, CatalogScopeKind.Recent) => ("\uE823", "Недавних запусков нет",
                "Здесь появятся режимы, которые вы открывали.", "Показать все режимы"),
            _ => ("\uE8A9", "Раздел пуст", "В этом разделе нет режимов.", "Показать все режимы")
        };
        GalleryEmptyGlyph.Text = glyph;
        GalleryEmptyTitle.Text = title;
        GalleryEmptyText.Text = text;
        GalleryEmptyAction.Content = action;
        GalleryEmptyAction.Visibility = action is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void GalleryEmptyAction_OnClick(object sender, RoutedEventArgs e)
    {
        if (IsSearching && _scope.Kind == CatalogScopeKind.All)
        {
            SearchBox.Clear();
            SearchBox.Focus();
            return;
        }
        _scopeBeforeSearch = null;
        SelectScope(_allScope);
        RefreshGallery(scrollToTop: true);
    }

    private void SearchBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearSearchButton.Visibility = SearchBox.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

        IReadOnlyList<string> tokens = CatalogSearch.Tokenize(SearchBox.Text);
        bool wasSearching = IsSearching;
        bool searching = tokens.Count > 0;
        // Поиск идёт по всему каталогу; после очистки поля возвращается раздел, открытый до него,
        // если пользователь не выбрал другой во время поиска.
        if (!wasSearching && searching && _scope != _allScope)
        {
            _scopeBeforeSearch = _scope;
            SelectScope(_allScope);
        }
        else if (wasSearching && !searching && _scopeBeforeSearch is not null)
        {
            SelectScope(_scopeBeforeSearch);
            _scopeBeforeSearch = null;
        }

        _queryTokens = tokens;
        UpdateSearchMatches();
        RefreshGallery(scrollToTop: true);
    }

    private void ClearSearchButton_OnClick(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        SearchBox.Focus();
    }

    private void SearchBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                e.Handled = FocusSelectedTile();
                break;
            case Key.Enter:
                Launch(_detailsTile);
                e.Handled = true;
                break;
            case Key.Escape when SearchBox.Text.Length > 0:
                SearchBox.Clear();
                e.Handled = true;
                break;
        }
    }

    private bool FocusSelectedTile()
    {
        if (_detailsTile is null) return false;
        CatalogGallery.ScrollIntoView(_detailsTile);
        CatalogGallery.UpdateLayout();
        return CatalogGallery.ItemContainerGenerator.ContainerFromItem(_detailsTile) is ListBoxItem container && container.Focus();
    }

    private void CatalogGallery_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingGallery) return;
        ShowDetails(CatalogGallery.SelectedItem as CatalogTile);
    }

    private void ShowDetails(CatalogTile? tile)
    {
        if (_detailsTile is not null && !ReferenceEquals(_detailsTile, tile))
            _previews.HideFromDetails(_detailsTile);
        _detailsTile = tile;
        if (tile is not null)
        {
            _selectedTile = tile;
            _previews.ShowInDetails(tile);
        }
        DetailsContent.DataContext = tile;
        DetailsContent.Visibility = tile is null ? Visibility.Collapsed : Visibility.Visible;
        DetailsEmptyText.Visibility = tile is null ? Visibility.Visible : Visibility.Collapsed;
        if (tile is not null) DetailsContent.ScrollToTop();
    }

    private void CatalogGallery_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            Launch(_detailsTile);
            e.Handled = true;
        }
    }

    private void CatalogGallery_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (ItemsControl.ContainerFromElement(CatalogGallery, e.OriginalSource as DependencyObject) is ListBoxItem { DataContext: CatalogTile tile })
        {
            Launch(tile);
            e.Handled = true;
        }
    }

    internal Task RenderPendingPreviewsAsync() => RenderPendingPreviewsCoreAsync(_lifetime.Token);

    private async Task RenderPendingPreviewsCoreAsync(CancellationToken token)
    {
        try { await _previews.RenderPendingAsync(_tiles, token); }
        catch (OperationCanceledException) { }
    }

    // ---------- Избранное и недавние ----------

    internal void ToggleFavorite(CatalogTile tile)
    {
        if (!_favorites.Remove(tile.DisplayName)) _favorites.Add(tile.DisplayName);
        tile.IsFavorite = _favorites.Contains(tile.DisplayName);
        FavoriteFractalsStore.Save(_favorites);
        if (_scope.Kind == CatalogScopeKind.Favorites) RefreshGallery();
        else UpdateScopeCounts();
    }

    internal void RecordLaunch(CatalogTile tile)
    {
        _recent = RecentFractalsStore.Push(_recent, tile.DisplayName);
        RecentFractalsStore.Save(_recent);
        ApplyRecentRanks();
        if (_scope.Kind == CatalogScopeKind.Recent) RefreshGallery();
        else UpdateScopeCounts();
    }

    private void ApplyRecentRanks()
    {
        foreach (CatalogTile tile in _tiles)
            tile.RecentRank = _recent.FindIndex(name => string.Equals(name, tile.DisplayName, StringComparison.OrdinalIgnoreCase));
    }

    private void FavoriteButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_detailsTile is not null) ToggleFavorite(_detailsTile);
    }

    private void TileContextMenu_OnOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu { DataContext: CatalogTile tile } menu) return;
        if (menu.Items[0] is MenuItem launch) launch.IsEnabled = tile.CanLaunch;
        if (menu.Items[1] is MenuItem favorite)
            favorite.Header = tile.IsFavorite ? "Убрать из избранного" : "Добавить в избранное";
    }

    private void TileLaunchMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CatalogTile tile }) Launch(tile);
    }

    private void TileFavoriteMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CatalogTile tile }) ToggleFavorite(tile);
    }

    // ---------- Запуск ----------

    private void LaunchButton_OnClick(object sender, RoutedEventArgs e) => Launch(_detailsTile);

    private void Launch(CatalogTile? tile)
    {
        if (tile is null || GetWindowFactory(tile.Item.LaunchKey) is not { } createWindow) return;
        Window window = createWindow();
        window.Owner = this;
        window.Show();
        RecordLaunch(tile);
    }

    /// <summary>Окно режима по ключу запуска каталога; <c>null</c> — ключ ничему не соответствует.</summary>
    internal static Func<Window>? GetWindowFactory(string? launchKey)
    {
        if (string.IsNullOrEmpty(launchKey)) return null;
        if (MathematicalLaboratoryCatalog.TryParseLaunchKey(launchKey, out MathematicalLaboratoryKind laboratoryKind))
            return () => new MathematicalLaboratoryWindow(laboratoryKind);
        if (BasinExplorerCatalog.TryParseLaunchKey(launchKey, out BasinExplorerKind basinKind))
            return () => new BasinExplorerWindow(basinKind);
        if (Fractal3DCatalog.TryParseLaunchKey(launchKey, out Fractal3DKind fractal3DKind))
            return () => new Fractal3DWindow(fractal3DKind);

        Func<Window>? named = launchKey switch
        {
            "JuliaGallery" => () => new JuliaGalleryWindow(MandelbrotVariant.Julia),
            "JuliaBurningShipGallery" => () => new JuliaGalleryWindow(MandelbrotVariant.JuliaBurningShip),
            "LSystem" or "Serpinsky" => () => new LSystemWindow(),
            "SerpinskyChaos" => () => new SerpinskyWindow(chaosOnly: true),
            "NewtonPools" => () => new NewtonPoolsWindow(),
            "Phoenix" => () => new PhoenixWindow(),
            "Collatz" => () => new CollatzWindow(),
            "InverseCollatzTree" => () => new InverseCollatzTreeWindow(),
            "DomainColoring" => () => new DomainColoringWindow(),
            "NovaMandelbrot" => () => new NovaWindow(NovaVariant.Mandelbrot),
            "NovaJulia" => () => new NovaWindow(NovaVariant.Julia),
            "Buddhabrot" => () => new BuddhabrotWindow(),
            "Flame" => () => new FlameWindow(),
            "IFS" => () => new IfsWindow(),
            "ApollonianGasket" => () => new ApollonianWindow(),
            "DLA" => () => new DlaWindow(),
            "GrayScott" => () => new GrayScottWindow(),
            _ => null
        };
        if (named is not null) return named;

        if (Enum.TryParse(launchKey, out DynamicSystemKind dynamicSystem))
            return () => new DynamicSystemWindow(dynamicSystem);
        if (Enum.TryParse(launchKey, out MandelbrotVariant variant))
            return () => new MandelbrotWindow(variant);
        return null;
    }

    private void OpenQuickSwitcher()
    {
        var dialog = new QuickSwitcherWindow(_catalog) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SelectedItem is not { } item) return;
        CatalogTile? tile = _tiles.FirstOrDefault(candidate => ReferenceEquals(candidate.Item, item));
        if (tile is null) return;

        RevealTile(tile);
        Launch(tile);
    }

    internal void RevealTile(CatalogTile tile)
    {
        _scopeBeforeSearch = null;
        if (SearchBox.Text.Length > 0) SearchBox.Clear();
        if (!_scope.Includes(tile)) SelectScope(_allScope);
        // Очистка поиска перестраивает старый раздел и может заменить выбор его первой плиткой.
        _selectedTile = tile;
        RefreshGallery(scrollToTop: true);
    }

    private void QuickSwitcherHint_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => OpenQuickSwitcher();

    // ---------- Верхняя панель и настройки ----------

    private void CloudButton_OnClick(object sender, RoutedEventArgs e) => CloudSaveManagerWindow.Open(this);

    private void AboutButton_OnClick(object sender, RoutedEventArgs e)
    {
        new AboutWindow { Owner = this }.ShowDialog();
    }

    private void SettingsToggle_OnChecked(object sender, RoutedEventArgs e) =>
        Dispatcher.BeginInvoke(() => RenderPatternSelector.Focus(), DispatcherPriority.Input);

    private void CloseSettings() => SettingsToggle.IsChecked = false;

    private void RenderPatternSelector_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RenderPatternSelector.SelectedIndex is >= 0 and <= 7)
        {
            RenderPatternSettings.SelectedPattern =
                (TileSchedulingStrategy)RenderPatternSelector.SelectedIndex;
            if (!_initializingRenderPattern)
                RenderPatternPreferenceStore.Save(RenderPatternSelector.SelectedIndex);
        }
    }

    private void ReloadThemeSelector()
    {
        _updatingThemes = true;
        IReadOnlyList<ThemeDefinition> themes = ThemeManager.GetAllThemes();
        ThemeSelector.ItemsSource = themes;
        ThemeSelector.SelectedItem = themes.FirstOrDefault(theme =>
            string.Equals(theme.Id, ThemeManager.CurrentThemeId, StringComparison.OrdinalIgnoreCase));
        _updatingThemes = false;
    }

    private void ThemeSelector_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updatingThemes && ThemeSelector.SelectedItem is ThemeDefinition theme)
            ThemeManager.SetTheme(theme.Id);
    }

    private void ThemeEditor_OnClick(object sender, RoutedEventArgs e)
    {
        CloseSettings();
        new ThemeEditorWindow { Owner = this }.ShowDialog();
        ReloadThemeSelector();
    }

    private async void RebuildShadersButton_OnClick(object sender, RoutedEventArgs e)
    {
        RebuildShadersButton.IsEnabled = false;
        ShaderCacheStatus.Visibility = Visibility.Visible;
        ShaderCacheStatus.Text = "Компиляция шейдеров… Это может занять несколько минут.";
        IProgress<(int Completed, int Total)> progress = new Progress<(int Completed, int Total)>(value =>
            ShaderCacheStatus.Text = $"Скомпилировано {value.Completed} из {value.Total} шейдеров…");
        try
        {
            await Task.Run(() => Fractal3DRenderer.RebuildShaderCache((completed, total, _) =>
                progress.Report((completed, total))));
            ShaderCacheStatus.Text = "Готово: кэш всех 3D-шейдеров обновлён.";
        }
        catch (Exception exception)
        {
            CrashLogger.Log("MainWindow.RebuildShaders", exception);
            ShaderCacheStatus.Text = "Не удалось пересобрать шейдеры.";
            MessageBox.Show(this, exception.Message, "Пересборка шейдеров",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            RebuildShadersButton.IsEnabled = true;
        }
    }

    private void ThemeManager_OnThemeChanged(object? sender, EventArgs e) => ReloadThemeSelector();
    private void ThemeManager_OnThemesChanged(object? sender, EventArgs e) => ReloadThemeSelector();

    private void MainWindow_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
        {
            CloseSettings();
            OpenQuickSwitcher();
            e.Handled = true;
        }
        else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            CloseSettings();
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && SettingsToggle.IsChecked == true)
        {
            CloseSettings();
            SettingsToggle.Focus();
            e.Handled = true;
        }
    }

    /// <summary>Щелчок мимо панели настроек закрывает её. Выпадающие списки панели живут в своих окнах — их щелчки не считаются.</summary>
    private void MainWindow_OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (SettingsToggle.IsChecked != true || e.OriginalSource is not DependencyObject source) return;
        if (PresentationSource.FromDependencyObject(source) != PresentationSource.FromVisual(this)) return;
        if (IsWithin(source, SettingsFlyout) || IsWithin(source, SettingsToggle)) return;
        CloseSettings();
    }

    private static bool IsWithin(DependencyObject element, DependencyObject container)
    {
        for (DependencyObject? current = element; current is not null;
             current = current is Visual or System.Windows.Media.Media3D.Visual3D
                 ? VisualTreeHelper.GetParent(current)
                 : LogicalTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, container)) return true;
        }
        return false;
    }
}
