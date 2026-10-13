using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    // ---------------------------------------------------------------- tiled preview
    // Прогрессивный тайловый предпросмотр двумерных окон (Мандельброт, Ньютон, Nova, Phoenix,
    // Коллатц, бассейны, раскраска функций, карта выбора C Nova). Окно открывается за
    // пределами экрана и само рисует кадр обычным путём — по таймеру после загрузки и после
    // LoadState; итог берётся из стабильного слоя предпросмотра.
    //
    //   tiled-preview [--record файл] [--compare файл] [--out папка]
    //
    // --record пишет SHA-256 пикселей каждого кадра, --compare сверяет с записанными: так
    // проверяется, что изменение конвейера предпросмотра не поменяло ни одного пикселя.
    // Без ключей группа только убеждается, что каждый кадр дорисован и не однотонный.

    private sealed record TiledPreviewCase(string Name, Func<Window> Create, double ZoomFactor, double ShiftX, double ShiftY,
        string? ExactX = null, string? ExactY = null, int? Iterations = null);

    private static async Task VerifyTiledPreviewAsync(string[] args)
    {
        string? record = ReadOption(args, "--record");
        string? compare = ReadOption(args, "--compare");
        string? outDirectory = ReadOption(args, "--out");
        if (outDirectory is not null) Directory.CreateDirectory(outDirectory);
        var themeStyles = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if (!Application.Current.Resources.MergedDictionaries.Any(d => d.Source == themeStyles))
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = themeStyles });

        TiledPreviewCase[] cases =
        [
            new("mandelbrot", () => new MandelbrotWindow(MandelbrotVariant.Mandelbrot), 1e6, -0.743643887037151, 0.131825904205330),
            // Глубокий кадр из группы deep: за порогом 1e25 его считает пертурбационный движок,
            // поэтому центр задан строкой точной точности.
            new("mandelbrot-deep", () => new MandelbrotWindow(MandelbrotVariant.Mandelbrot), 5.7607143988620999e25,
                -1.2628848671045503000020782246, 0.0409687601493310685285376264,
                "-1.2628848671045503000020782246", "0.0409687601493310685285376264", Iterations: 4500),
            // Глубокие точки ниже взяты из групп newton, nova, phoenix и collatz: у кадров есть
            // структура, и за порогом 1.5e9 их считает пертурбационный движок окна.
            // −2^(−1/3) — прообраз полюса метода Ньютона для z³−1, лежит на границе бассейнов.
            new("newton", () => new NewtonPoolsWindow(), 1e12, -0.7937005259840997, 0,
                "-0.79370052598409973737585281963615413", "0"),
            new("nova", () => new NovaWindow(NovaVariant.Mandelbrot), 2.5e12, -0.5095166183651508, 0.1244652640381422,
                "-0.509516618365150777259563561165083570374599345148385054482531586472759954631328582763671875",
                "0.12446526403814218906112533265557181617030335358912995769031795134651474654674530029296875", Iterations: 300),
            new("phoenix", () => new PhoenixWindow(), 1.10e12, 0.3605697876344493, 0.9162050700528198,
                "0.3605697876344492991196400742422874", "0.9162050700528197582748546315293717"),
            new("collatz", () => new CollatzWindow(), 1e9, -0.8691778646221384, 0.003351110447198153,
                "-0.869177864622138448380772548135348733365049368", "0.003351110447198153027105397611632949120554176"),
            new("basin-muller", () => new BasinExplorerWindow(BasinExplorerKind.Muller), 40, 0.2, 0.15),
            new("basin-pendulum", () => new BasinExplorerWindow(BasinExplorerKind.MagneticPendulum), 6, 0.2, 0.15),
            new("domain-coloring", () => new DomainColoringWindow(), 25, 0.5, 0.3),
            new("nova-selector", () => new NovaParameterSelectorWindow(new NovaState { PReal = 3, Z0Real = 1, M = 1 }), 0, 0, 0),
        ];

        var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (TiledPreviewCase test in cases)
        {
            Window window = test.Create();
            window.Width = 980;
            window.Height = 680;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -20000;
            window.Top = -20000;
            window.ShowInTaskbar = false;
            window.ShowActivated = false;
            window.Show();
            try
            {
                await CaptureTiledFrameAsync(window, $"{test.Name}/default", hashes, outDirectory);

                if (test.ZoomFactor > 0)
                {
                    LoadZoomedState(window, test);
                    await CaptureTiledFrameAsync(window, $"{test.Name}/zoomed", hashes, outDirectory);
                }

                // SSAA 2× меняет и размер холста, и размер тайлов (16 × множитель).
                if (FindSsaaBox(window) is { } ssaa && ssaa.Items.Count > 1)
                {
                    ssaa.SelectedIndex = 1;
                    await CaptureTiledFrameAsync(window, $"{test.Name}/ssaa2", hashes, outDirectory);
                }
            }
            finally
            {
                window.Close();
                await DrainAsync();
            }
        }

        if (record is not null)
        {
            File.WriteAllLines(record, hashes.Select(pair => $"{pair.Key} {pair.Value}"));
            Console.WriteLine($"Записано {hashes.Count} хешей кадров в {record}.");
        }
        if (compare is not null)
        {
            Dictionary<string, string> expected = File.ReadAllLines(compare)
                .Where(line => line.Length > 0)
                .Select(line => line.Split(' ', 2))
                .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);
            var mismatches = new List<string>();
            foreach ((string key, string hash) in hashes)
            {
                if (!expected.TryGetValue(key, out string? reference)) mismatches.Add($"{key}: нет эталона");
                else if (reference != hash) mismatches.Add($"{key}: пиксели отличаются от эталона");
            }
            mismatches.AddRange(expected.Keys.Where(key => !hashes.ContainsKey(key)).Select(key => $"{key}: кадр не снят"));
            Check(mismatches.Count == 0, "Тайловый предпросмотр изменился:\n  " + string.Join("\n  ", mismatches));
            Console.WriteLine($"Все {hashes.Count} кадров совпадают с эталоном {compare} попиксельно.");
        }
        Console.WriteLine($"PASS (tiled-preview): {hashes.Count} кадров в {cases.Length} окнах дорисованы.");
    }

    private static string? ReadOption(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static ComboBox? FindSsaaBox(Window window) =>
        window.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(field => field.FieldType == typeof(ComboBox) &&
                            field.Name.Contains("Ssaa", StringComparison.OrdinalIgnoreCase))
            .OrderBy(field => field.Name.Contains("Export", StringComparison.OrdinalIgnoreCase))
            .Select(field => field.GetValue(window) as ComboBox)
            .FirstOrDefault(box => box is not null);

    /// <summary>
    /// Снимает состояние окна, домножает зум и переносит центр, затем загружает обратно —
    /// тем же путём, каким открывается сохранение. Типы зума и центра у окон разные.
    /// </summary>
    private static void LoadZoomedState(Window window, TiledPreviewCase test)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        object state = window.GetType().GetMethod("CaptureState", flags, [typeof(string)])!
            .Invoke(window, ["tiled-preview"])!;
        PropertyInfo zoom = state.GetType().GetProperty("Zoom")!;
        object current = zoom.GetValue(state)!;
        zoom.SetValue(state, current switch
        {
            FloatExp value => value * test.ZoomFactor,
            double value => value * test.ZoomFactor,
            decimal value => value * (decimal)test.ZoomFactor,
            _ => throw new InvalidOperationException($"Неизвестный тип зума {current.GetType()}.")
        });
        SetNumber(state, "CenterX", test.ShiftX);
        SetNumber(state, "CenterY", test.ShiftY);
        state.GetType().GetProperty("CenterXExact")?.SetValue(state, test.ExactX);
        state.GetType().GetProperty("CenterYExact")?.SetValue(state, test.ExactY);
        if (test.Iterations is int iterations) state.GetType().GetProperty("Iterations")!.SetValue(state, iterations);
        window.GetType().GetMethod("LoadState", flags, [state.GetType()])!.Invoke(window, [state]);
    }

    private static void SetNumber(object target, string name, double value)
    {
        PropertyInfo property = target.GetType().GetProperty(name)!;
        property.SetValue(target, property.PropertyType == typeof(decimal) ? (decimal)value : value);
    }

    /// <summary>
    /// Ждёт, пока окно закончит рендер (таймер отложенного рендера остановлен, флаг рендера
    /// снят, стабильный кадр стоит и не меняется), и записывает хеш его пикселей.
    /// </summary>
    private static async Task CaptureTiledFrameAsync(Window window, string key,
        IDictionary<string, string> hashes, string? outDirectory)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        Type type = window.GetType();
        FieldInfo timerField = type.GetField("_renderTimer", flags)!;
        FieldInfo renderingField = (type.GetField("_isRendering", flags) ?? type.GetField("_rendering", flags))!;
        var image = (Image)(window.FindName("StablePreviewImage") ?? window.FindName("StableImage"))!;

        // Изменение, запустившее рендер, сначала взводит таймер: даём ему сработать.
        await Task.Delay(50);
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromMinutes(3);
        ImageSource? settled = null;
        int stableChecks = 0;
        while (stableChecks < 4)
        {
            Check(DateTime.UtcNow < deadline, $"{key}: кадр не дорисовался за три минуты.");
            await Task.Delay(150);
            await DrainAsync();
            bool busy = ((DispatcherTimer)timerField.GetValue(window)!).IsEnabled || (bool)renderingField.GetValue(window)!;
            ImageSource? source = image.Source;
            if (busy || source is null || !ReferenceEquals(source, settled))
            {
                settled = source;
                stableChecks = 0;
                continue;
            }
            stableChecks++;
        }

        var bitmap = (BitmapSource)settled!;
        var bgra = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        int stride = bgra.PixelWidth * 4;
        byte[] pixels = new byte[stride * bgra.PixelHeight];
        bgra.CopyPixels(pixels, stride, 0);
        if (outDirectory is not null)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bgra));
            using FileStream stream = File.Create(Path.Combine(outDirectory, key.Replace('/', '_') + ".png"));
            encoder.Save(stream);
        }
        Check(pixels.Chunk(4).Select(p => (p[0], p[1], p[2])).Distinct().Take(2).Count() == 2,
            $"{key}: кадр однотонный — рендер не дошёл до холста.");

        string hash = Convert.ToHexString(SHA256.HashData(pixels));
        hashes[key] = $"{bgra.PixelWidth}x{bgra.PixelHeight}:{hash}";
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  {key}: {bgra.PixelWidth}×{bgra.PixelHeight}, {hash[..16]}…"));
    }
}
