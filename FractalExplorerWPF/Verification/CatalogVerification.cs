using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Theming;

// Каталог главного окна: данные FractalCatalog, поиск, недавние, связь плиток, меню разделов и панели
// деталей с разметкой. Окно не показывается: его содержимое раскладывается отдельно от окна.
internal static partial class Program
{
    private static async Task VerifyCatalogAsync(string? outputDirectory)
    {
        VerifyCatalogData();
        VerifyCatalogSearch();
        VerifyRecentFractalsStore();
        await VerifyMainWindowCatalogAsync(outputDirectory);
        Console.WriteLine("PASS (catalog): catalog data, previews, launch keys, search, recents, scopes, grouping, selection, details, favorites, bindings and themes.");
    }

    private static void VerifyCatalogData()
    {
        IReadOnlyList<FractalCatalogItem> catalog = FractalCatalog.Create();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (FractalCatalogItem item in catalog)
        {
            Check(names.Add(item.DisplayName.Trim()),
                $"Duplicate catalog name «{item.DisplayName}»: favorites and recents are stored by name.");
            Check(item.CategoryPath.Count > 0 && item.CategoryPath.All(part => !string.IsNullOrWhiteSpace(part)),
                $"«{item.DisplayName}» has an empty category path.");
            Check(!string.IsNullOrWhiteSpace(item.Description), $"«{item.DisplayName}» has no description.");
            Check(MainWindow.GetWindowFactory(item.LaunchKey) is not null,
                $"«{item.DisplayName}»: launch key «{item.LaunchKey}» opens no window.");
            BitmapSource? preview = CatalogPreviewLoader.DecodeResource(item.PreviewResourcePath, 0);
            Check(preview is not null && preview.PixelWidth == preview.PixelHeight && preview.PixelWidth >= CatalogPreviewLoader.ThumbnailPixelWidth,
                $"«{item.DisplayName}»: preview {item.PreviewResourcePath} is missing or not a square of at least {CatalogPreviewLoader.ThumbnailPixelWidth} px.");
            BitmapSource? thumbnail = CatalogPreviewLoader.DecodeResource(item.PreviewResourcePath, CatalogPreviewLoader.ThumbnailPixelWidth);
            Check(thumbnail?.PixelWidth == CatalogPreviewLoader.ThumbnailPixelWidth,
                $"«{item.DisplayName}»: thumbnail must be decoded at {CatalogPreviewLoader.ThumbnailPixelWidth} px, not {thumbnail?.PixelWidth}.");
        }

        foreach (MathematicalLaboratoryKind kind in Enum.GetValues<MathematicalLaboratoryKind>())
            Check(catalog.Count(item => item.LaunchKey == MathematicalLaboratoryCatalog.LaunchKey(kind)) == 1,
                $"Laboratory {kind} must appear in the catalog exactly once.");
        foreach (BasinExplorerKind kind in Enum.GetValues<BasinExplorerKind>())
            Check(catalog.Count(item => item.LaunchKey == BasinExplorerCatalog.LaunchKey(kind)) == 1,
                $"Basin explorer {kind} must appear in the catalog exactly once.");
        Check(MainWindow.GetWindowFactory(null) is null && MainWindow.GetWindowFactory("NoSuchWindow") is null,
            "Unknown launch keys must not open a window.");

        IReadOnlyList<CatalogScope> scopes = CatalogScope.Build(catalog);
        Check(scopes.Take(3).Select(scope => scope.Kind).SequenceEqual([CatalogScopeKind.All, CatalogScopeKind.Favorites, CatalogScopeKind.Recent]),
            "The menu must start with all modes, favorites and recents.");
        int prefixes = catalog
            .SelectMany(item => Enumerable.Range(1, item.CategoryPath.Count).Select(length => string.Join("\u001F", item.CategoryPath.Take(length))))
            .Distinct().Count();
        Check(scopes.Count(scope => scope.Kind == CatalogScopeKind.Category) == prefixes,
            "Every distinct category prefix must get exactly one menu entry.");
        var tiles = catalog.Select(item => new CatalogTile(item, new CatalogGroup(item.CategoryPath))).ToList();
        foreach (CatalogScope scope in scopes.Where(scope => scope.Kind == CatalogScopeKind.Category))
        {
            Check(tiles.Any(scope.Includes), $"Menu entry «{scope.ToolTipText ?? scope.Title}» is empty.");
            Check(scope.IsSection == (scope.Path.Count == 1) && scope.Indent.Left == Math.Max(0, scope.Depth - 1) * 14,
                $"Menu entry «{scope.Title}» has a wrong level.");
        }
        CatalogScope nested = scopes.First(scope => scope.Kind == CatalogScopeKind.Category && scope.Path.Count == 3);
        Check(nested.ToolTipText == string.Join(" › ", nested.Path) && scopes[0].ToolTipText is null,
            "Only nested categories show their full path as a tooltip.");
    }

    private static void VerifyCatalogSearch()
    {
        IReadOnlyList<FractalCatalogItem> catalog = FractalCatalog.Create();
        List<string> Search(string query) =>
            catalog.Where(item => CatalogSearch.Matches(item, query)).Select(item => item.DisplayName).ToList();

        Check(Search("").Count == catalog.Count && Search("  \t ").Count == catalog.Count, "An empty query must match everything.");
        Check(Search("gray-scott").Contains("Gray–Scott reaction–diffusion"), "A hyphen must match an en dash.");
        Check(Search("L-системы").Contains("L‑системы и черепашья графика"), "A hyphen must match a non-breaking hyphen.");
        Check(Search("РЕССЛЕР").Contains("Аттрактор Рёсслера"), "Search must ignore case and treat ё as е.");
        List<string> newton = Search("ньютон");
        Check(newton.Contains("Бассейны Ньютона+") && newton.Contains("Бассейны метода Лагерра"),
            "Search must look into descriptions, not only names.");
        Check(Search("жюлиа галерея").ToHashSet().SetEquals(["Галерея констант C (Жюлиа)", "Галерея констант C (Жюлиа горящий корабль)"]),
            "All words of the query must match, in any order.");
        List<string> attractors = Search("аттракторы");
        Check(catalog.Where(item => item.CategoryPath[^1] == "Аттракторы").All(item => attractors.Contains(item.DisplayName)),
            "Search must look into category names.");
        Check(Search("жюлиа нетакогослова").Count == 0, "A word that matches nothing must reject the item.");
        Check(CatalogSearch.CountModes(0) == "0 режимов" && CatalogSearch.CountModes(1) == "1 режим" &&
              CatalogSearch.CountModes(3) == "3 режима" && CatalogSearch.CountModes(5) == "5 режимов" &&
              CatalogSearch.CountModes(11) == "11 режимов" && CatalogSearch.CountModes(21) == "21 режим" &&
              CatalogSearch.CountModes(57) == "57 режимов" && CatalogSearch.CountModes(112) == "112 режимов",
            "Russian plural forms of the mode count are wrong.");
    }

    private static void VerifyRecentFractalsStore()
    {
        using var sandbox = DataSandbox.Create("recent");
        Check(RecentFractalsStore.Load().Count == 0, "Recents must be empty without a file.");
        List<string> recent = RecentFractalsStore.Push([], "A");
        recent = RecentFractalsStore.Push(recent, "B");
        recent = RecentFractalsStore.Push(recent, "a");
        Check(recent.SequenceEqual(["a", "B"]), "A relaunched mode must move to the front without duplicates.");
        for (int index = 0; index < 20; index++) recent = RecentFractalsStore.Push(recent, $"X{index}");
        Check(recent.Count == RecentFractalsStore.Capacity && recent[0] == "X19" && recent[^1] == $"X{20 - RecentFractalsStore.Capacity}",
            "Recents must keep only the newest entries.");
        RecentFractalsStore.Save(recent);
        Check(RecentFractalsStore.Load().SequenceEqual(recent), "Recents must survive a save.");
        File.WriteAllLines(AppPaths.GetSettingsFile("recent_fractals.txt"), ["  C ", "", "c", "D"]);
        Check(RecentFractalsStore.Load().SequenceEqual(["C", "D"]), "Blank and repeated lines must be ignored on load.");
    }

    private static async Task VerifyMainWindowCatalogAsync(string? outputDirectory)
    {
        using var sandbox = DataSandbox.Create("catalog");
        var themeStyles = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if (!Application.Current.Resources.MergedDictionaries.Any(dictionary => dictionary.Source == themeStyles))
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = themeStyles });
        ThemeManager.Initialize(Application.Current);
        string initialTheme = ThemeManager.CurrentThemeId;

        const string newest = "Фрактал Феникс", older = "Аттрактор Лоренца", oldest = "Классическое Жюлиа";
        File.WriteAllLines(AppPaths.EnsureDirectoryFor(AppPaths.GetSettingsFile("recent_fractals.txt")),
            [newest, "Удалённый режим", older, oldest]);
        File.WriteAllLines(AppPaths.GetSettingsFile("favorite_fractals.txt"),
            ["Классический Мандельброт", "Domain Coloring", "Удалённый режим"]);
        File.WriteAllText(AppPaths.GetSettingsFile("render_pattern.txt"), "2");

        IReadOnlyList<FractalCatalogItem> catalog = FractalCatalog.Create();
        var bindingErrors = new CatalogBindingErrorListener();
        // Без отладчика WPF не пишет трассировку привязок, пока её не обновят явно.
        PresentationTraceSources.Refresh();
        SourceLevels previousLevel = PresentationTraceSources.DataBindingSource.Switch.Level;
        PresentationTraceSources.DataBindingSource.Listeners.Add(bindingErrors);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        try
        {
            // Ловушка должна действительно слышать ошибки привязок, иначе итоговая проверка ничего не доказывает.
            var probe = new TextBlock { DataContext = new CatalogGroup(["Probe"]) };
            probe.SetBinding(TextBlock.TextProperty, new Binding("NoSuchProperty"));
            Check(bindingErrors.Messages.Any(message => message.Contains("NoSuchProperty")),
                "The binding error listener does not receive binding errors.");
            bindingErrors.Messages.Clear();

            var window = new MainWindow();
            FrameworkElement root = DetachForLayout(window);
            await LayoutCatalogAsync(root);
            Console.WriteLine("DIAG line 156: " + bindingErrors.Messages.Count);

            // ----- Меню разделов -----
            CatalogScope all = window.Scopes[0], favorites = window.Scopes[1], recents = window.Scopes[2];
            CatalogScope Category(params string[] path) => window.Scopes.Single(scope =>
                scope.Kind == CatalogScopeKind.Category && scope.Path.SequenceEqual(path));
            CatalogScope mandelbrotFamily = Category("Фракталы", "Комплексная динамика", "Семейство Мандельброта");
            CatalogScope laboratories = Category("Математические лаборатории");
            CatalogScope fractals = Category("Фракталы");
            CatalogScope attractors = Category("Динамические системы и хаос", "Аттракторы");
            CatalogScope basins = Category("Фракталы", "Комплексная динамика", "Бассейны притяжения");

            Check(all.Count == catalog.Count && favorites.Count == 2 && recents.Count == 3,
                $"Menu counts must ignore names missing from the catalog: {all.Count}/{favorites.Count}/{recents.Count}.");
            foreach (CatalogScope scope in window.Scopes.Where(scope => scope.Kind == CatalogScopeKind.Category))
            {
                int expected = catalog.Count(item => item.CategoryPath.Take(scope.Path.Count).SequenceEqual(scope.Path));
                Check(scope.Count == expected, $"«{scope.Title}» shows {scope.Count} instead of {expected}.");
            }
            for (int index = 0; index < window.Scopes.Count; index++)
            {
                Check(window.ScopeList.ItemContainerGenerator.ContainerFromIndex(index) is ListBoxItem container &&
                      CatalogDescendants<TextBlock>(container).Any(text => text.Text == window.Scopes[index].DisplayTitle) &&
                      CatalogDescendants<TextBlock>(container).Any(text => text.Text == window.Scopes[index].Count.ToString()),
                    $"Menu entry «{window.Scopes[index].Title}» is not bound to its row.");
            }
            Check(window.RenderPatternSelector.SelectedIndex == 2, "The saved render pattern must be selected.");
            Check(window.ThemeSelector.SelectedItem is ThemeDefinition { Id: var themeId } && themeId == ThemeManager.CurrentThemeId,
                "The current theme must be selected.");

            // ----- Начальное состояние: все режимы, открыт последний запущенный -----
            CatalogTile phoenix = Tile(window, newest);
            Check(window.CurrentScope == all && window.ScopeList.SelectedItem == all, "The catalog must open with all modes.");
            Check(window.SelectedTile == phoenix && window.CatalogGallery.SelectedItem == phoenix,
                "The most recently launched mode must be selected on start.");
            Check(IsTileInView(window, phoenix), "The selected mode must be scrolled into view on start.");
            CheckDetails(window, phoenix);
            FrameworkElement? typedText = CatalogDescendants<FrameworkElement>(window.SearchBox)
                .FirstOrDefault(element => element.GetType().Name == "TextBoxView");
            double textLeft = typedText?.TranslatePoint(new Point(), window.SearchBox).X ?? double.NaN;
            Check(textLeft is > 32 and < 40, $"Search text must start right after the search icon, not at {textLeft:0.#} px.");
            Check(window.GalleryTitle.Text == "Все режимы" && window.GallerySubtitle.Text == CatalogSearch.CountModes(catalog.Count),
                $"Wrong gallery header: {window.GalleryTitle.Text} / {window.GallerySubtitle.Text}.");

            List<CatalogGroup> expectedGroups = window.Tiles.Select(tile => tile.Group).Distinct().ToList();
            Check(window.GalleryView.Groups?.Count == expectedGroups.Count &&
                  window.GalleryView.Groups.Cast<CollectionViewGroup>().Select(group => group.Name).SequenceEqual(expectedGroups),
                "All modes must be grouped by their category in catalog order.");
            Check(expectedGroups.Select(group => group.Key).Distinct().Count() == expectedGroups.Count,
                "Each category path must map to a single group object.");
            CheckTileContainers(window, catalog.Count);
            List<UniformWrapPanel> panels = CatalogDescendants<UniformWrapPanel>(window.CatalogGallery).ToList();
            Check(panels.Count == expectedGroups.Count && panels.All(panel => panel.Columns == panels[0].Columns && panel.Columns >= 2),
                $"Every group must lay tiles out in the same number of columns: {string.Join(", ", panels.Select(panel => panel.Columns))}.");
            Check(CatalogDescendants<GroupItem>(window.CatalogGallery).Any(group =>
                      CatalogDescendants<TextBlock>(group).Any(text => text.Text == "Семейство Мандельброта") &&
                      CatalogDescendants<TextBlock>(group).Any(text => text.Text == "Фракталы › Комплексная динамика") &&
                      CatalogDescendants<TextBlock>(group).Any(text => text.Text == "7")),
                "Group headers must show the group title, its parent path and item count.");
            string? pngDirectory = outputDirectory is null ? null : Directory.CreateDirectory(outputDirectory).FullName;
            SaveCatalogPng(root, pngDirectory, "01-all-modes");

            // ----- Превью -----
            List<CatalogTile> rendered = window.Tiles.Where(tile => CatalogPreviewLoader.IsRendered(tile.Item)).ToList();
            Check(rendered.Count == Enum.GetValues<MathematicalLaboratoryKind>().Length + 1,
                "Laboratories and Gray–Scott must be the modes rendered on the fly.");
            foreach (CatalogTile tile in window.Tiles.Except(rendered))
            {
                Check(tile.Thumbnail is BitmapSource { PixelWidth: CatalogPreviewLoader.ThumbnailPixelWidth } && !tile.IsPreviewPending,
                    $"«{tile.DisplayName}» must show its bundled preview as a {CatalogPreviewLoader.ThumbnailPixelWidth} px thumbnail.");
            }
            Check(rendered.All(tile => tile.IsPreviewPending && tile.Thumbnail is null),
                "Rendered previews must wait for rendering instead of showing the shared placeholder.");
            CheckTileImage(window, Tile(window, "Классический Мандельброт"));

            var clock = Stopwatch.StartNew();
            await window.RenderPendingPreviewsAsync();
            Console.WriteLine("DIAG line 232: " + bindingErrors.Messages.Count);
            clock.Stop();
            foreach (CatalogTile tile in rendered)
            {
                Check(!tile.IsPreviewPending && tile.Thumbnail is BitmapSource { PixelWidth: CatalogPreviewLoader.RenderedPixelSize } bitmap &&
                      ReferenceEquals(tile.Preview, bitmap) && CountDistinctColors(bitmap) > 1,
                    $"«{tile.DisplayName}» must get a rendered, non-uniform preview.");
            }
            Check(rendered.Select(tile => tile.Thumbnail).Distinct().Count() == rendered.Count,
                "Every rendered mode must get its own preview.");
            Console.WriteLine($"  catalog: {rendered.Count} previews rendered in {clock.ElapsedMilliseconds} ms");
            root.UpdateLayout();
            CheckTileImage(window, Tile(window, "Гиперболическая геометрия"));

            // Полноразмерное превью держится только у открытого в деталях пункта.
            Check(phoenix.Preview is BitmapSource { PixelWidth: 512 } && ReferenceEquals(phoenix.DisplayPreview, phoenix.Preview),
                "The selected bundled preview must be loaded at full size.");
            CatalogTile mandelbrot = Tile(window, "Классический Мандельброт");
            window.CatalogGallery.SelectedItem = mandelbrot;
            Console.WriteLine("DIAG line 250: " + bindingErrors.Messages.Count);
            Check(window.SelectedTile == mandelbrot && phoenix.Preview is null && ReferenceEquals(phoenix.DisplayPreview, phoenix.Thumbnail) &&
                  mandelbrot.Preview is BitmapSource { PixelWidth: 512 },
                "Selecting another tile must release the previous full-size preview.");
            CheckDetails(window, mandelbrot);
            CatalogTile hyperbolic = Tile(window, "Гиперболическая геометрия");
            window.CatalogGallery.SelectedItem = hyperbolic;
            Console.WriteLine("DIAG line 256: " + bindingErrors.Messages.Count);
            window.CatalogGallery.SelectedItem = mandelbrot;
            Console.WriteLine("DIAG line 257: " + bindingErrors.Messages.Count);
            Check(hyperbolic.Preview is not null && ReferenceEquals(hyperbolic.Preview, hyperbolic.Thumbnail),
                "A rendered preview must stay in memory after leaving the details panel.");

            // ----- Разделы -----
            window.ScopeList.SelectedItem = mandelbrotFamily;
            Console.WriteLine("DIAG line 262: " + bindingErrors.Messages.Count);
            await LayoutCatalogAsync(root);
            Console.WriteLine("DIAG line 263: " + bindingErrors.Messages.Count);
            Check(window.CurrentScope == mandelbrotFamily && ViewItems(window).Count == 7 && window.GalleryView.Groups is null,
                "A leaf category must show its modes without group headers.");
            Check(window.GalleryTitle.Text == "Семейство Мандельброта" &&
                  window.GallerySubtitle.Text == "Фракталы › Комплексная динамика · 7 режимов",
                $"Wrong leaf header: {window.GalleryTitle.Text} / {window.GallerySubtitle.Text}.");
            Check(window.SelectedTile == mandelbrot, "A selection inside the new scope must be kept.");
            CheckTileContainers(window, 7);

            window.ScopeList.SelectedItem = laboratories;
            Console.WriteLine("DIAG line 272: " + bindingErrors.Messages.Count);
            await LayoutCatalogAsync(root);
            Console.WriteLine("DIAG line 273: " + bindingErrors.Messages.Count);
            int laboratoryModes = catalog.Count(item => item.CategoryPath[0] == "Математические лаборатории");
            int laboratoryGroups = catalog.Where(item => item.CategoryPath[0] == "Математические лаборатории")
                .Select(item => item.CategoryBreadcrumb).Distinct().Count();
            Check(laboratoryGroups > 1 && ViewItems(window).Count == laboratoryModes && window.GalleryView.Groups?.Count == laboratoryGroups,
                "A section must show all of its groups with headers.");
            Check(window.SelectedTile == ViewItems(window)[0] && window.SelectedTile!.Item.CategoryPath[0] == "Математические лаборатории",
                "A selection outside the new scope must move to its first mode.");
            Check(IsTileInView(window, window.SelectedTile!) && GalleryScrollViewer(window).VerticalOffset == 0,
                "A new scope must start from the top of the gallery.");
            CheckDetails(window, window.SelectedTile!);
            SaveCatalogPng(root, pngDirectory, "02-laboratories");

            window.ScopeList.SelectedItem = recents;
            Console.WriteLine("DIAG line 286: " + bindingErrors.Messages.Count);
            await LayoutCatalogAsync(root);
            Console.WriteLine("DIAG line 287: " + bindingErrors.Messages.Count);
            Check(ViewItems(window).Select(tile => tile.DisplayName).SequenceEqual([newest, older, oldest]) && window.GalleryView.Groups is null,
                "Recents must be listed in launch order without group headers.");
            Check(window.GallerySubtitle.Text == "Последние запущенные · 3 режима", $"Wrong recents header: {window.GallerySubtitle.Text}.");
            window.RecordLaunch(Tile(window, oldest));
            Console.WriteLine("DIAG line 291: " + bindingErrors.Messages.Count);
            Check(ViewItems(window).Select(tile => tile.DisplayName).SequenceEqual([oldest, newest, older]),
                "A launch must move the mode to the top of recents.");
            Check(File.ReadAllLines(AppPaths.GetSettingsFile("recent_fractals.txt")).SequenceEqual([oldest, newest, older]),
                "A launch must be saved to recents.");
            window.RecordLaunch(mandelbrot);
            Console.WriteLine("DIAG line 296: " + bindingErrors.Messages.Count);
            Check(recents.Count == 4 && mandelbrot.RecentRank == 0 && Tile(window, oldest).RecentRank == 1,
                "Recent ranks and the menu count must follow launches.");

            // ----- Избранное -----
            window.ScopeList.SelectedItem = favorites;
            Console.WriteLine("DIAG line 301: " + bindingErrors.Messages.Count);
            await LayoutCatalogAsync(root);
            Console.WriteLine("DIAG line 302: " + bindingErrors.Messages.Count);
            CatalogTile domainColoring = Tile(window, "Domain Coloring");
            Check(ViewItems(window).ToHashSet().SetEquals([mandelbrot, domainColoring]) && window.GalleryView.Groups?.Count == 2,
                "Favorites from different categories must be grouped.");
            Check(CatalogDescendants<ListBoxItem>(window.CatalogGallery).Count(item =>
                      item.DataContext is CatalogTile { IsFavorite: true } &&
                      CatalogDescendants<Border>(item).Any(badge => badge.ToolTip as string == "В избранном" && badge.Visibility == Visibility.Visible)) == 2,
                "Favorite tiles must show the star badge.");
            window.CatalogGallery.SelectedItem = domainColoring;
            Console.WriteLine("DIAG line 310: " + bindingErrors.Messages.Count);
            window.ToggleFavorite(domainColoring);
            Console.WriteLine("DIAG line 311: " + bindingErrors.Messages.Count);
            Check(!domainColoring.IsFavorite && favorites.Count == 1 && ViewItems(window).SequenceEqual([mandelbrot]) &&
                  window.SelectedTile == mandelbrot,
                "Removing a favorite must remove it from the favorites view and move the selection.");
            Check(!File.ReadAllLines(AppPaths.GetSettingsFile("favorite_fractals.txt")).Contains("Domain Coloring"),
                "Removing a favorite must be saved.");
            window.ToggleFavorite(mandelbrot);
            Console.WriteLine("DIAG line 317: " + bindingErrors.Messages.Count);
            await LayoutCatalogAsync(root);
            Console.WriteLine("DIAG line 318: " + bindingErrors.Messages.Count);
            Check(ViewItems(window).Count == 0 && window.GalleryEmptyState.Visibility == Visibility.Visible &&
                  window.CatalogGallery.Visibility == Visibility.Collapsed && window.GalleryEmptyTitle.Text == "В избранном пока пусто" &&
                  window.GalleryEmptyAction.Visibility == Visibility.Collapsed,
                "Empty favorites must explain how to add one.");
            Check(window.SelectedTile is null && window.DetailsEmptyText.Visibility == Visibility.Visible &&
                  window.DetailsContent.Visibility == Visibility.Collapsed,
                "Without a visible mode the details panel must be empty.");
            window.ScopeList.SelectedItem = attractors;
            Console.WriteLine("DIAG line 326: " + bindingErrors.Messages.Count);
            Check(window.SelectedTile?.Item.CategoryPath[^1] == "Аттракторы", "Leaving an empty scope must select a mode again.");
            window.ToggleFavorite(window.SelectedTile!);
            Console.WriteLine("DIAG line 328: " + bindingErrors.Messages.Count);
            Check(favorites.Count == 1 && window.SelectedTile!.IsFavorite && ViewItems(window).Count == 5,
                "Adding a favorite outside the favorites view must only update the menu.");

            // ----- Поиск -----
            window.ScopeList.SelectedItem = mandelbrotFamily;
            Console.WriteLine("DIAG line 333: " + bindingErrors.Messages.Count);
            window.SearchBox.Text = "ньютон";
            Console.WriteLine("DIAG line 334: " + bindingErrors.Messages.Count);
            await LayoutCatalogAsync(root);
            Console.WriteLine("DIAG line 335: " + bindingErrors.Messages.Count);
            Check(window.CurrentScope == all && window.ScopeList.SelectedItem == all, "Typing a query must search the whole catalog.");
            Check(ViewItems(window).Any(tile => tile.DisplayName == "Бассейны Ньютона+") &&
                  ViewItems(window).Any(tile => tile.DisplayName == "Бассейны метода Лагерра") &&
                  ViewItems(window).All(tile => CatalogSearch.Matches(tile.Item, "ньютон")),
                "The gallery must show exactly the matches.");
            Check(basins.Count >= 2 && attractors.Count == 0 && attractors.IsDimmed && !basins.IsDimmed && all.Count == ViewItems(window).Count,
                "Menu counts must show where the matches are.");
            Check(window.GallerySubtitle.Text == $"Найдено {CatalogSearch.CountModes(ViewItems(window).Count)} по запросу «ньютон»",
                $"Wrong search header: {window.GallerySubtitle.Text}.");
            Check(window.ClearSearchButton.Visibility == Visibility.Visible && window.SearchPlaceholder.Visibility == Visibility.Collapsed,
                "A query must show the clear button and hide the placeholder.");
            Check(ViewItems(window).Contains(window.SelectedTile!), "The selection must be one of the matches.");
            SaveCatalogPng(root, pngDirectory, "03-search");

            window.SearchBox.Text = "";
            Console.WriteLine("DIAG line 350: " + bindingErrors.Messages.Count);
            Check(window.CurrentScope == mandelbrotFamily && window.ScopeList.SelectedItem == mandelbrotFamily &&
                  !attractors.IsDimmed && attractors.Count == 5 && all.Count == catalog.Count,
                "Clearing the query must return to the scope opened before the search.");
            Check(window.ClearSearchButton.Visibility == Visibility.Collapsed && window.SearchPlaceholder.Visibility == Visibility.Visible,
                "An empty query must hide the clear button.");

            window.SearchBox.Text = "аттрактор";
            Console.WriteLine("DIAG line 357: " + bindingErrors.Messages.Count);
            window.ScopeList.SelectedItem = fractals;
            Console.WriteLine("DIAG line 358: " + bindingErrors.Messages.Count);
            Check(window.CurrentScope == fractals && ViewItems(window).All(tile => tile.Item.CategoryPath[0] == "Фракталы" && CatalogSearch.Matches(tile.Item, "аттрактор")),
                "A scope chosen during a search must filter the matches.");
            window.SearchBox.Text = "";
            Console.WriteLine("DIAG line 361: " + bindingErrors.Messages.Count);
            Check(window.CurrentScope == fractals, "A scope chosen during a search must stay after clearing it.");

            window.CatalogGallery.SelectedItem = Tile(window, "Аполлонова прокладка");
            Console.WriteLine("DIAG line 364: " + bindingErrors.Messages.Count);
            window.SearchBox.Text = "нетакогорежима";
            Console.WriteLine("DIAG line 365: " + bindingErrors.Messages.Count);
            await LayoutCatalogAsync(root);
            Console.WriteLine("DIAG line 366: " + bindingErrors.Messages.Count);
            Check(window.GalleryEmptyState.Visibility == Visibility.Visible && window.GalleryEmptyTitle.Text == "Ничего не найдено" &&
                  (string?)window.GalleryEmptyAction.Content == "Сбросить поиск" && window.SelectedTile is null &&
                  window.Scopes.Where(scope => scope.Kind == CatalogScopeKind.Category).All(scope => scope.IsDimmed),
                "A query without matches must show the empty state.");
            SaveCatalogPng(root, pngDirectory, "04-nothing-found");
            window.SearchBox.Text = "";
            Console.WriteLine("DIAG line 372: " + bindingErrors.Messages.Count);
            await LayoutCatalogAsync(root);
            Console.WriteLine("DIAG line 373: " + bindingErrors.Messages.Count);
            Check(window.SelectedTile?.DisplayName == "Аполлонова прокладка" && window.CurrentScope == fractals,
                "Clearing a query without matches must restore the previous selection and scope.");
            Check(IsTileInView(window, window.SelectedTile!), "A restored selection must be scrolled into view.");

            // ----- Настройки и темы -----
            Check(window.SettingsFlyout.Visibility == Visibility.Collapsed, "Settings must start closed.");
            window.SettingsToggle.IsChecked = true;
            Check(window.SettingsFlyout.Visibility == Visibility.Visible, "The settings toggle must open the settings panel.");
            await LayoutCatalogAsync(root);
            Console.WriteLine("DIAG line 382: " + bindingErrors.Messages.Count);
            SaveCatalogPng(root, pngDirectory, "05-settings");
            window.SettingsToggle.IsChecked = false;
            Check(window.SettingsFlyout.Visibility == Visibility.Collapsed, "The settings toggle must close the settings panel.");

            window.ScopeList.SelectedItem = all;
            Console.WriteLine("DIAG line 387: " + bindingErrors.Messages.Count);
            window.CatalogGallery.SelectedItem = phoenix;
            Console.WriteLine("DIAG line 388: " + bindingErrors.Messages.Count);
            foreach (ThemeDefinition theme in ThemeManager.GetAllThemes())
            {
                ThemeManager.SetTheme(theme.Id);
                // Смена ресурсов приложения доходит только до окон. Отсоединённому содержимому её передаёт
                // собственный словарь с теми же ключами: WPF пересчитывает ссылки только на ключи сменившегося словаря.
                var themeResources = new ResourceDictionary();
                foreach (System.Collections.DictionaryEntry entry in Application.Current.Resources) themeResources[entry.Key] = entry.Value;
                root.Resources = themeResources;
                await LayoutCatalogAsync(root);
                Console.WriteLine("DIAG line 397: " + bindingErrors.Messages.Count);
                Check(window.ThemeSelector.SelectedItem is ThemeDefinition { Id: var selectedId } && selectedId == theme.Id,
                    $"The theme selector must follow the theme «{theme.DisplayName}».");
                Check(window.GalleryTitle.Foreground is SolidColorBrush { Color: var titleColor } && titleColor == theme.PrimaryText &&
                      window.SettingsFlyout.Children.OfType<Border>().Last().Background is SolidColorBrush { Color: var flyoutColor } &&
                      flyoutColor == theme.PanelBackground,
                    $"The catalog must repaint in the theme «{theme.DisplayName}».");
                if (theme.Id == "light") SaveCatalogPng(root, pngDirectory, "06-light-theme");
            }
            ThemeManager.SetTheme(initialTheme);
            await LayoutCatalogAsync(root);
            Console.WriteLine("DIAG line 407: " + bindingErrors.Messages.Count);

            Check(bindingErrors.Messages.Count == 0,
                "Binding errors in the catalog:" + Environment.NewLine + string.Join(Environment.NewLine, bindingErrors.Messages.Distinct()));
        }
        finally
        {
            PresentationTraceSources.DataBindingSource.Listeners.Remove(bindingErrors);
            PresentationTraceSources.DataBindingSource.Switch.Level = previousLevel;
        }
    }

    private static CatalogTile Tile(MainWindow window, string name) => window.Tiles.Single(tile => tile.DisplayName == name);

    private static List<CatalogTile> ViewItems(MainWindow window) => window.GalleryView.Cast<CatalogTile>().ToList();

    private static ScrollViewer GalleryScrollViewer(MainWindow window) => CatalogDescendants<ScrollViewer>(window.CatalogGallery).First();

    private static bool IsTileInView(MainWindow window, CatalogTile tile)
    {
        ScrollViewer viewer = GalleryScrollViewer(window);
        if (window.CatalogGallery.ItemContainerGenerator.ContainerFromItem(tile) is not FrameworkElement container) return false;
        Rect bounds = container.TransformToAncestor(viewer).TransformBounds(new Rect(container.RenderSize));
        return bounds.Top >= -0.5 && bounds.Bottom <= viewer.ViewportHeight + 0.5;
    }

    private static void CheckDetails(MainWindow window, CatalogTile tile)
    {
        Check(window.DetailsContent.DataContext == tile && window.DetailsContent.Visibility == Visibility.Visible &&
              window.DetailsEmptyText.Visibility == Visibility.Collapsed,
            $"The details panel must show «{tile.DisplayName}».");
        Check(window.DetailsTitle.Text == tile.DisplayName && window.DetailsDescription.Text == tile.Description &&
              window.DetailsBreadcrumb.Text == tile.Breadcrumb && window.LaunchButton.IsEnabled == tile.CanLaunch,
            $"The details panel is not bound to «{tile.DisplayName}».");
    }

    private static void CheckTileContainers(MainWindow window, int expectedCount)
    {
        List<CatalogTile> items = ViewItems(window);
        Check(items.Count == expectedCount, $"The gallery shows {items.Count} modes instead of {expectedCount}.");
        List<ListBoxItem> containers = CatalogDescendants<ListBoxItem>(window.CatalogGallery).ToList();
        Check(containers.Count == expectedCount && containers.Select(container => container.DataContext).ToHashSet().SetEquals(items),
            $"The gallery generated {containers.Count} tiles for {expectedCount} modes.");
        foreach (ListBoxItem container in containers)
        {
            var tile = (CatalogTile)container.DataContext;
            Check(ReferenceEquals(window.CatalogGallery.ItemContainerGenerator.ContainerFromItem(tile), container),
                $"«{tile.DisplayName}» cannot be found by its container generator.");
            Check(CatalogDescendants<TextBlock>(container).Any(text => text.Text == tile.DisplayName),
                $"The tile of «{tile.DisplayName}» does not show its name.");
            Grid square = CatalogDescendants<Grid>(container).First();
            Check(square.ActualWidth > 100 && Math.Abs(square.ActualWidth - square.ActualHeight) < 0.5,
                $"The preview of «{tile.DisplayName}» is not a square: {square.ActualWidth:0.#}×{square.ActualHeight:0.#}.");
        }
    }

    private static void CheckTileImage(MainWindow window, CatalogTile tile)
    {
        var container = (ListBoxItem?)window.CatalogGallery.ItemContainerGenerator.ContainerFromItem(tile);
        Check(container is not null && tile.Thumbnail is not null &&
              CatalogDescendants<Border>(container).Any(border => border.Background is ImageBrush brush && ReferenceEquals(brush.ImageSource, tile.Thumbnail)),
            $"The tile of «{tile.DisplayName}» does not paint its thumbnail.");
    }

    private static FrameworkElement DetachForLayout(Window window)
    {
        var root = (FrameworkElement)window.Content;
        INameScope? names = NameScope.GetNameScope(window);
        window.Content = null;
        NameScope.SetNameScope(root, names);
        root.SetValue(TextElement.FontFamilyProperty, window.FontFamily);
        root.SetValue(TextOptions.TextFormattingModeProperty, TextOptions.GetTextFormattingMode(window));
        return root;
    }

    private static async Task LayoutCatalogAsync(FrameworkElement root)
    {
        var size = new Size(1180, 740);
        root.Measure(size);
        root.Arrange(new Rect(size));
        root.UpdateLayout();
        await DrainAsync();
        root.UpdateLayout();
    }

    private static void SaveCatalogPng(FrameworkElement root, string? directory, string name)
    {
        if (directory is null) return;
        var bitmap = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream stream = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(stream);
    }

    private static int CountDistinctColors(BitmapSource bitmap)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        int stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        var colors = new HashSet<int>();
        for (int offset = 0; offset < pixels.Length && colors.Count < 16; offset += 4 * 37)
            colors.Add(BitConverter.ToInt32(pixels, offset));
        return colors.Count;
    }

    private static IEnumerable<T> CatalogDescendants<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int index = 0; index < count; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (T nested in CatalogDescendants<T>(child)) yield return nested;
        }
    }

    private sealed class CatalogBindingErrorListener : TraceListener
    {
        private readonly System.Text.StringBuilder _line = new();
        public List<string> Messages { get; } = [];
        private string Diagnostics = "";
        public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? format, params object?[]? args)
        {
            Diagnostics = string.Join(" | ", (args ?? []).Select(arg => arg switch
            {
                ListBoxItem item => $"ListBoxItem dc={item.DataContext} parentVisual={VisualTreeHelper.GetParent(item)} owner={ItemsControl.ItemsControlFromItemContainer(item)?.Name}",
                BindingExpressionBase expression => $"expr target={expression.Target} dc={(expression.Target as FrameworkElement)?.DataContext}",
                _ => arg?.GetType().Name + ":" + arg
            }));
            base.TraceEvent(eventCache, source, eventType, id, format, args);
        }
        public override void Write(string? message) => _line.Append(message);
        public override void WriteLine(string? message)
        {
            _line.Append(message);
            Messages.Add(_line.ToString() + Environment.NewLine + Diagnostics);
            _line.Clear();
        }
    }
}
