using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Theming;
using FractalExplorerWPF.Core.Rendering;

namespace FractalExplorerWPF;

public partial class MainWindow : Window
{
    private void CloudButton_OnClick(object sender, RoutedEventArgs e) => CloudSaveManagerWindow.Open(this);
    private readonly IReadOnlyList<FractalCatalogItem> _catalog = FractalCatalog.Create();
    private readonly HashSet<string> _favorites = FavoriteFractalsStore.Load();
    private FractalCatalogItem? _selectedItem;
    private bool _initializingRenderPattern = true;
    private bool _updatingThemes;
    private readonly Dictionary<MathematicalLaboratoryKind, BitmapSource> _laboratoryPreviews = [];
    private BitmapSource? _grayScottPreview;
    private CancellationTokenSource? _previewCts;

    public MainWindow()
    {
        InitializeComponent();
        int patternIndex = RenderPatternPreferenceStore.Load();
        RenderPatternSelector.SelectedIndex = patternIndex;
        RenderPatternSettings.SelectedPattern = (TileSchedulingStrategy)patternIndex;
        _initializingRenderPattern = false;
        ThemeManager.ThemeChanged += ThemeManager_OnThemeChanged;
        ThemeManager.ThemesChanged += ThemeManager_OnThemesChanged;
        Closed += (_, _) =>
        {
            _previewCts?.Cancel();
            _previewCts?.Dispose();
            ThemeManager.ThemeChanged -= ThemeManager_OnThemeChanged;
            ThemeManager.ThemesChanged -= ThemeManager_OnThemesChanged;
        };
        ReloadThemeSelector();
        ApplyCatalogFilter(null);
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
        new ThemeEditorWindow { Owner = this }.ShowDialog();
        ReloadThemeSelector();
    }

    private void AboutButton_OnClick(object sender, RoutedEventArgs e)
    {
        new AboutWindow { Owner = this }.ShowDialog();
    }

    private void ThemeManager_OnThemeChanged(object? sender, EventArgs e) => ReloadThemeSelector();
    private void ThemeManager_OnThemesChanged(object? sender, EventArgs e) => ReloadThemeSelector();

    private void SearchBox_OnTextChanged(object sender, TextChangedEventArgs e) =>
        ApplyCatalogFilter(SearchBox.Text);

    private void ApplyCatalogFilter(string? query)
    {
        query = query?.Trim();
        bool hasQuery = !string.IsNullOrEmpty(query);
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;

        IEnumerable<FractalCatalogItem> source = hasQuery
            ? _catalog.Where(item => item.DisplayName.Contains(query!, StringComparison.OrdinalIgnoreCase))
            : _catalog;

        FractalTree.Items.Clear();

        if (!hasQuery && _favorites.Count > 0)
        {
            List<FractalCatalogItem> favoriteItems =
                _catalog.Where(item => _favorites.Contains(item.DisplayName)).ToList();
            if (favoriteItems.Count > 0)
            {
                var favoriteNode = new TreeViewItem { Header = "★ Избранное", IsExpanded = true };
                foreach (FractalCatalogItem item in favoriteItems)
                    favoriteNode.Items.Add(CreateLeafNode(item));
                FractalTree.Items.Add(favoriteNode);
            }
        }

        AddCatalogLevel(FractalTree.Items, source, 0, hasQuery);

        EmptySearchText.Visibility = FractalTree.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (FractalTree.Items.Count > 0 && FractalTree.Items[0] is TreeViewItem firstCategory)
        {
            TrySelectFirstCatalogItem(firstCategory);
        }
    }

    private void AddCatalogLevel(
        ItemCollection target,
        IEnumerable<FractalCatalogItem> items,
        int depth,
        bool expandAll)
    {
        foreach (IGrouping<string?, FractalCatalogItem> branch in items.GroupBy(item =>
                     depth < item.CategoryPath.Count ? item.CategoryPath[depth] : null))
        {
            if (branch.Key is null)
            {
                foreach (FractalCatalogItem item in branch)
                {
                    target.Add(CreateLeafNode(item));
                }

                continue;
            }

            var categoryNode = new TreeViewItem { Header = branch.Key, IsExpanded = expandAll };
            AddCatalogLevel(categoryNode.Items, branch, depth + 1, expandAll);
            target.Add(categoryNode);
        }
    }

    private TreeViewItem CreateLeafNode(FractalCatalogItem item)
    {
        bool isFavorite = _favorites.Contains(item.DisplayName);
        var node = new TreeViewItem
        {
            Header = isFavorite ? "★ " + item.DisplayName : item.DisplayName,
            Tag = item
        };

        var toggleFavorite = new MenuItem();
        toggleFavorite.Click += (_, _) => ToggleFavorite(item);
        var contextMenu = new ContextMenu();
        contextMenu.Items.Add(toggleFavorite);
        contextMenu.Opened += (_, _) => toggleFavorite.Header = _favorites.Contains(item.DisplayName)
            ? "Убрать из избранного"
            : "Добавить в избранное";
        node.ContextMenu = contextMenu;

        return node;
    }

    private void ToggleFavorite(FractalCatalogItem item)
    {
        if (!_favorites.Remove(item.DisplayName))
            _favorites.Add(item.DisplayName);
        FavoriteFractalsStore.Save(_favorites);
        ApplyCatalogFilter(SearchBox.Text);
    }

    private void MainWindow_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
        {
            OpenQuickSwitcher();
            e.Handled = true;
        }
    }

    private void QuickSwitcherHint_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        OpenQuickSwitcher();

    private void OpenQuickSwitcher()
    {
        var dialog = new QuickSwitcherWindow(_catalog) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.SelectedItem is { } item)
        {
            _selectedItem = item;
            FractalName.Text = item.DisplayName;
            FractalDescription.Text = item.Description;
            LaunchButton.IsEnabled = item.LaunchKey is not null;
            LaunchButton.Content = "Запустить";
            LaunchSelected();
        }
    }

    private static bool TrySelectFirstCatalogItem(TreeViewItem node)
    {
        if (node.Tag is FractalCatalogItem)
        {
            node.IsSelected = true;
            return true;
        }

        foreach (object child in node.Items)
        {
            if (child is TreeViewItem childNode && TrySelectFirstCatalogItem(childNode))
            {
                node.IsExpanded = true;
                return true;
            }
        }

        return false;
    }

    private void FractalTree_OnSelectedItemChanged(
        object sender,
        RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not TreeViewItem { Tag: FractalCatalogItem item })
        {
            return;
        }

        FractalName.Text = item.DisplayName;
        FractalDescription.Text = item.Description;
        _selectedItem = item;
        LaunchButton.IsEnabled = item.LaunchKey is not null;
        LaunchButton.Content = item.LaunchKey is not null
            ? "Запустить"
            : "Запуск будет перенесён позже";
        if (MathematicalLaboratoryCatalog.TryParseLaunchKey(item.LaunchKey, out MathematicalLaboratoryKind kind))
        {
            _ = LoadLaboratoryPreviewAsync(item, kind);
        }
        else if (item.LaunchKey == "GrayScott")
        {
            _ = LoadGrayScottPreviewAsync(item);
        }
        else
        {
            _previewCts?.Cancel();
            FractalPreview.Source = new BitmapImage(
                new Uri($"pack://application:,,,/{item.PreviewResourcePath}", UriKind.Absolute));
        }
    }

    private async Task LoadGrayScottPreviewAsync(FractalCatalogItem item)
    {
        _previewCts?.Cancel();
        _previewCts?.Dispose();
        _previewCts = new CancellationTokenSource();
        CancellationToken token = _previewCts.Token;
        if (_grayScottPreview is not null)
        {
            if (ReferenceEquals(_selectedItem, item)) FractalPreview.Source = _grayScottPreview;
            return;
        }
        try
        {
            GrayScottState state = GrayScottPresets.All[0].State.Clone();
            BitmapSource preview = await GrayScottRenderer.RenderPreviewAsync(state, 512, 512, token);
            if (token.IsCancellationRequested || !ReferenceEquals(_selectedItem, item)) return;
            _grayScottPreview = preview;
            FractalPreview.Source = preview;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            if (ReferenceEquals(_selectedItem, item))
                FractalPreview.Source = new BitmapImage(
                    new Uri($"pack://application:,,,/{item.PreviewResourcePath}", UriKind.Absolute));
        }
    }

    private async Task LoadLaboratoryPreviewAsync(
        FractalCatalogItem item, MathematicalLaboratoryKind kind)
    {
        _previewCts?.Cancel();
        _previewCts?.Dispose();
        _previewCts = new CancellationTokenSource();
        CancellationToken token = _previewCts.Token;
        if (_laboratoryPreviews.TryGetValue(kind, out BitmapSource? cached))
        {
            if (ReferenceEquals(_selectedItem, item)) FractalPreview.Source = cached;
            return;
        }
        try
        {
            MathematicalLaboratoryState state = MathematicalLaboratoryCatalog.CreateDefaultState(kind);
            BitmapSource preview = await MathematicalLaboratoryRenderer.RenderBitmapAsync(state, 512, 512, token);
            if (token.IsCancellationRequested || !ReferenceEquals(_selectedItem, item)) return;
            _laboratoryPreviews[kind] = preview;
            FractalPreview.Source = preview;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            if (ReferenceEquals(_selectedItem, item))
                FractalPreview.Source = new BitmapImage(
                    new Uri($"pack://application:,,,/{item.PreviewResourcePath}", UriKind.Absolute));
        }
    }

    private void FractalTree_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DependencyObject? element = e.OriginalSource as DependencyObject;

        while (element is not null)
        {
            if (element is Border { Name: "HeaderBorder", TemplatedParent: TreeViewItem item } && item.HasItems)
            {
                item.IsExpanded = !item.IsExpanded;
                e.Handled = true;
                return;
            }

            element = VisualTreeHelper.GetParent(element);
        }
    }

    private void LaunchButton_OnClick(object sender, RoutedEventArgs e) => LaunchSelected();

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

    private void FractalTree_OnMouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_selectedItem?.LaunchKey is not null)
        {
            LaunchSelected();
            e.Handled = true;
        }
    }

    private void LaunchSelected()
    {
        if (MathematicalLaboratoryCatalog.TryParseLaunchKey(
                _selectedItem?.LaunchKey, out MathematicalLaboratoryKind laboratoryKind))
        {
            new MathematicalLaboratoryWindow(laboratoryKind) { Owner = this }.Show();
            return;
        }

        if (_selectedItem?.LaunchKey == "JuliaGallery")
        {
            new JuliaGalleryWindow(MandelbrotVariant.Julia) { Owner = this }.Show();
            return;
        }

        if (_selectedItem?.LaunchKey == "JuliaBurningShipGallery")
        {
            new JuliaGalleryWindow(MandelbrotVariant.JuliaBurningShip) { Owner = this }.Show();
            return;
        }

        if (_selectedItem?.LaunchKey is "LSystem" or "Serpinsky")
        {
            new LSystemWindow { Owner = this }.Show();
            return;
        }

        if (_selectedItem?.LaunchKey == "SerpinskyChaos")
        {
            new SerpinskyWindow(chaosOnly: true) { Owner = this }.Show();
            return;
        }

        if (BasinExplorerCatalog.TryParseLaunchKey(_selectedItem?.LaunchKey, out BasinExplorerKind basinKind))
        {
            new BasinExplorerWindow(basinKind) { Owner = this }.Show();
            return;
        }

        if (_selectedItem?.LaunchKey == "NewtonPools")
        {
            new NewtonPoolsWindow { Owner = this }.Show();
            return;
        }

        if (_selectedItem?.LaunchKey == "Phoenix")
        {
            new PhoenixWindow { Owner = this }.Show();
            return;
        }

        if (_selectedItem?.LaunchKey == "Collatz")
        {
            new CollatzWindow { Owner = this }.Show();
            return;
        }

        if (_selectedItem?.LaunchKey == "InverseCollatzTree")
        {
            new InverseCollatzTreeWindow { Owner = this }.Show();
            return;
        }

        if (_selectedItem?.LaunchKey == "DomainColoring")
        {
            new DomainColoringWindow { Owner = this }.Show();
            return;
        }

        if (_selectedItem?.LaunchKey == "NovaMandelbrot")
        {
            new NovaWindow(NovaVariant.Mandelbrot) { Owner = this }.Show();
            return;
        }

        if (_selectedItem?.LaunchKey == "NovaJulia")
        {
            new NovaWindow(NovaVariant.Julia) { Owner = this }.Show();
            return;
        }

        if (_selectedItem?.LaunchKey == "Buddhabrot")
        {
            new BuddhabrotWindow { Owner = this }.Show();
            return;
        }

        if (_selectedItem?.LaunchKey == "Flame")
        {
            new FlameWindow { Owner = this }.Show();
            return;
        }

        if (_selectedItem?.LaunchKey == "IFS")
        {
            new IfsWindow { Owner = this }.Show();
            return;
        }

        if (_selectedItem?.LaunchKey == "ApollonianGasket")
        {
            new ApollonianWindow { Owner = this }.Show();
            return;
        }

        if (_selectedItem?.LaunchKey == "DLA")
        {
            new DlaWindow { Owner = this }.Show();
            return;
        }

        if (_selectedItem?.LaunchKey == "GrayScott")
        {
            new GrayScottWindow { Owner = this }.Show();
            return;
        }

        if (Enum.TryParse(_selectedItem?.LaunchKey, out DynamicSystemKind dynamicSystem))
        {
            new DynamicSystemWindow(dynamicSystem) { Owner = this }.Show();
            return;
        }

        if (Enum.TryParse(_selectedItem?.LaunchKey, out MandelbrotVariant variant))
        {
            new MandelbrotWindow(variant) { Owner = this }.Show();
        }
    }
}
