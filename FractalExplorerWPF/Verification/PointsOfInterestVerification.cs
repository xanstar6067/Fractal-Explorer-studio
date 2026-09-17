using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private const int PointOfInterestWidth = 240;
    private const int PointOfInterestHeight = 180;

    /// <summary>
    /// Встроенные точки интереса раздела «Фракталы / Комплексная динамика»: каждая рендерится тем же
    /// путём, что и превью менеджера сохранений, и не должна давать однотонный или почти пустой кадр
    /// (промах мимо фрактала, внутренность множества, пустой фон Буддаброта).
    /// </summary>
    private static async Task VerifyPointsOfInterestAsync(string? filter, string? outputDirectory)
    {
        using var sandbox = DataSandbox.Create("points-of-interest");
        EnsureThemeStyles();
        List<string> failures = [];
        int checkedCount = 0;
        foreach (PointOfInterestGroup group in PointOfInterestGroups())
        {
            if (filter is not null && !group.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            Window window = group.CreateWindow();
            try
            {
                Console.WriteLine($"== {group.Name}: {group.Names.Count} точек");
                for (int i = 0; i < group.Names.Count; i++)
                {
                    BitmapSource image = await group.RenderAsync(window, i, PointOfInterestWidth, PointOfInterestHeight);
                    FrameMetrics metrics = MeasureFrame(image);
                    checkedCount++;
                    bool known = KnownSparsePoints.Contains($"{group.Name} / {group.Names[i]}");
                    Console.WriteLine($"   {(!metrics.IsFlat ? "ok    " : known ? "sparse" : "FLAT  ")} {metrics} · {group.Names[i]}");
                    if (metrics.IsFlat && !known) failures.Add($"{group.Name} / {group.Names[i]}: {metrics}");
                    if (outputDirectory is not null) SavePng(image, Path.Combine(outputDirectory, group.Name, $"{i:00} {AppPaths.ToSafeFileName(group.Names[i])}.png"));
                }
            }
            finally { window.Close(); }
        }
        Check(checkedCount > 0, $"Фильтр «{filter}» не нашёл ни одной группы точек интереса.");
        Check(failures.Count == 0, "Однотонные или пустые точки интереса:\n" + string.Join("\n", failures));
        Console.WriteLine($"PASS (poi): {checkedCount} точек интереса дают содержательный кадр.");
    }

    /// <summary>
    /// Исследовательский режим без пересборки приложения: рендерит состояния-кандидаты из JSON-массива
    /// (тот же формат, что у файлов сохранений) и печатает те же метрики, что и <c>poi</c>.
    /// </summary>
    private static async Task ProbePointsOfInterestAsync(string groupName, string file, string outputDirectory)
    {
        using var sandbox = DataSandbox.Create("points-of-interest-probe");
        EnsureThemeStyles();
        PointOfInterestGroup group = PointOfInterestGroups().FirstOrDefault(g => g.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Неизвестная группа «{groupName}». Допустимы: {string.Join(", ", PointOfInterestGroups().Select(g => g.Name))}.");
        string json = File.ReadAllText(file);
        Window window = group.CreateWindow();
        try
        {
            IReadOnlyList<string> names = group.LoadProbe(json);
            for (int i = 0; i < names.Count; i++)
            {
                BitmapSource image = await group.RenderProbeAsync(window, i, PointOfInterestWidth, PointOfInterestHeight);
                FrameMetrics metrics = MeasureFrame(image);
                Console.WriteLine($"{(metrics.IsFlat ? "FLAT" : "ok  ")} {metrics} · {names[i]}");
                SavePng(image, Path.Combine(outputDirectory, $"{i:00} {AppPaths.ToSafeFileName(names[i])}.png"));
            }
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// Кадры, которые метрика считает пустыми по природе системы, а не из-за промаха: тонкое
    /// множество на чёрном фоне бассейна ∞, бледные широкие полосы sin(z) и векторные поля с одним или концентрическими бассейнами,
    /// где яркость почти одинакова по всей плоскости. Новые точки сюда не добавлять — их нужно
    /// кадрировать так, чтобы проверка проходила.
    /// </summary>
    private static readonly HashSet<string> KnownSparsePoints =
    [
        "Basins-PeriodicCycles / z² + c · аэроплан, период 3",
        "Basins-PeriodicCycles / z³ + c·z · два цикла периода 3",
        "Basins-ComplexLogistic / λ = 3.5 · цикл периода 4",
        "Basins-ComplexLogistic / λ = 3.55 · цикл периода 8",
        "Basins-PolynomialVectorField / Предельный цикл · нормальная форма Хопфа",
        "Basins-PolynomialVectorField / Точка и цикл · два бассейна",
        "Basins-PolynomialVectorField / Два вложенных устойчивых цикла",
        "Basins-PolynomialVectorField / Осциллятор Ван дер Поля",
        "Basins-Muller / sin(z) − 0.5",
    ];

    private static void EnsureThemeStyles()
    {
        var themeStyles = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if (!Application.Current.Resources.MergedDictionaries.Any(d => d.Source == themeStyles))
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = themeStyles });
    }

    private static IEnumerable<PointOfInterestGroup> PointOfInterestGroups()
    {
        foreach (MandelbrotVariant variant in Enum.GetValues<MandelbrotVariant>())
            yield return Group($"Mandelbrot-{variant}", () => new MandelbrotWindow(variant),
                () => PresetManager.GetMandelbrotPresets(variant), s => s.SaveName,
                (w, s, width, height) => ((MandelbrotWindow)w).RenderStatePreviewAsync(s, width, height, CancellationToken.None),
                s => { if (s.Palette.Colors.Count == 0) s.Palette = NamedPalette(new MandelbrotPaletteManager(), s.PaletteName, s.Palette.ColorPeriod); });
        yield return Group("Newton", () => new NewtonPoolsWindow(), PresetManager.GetNewtonPresets, s => s.SaveName,
            (w, s, width, height) => ((NewtonPoolsWindow)w).RenderStatePreviewAsync(s, width, height, CancellationToken.None));
        foreach (BasinExplorerKind kind in Enum.GetValues<BasinExplorerKind>())
            yield return Group($"Basins-{kind}", () => new BasinExplorerWindow(kind), () => BasinExplorerCatalog.GetPresets(kind), s => s.SaveName,
                (w, s, width, height) => ((BasinExplorerWindow)w).RenderStatePreviewAsync(s.Clone(), width, height, CancellationToken.None));
        yield return Group("Phoenix", () => new PhoenixWindow(), PresetManager.GetPhoenixPresets, s => s.SaveName,
            (w, s, width, height) => ((PhoenixWindow)w).RenderStatePreviewAsync(s, width, height, CancellationToken.None),
            s => { if (s.Palette.Colors.Count == 0) s.Palette = NamedPalette(new MandelbrotPaletteManager(), s.Palette.Name, s.Palette.ColorPeriod); });
        foreach (NovaVariant variant in Enum.GetValues<NovaVariant>())
            yield return Group($"Nova-{variant}", () => new NovaWindow(variant), () => PresetManager.GetNovaPresets(variant), s => s.SaveName,
                (w, s, width, height) => ((NovaWindow)w).RenderStatePreviewAsync(s, width, height, CancellationToken.None),
                s => { if (s.Palette.Colors.Count == 0) s.Palette = NamedPalette(new NovaPaletteManager(), s.Palette.Name, s.Palette.ColorPeriod); });
        yield return Group("Collatz", () => new CollatzWindow(), PresetManager.GetCollatzPresets, s => s.SaveName,
            (w, s, width, height) => ((CollatzWindow)w).RenderStatePreviewAsync(s, width, height, CancellationToken.None),
            s => { if (s.Palette.Colors.Count == 0) s.Palette = NamedPalette(new CollatzPaletteManager(), s.Palette.Name, s.Palette.ColorPeriod); });
        yield return Group("Buddhabrot", () => new BuddhabrotWindow(), PresetManager.GetBuddhabrotPresets, s => s.SaveName,
            (w, s, width, height) => ((BuddhabrotWindow)w).RenderStatePreviewAsync(s, width, height, CancellationToken.None),
            s => { if (s.Palette.Colors.Count == 0) s.Palette = new BuddhabrotPaletteManager().Palettes.First(p => p.Name == s.Palette.Name).Clone(s.Palette.Name, true); });
    }

    private static PointOfInterestGroup Group<TState>(string name, Func<Window> createWindow,
        Func<IReadOnlyList<TState>> presets, Func<TState, string> getName,
        Func<Window, TState, int, int, Task<BitmapSource>> render, Action<TState>? probeFixup = null)
    {
        IReadOnlyList<TState>? cached = null, probe = null;
        IReadOnlyList<TState> Presets() => cached ??= presets();
        return new PointOfInterestGroup(name, createWindow,
            () => Presets().Select(getName).ToList(),
            (window, index, width, height) => render(window, Presets()[index], width, height),
            json =>
            {
                probe = JsonSerializer.Deserialize<List<TState>>(json, JsonOptionsFactory.Create()) ?? [];
                foreach (TState state in probe) probeFixup?.Invoke(state);
                return probe.Select(getName).ToList();
            },
            (window, index, width, height) => render(window, probe![index], width, height));
    }

    // Кандидаты в JSON указывают встроенную палитру только именем — полные цвета подставляются здесь.
    // Период цвета, отличный от значения по умолчанию, переносится из кандидата.
    private static MandelbrotPalette NamedPalette(MandelbrotPaletteManager manager, string name, int period = 500)
    {
        MandelbrotPalette palette = manager.Palettes.First(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Clone(name);
        if (period != 500) palette.ColorPeriod = period;
        return palette;
    }

    private sealed class PointOfInterestGroup(string name, Func<Window> createWindow, Func<List<string>> names,
        Func<Window, int, int, int, Task<BitmapSource>> render, Func<string, IReadOnlyList<string>> loadProbe,
        Func<Window, int, int, int, Task<BitmapSource>> renderProbe)
    {
        private List<string>? _names;
        public string Name { get; } = name;
        public IReadOnlyList<string> Names => _names ??= names();
        public Window CreateWindow() => createWindow();
        public Task<BitmapSource> RenderAsync(Window window, int index, int width, int height) => render(window, index, width, height);
        public IReadOnlyList<string> LoadProbe(string json) => loadProbe(json);
        public Task<BitmapSource> RenderProbeAsync(Window window, int index, int width, int height) => renderProbe(window, index, width, height);
    }

    /// <summary>
    /// Грубые, но устойчивые признаки «пустого» кадра. Цвета квантуются до 4 бит на канал, чтобы
    /// плавный градиент одной заливки не считался разнообразием.
    /// </summary>
    private readonly record struct FrameMetrics(double DominantShare, int SignificantColors, double LuminanceDeviation, double EdgeShare)
    {
        // Резкие переходы учитываются только вместе с низким контрастом: бассейны раскрашены гладко и
        // почти без переходов, но с заметным разбросом яркости.
        public bool IsFlat => DominantShare > 0.9 || SignificantColors < 3 && LuminanceDeviation < 20 || LuminanceDeviation < 6 ||
                              LuminanceDeviation < 10 && EdgeShare < 0.01;

        public override string ToString() => string.Create(CultureInfo.InvariantCulture,
            $"dom={DominantShare:P0} colors={SignificantColors,3} σ={LuminanceDeviation,5:F1} edges={EdgeShare:P0}");
    }

    private static FrameMetrics MeasureFrame(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int width = converted.PixelWidth, height = converted.PixelHeight, count = width * height;
        byte[] pixels = new byte[count * 4];
        converted.CopyPixels(pixels, width * 4, 0);
        int[] histogram = new int[4096];
        double[] luminance = new double[count];
        double sum = 0, sumSquares = 0;
        for (int i = 0; i < count; i++)
        {
            byte b = pixels[i * 4], g = pixels[i * 4 + 1], r = pixels[i * 4 + 2];
            histogram[(r >> 4) << 8 | (g >> 4) << 4 | b >> 4]++;
            double y = 0.2126 * r + 0.7152 * g + 0.0722 * b;
            luminance[i] = y; sum += y; sumSquares += y * y;
        }
        double mean = sum / count;
        int edges = 0;
        for (int y = 0; y < height - 1; y++)
        for (int x = 0; x < width - 1; x++)
        {
            int i = y * width + x;
            if (Math.Abs(luminance[i] - luminance[i + 1]) > 12 || Math.Abs(luminance[i] - luminance[i + width]) > 12) edges++;
        }
        return new FrameMetrics(histogram.Max() / (double)count, histogram.Count(value => value >= count / 1000),
            Math.Sqrt(Math.Max(0, sumSquares / count - mean * mean)), edges / (double)((width - 1) * (height - 1)));
    }

    private static void SavePng(BitmapSource image, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using FileStream stream = File.Create(path);
        encoder.Save(stream);
    }
}
