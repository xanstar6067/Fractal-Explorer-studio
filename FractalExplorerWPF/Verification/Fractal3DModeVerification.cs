using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    // ---------------------------------------------------------------- fractal3d-modes
    // Всё, что зависит от пункта Fractal3DKind и не требует ГП: определение, признаки общих
    // полей, подписи и доступность источников цвета, стартовое состояние и готовые виды (JSON),
    // описание в менеджере сохранений, константы шейдера и высота полосы кадра, разметка
    // окна (подписи, видимые панели, диапазоны полей «Итерации» и «Степень»).
    //
    //   fractal3d-modes [--record файл] [--compare файл]
    //
    // --record пишет снимок, --compare сверяет построчно: так перенос режимов в отдельные
    // классы проверяется на старом коде. Без ключей идут только проверки полноты каталога.

    private static async Task VerifyFractal3DModesAsync(string[] args)
    {
        string? record = ReadOption(args, "--record");
        string? compare = ReadOption(args, "--compare");
        EnsureWindowThemeStyles();

        var lines = new List<string>();
        Fractal3DKind[] kinds = Enum.GetValues<Fractal3DKind>();
        var categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var prefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Fractal3DKind kind in kinds)
        {
            Fractal3DDefinition definition = Fractal3DCatalog.GetDefinition(kind);
            Check(categories.Add(definition.SaveCategory), $"{kind}: категория сохранений {definition.SaveCategory} повторяется.");
            Check(prefixes.Add(definition.ExportPrefix), $"{kind}: префикс экспорта {definition.ExportPrefix} повторяется.");
            IReadOnlyList<Fractal3DState> presets = Fractal3DCatalog.GetPresets(kind);
            Check(presets.Count > 0, $"{kind}: нет готовых видов.");
            Check(presets.All(preset => preset.Kind == kind), $"{kind}: готовый вид другого режима.");
            Check(presets.Select(preset => preset.SaveName).Distinct().Count() == presets.Count, $"{kind}: имена готовых видов повторяются.");
            Check(Fractal3DCatalog.CreateDefaultState(kind).Kind == kind, $"{kind}: стартовое состояние другого режима.");
            Check(!Fractal3DCatalog.IsGpuSimulation(kind) || Fractal3DCatalog.IsDensityVolume(kind),
                $"{kind}: моделирование на ГП должно трассировать объём плотности.");
            Check(Fractal3DCatalog.TryParseLaunchKey(Fractal3DCatalog.LaunchKey(kind), out Fractal3DKind parsed) && parsed == kind,
                $"{kind}: ключ запуска не читается обратно.");

            lines.AddRange(DescribeModeData(kind));
            lines.AddRange(await DescribeModeWindowAsync(kind));
        }
        // Одна запись — одна строка файла, даже если в описании есть перевод строки.
        lines = lines.Select(line => line.Replace("\r", "\\r").Replace("\n", "\\n")).ToList();

        if (record is not null)
        {
            File.WriteAllLines(record, lines);
            Console.WriteLine($"Записано {lines.Count} строк снимка режимов в {record}.");
        }
        if (compare is not null)
        {
            string[] expected = File.ReadAllLines(compare).Where(line => line.Length > 0).ToArray();
            var mismatches = new List<string>();
            for (int i = 0; i < Math.Max(expected.Length, lines.Count); i++)
            {
                string? before = i < expected.Length ? expected[i] : null;
                string? after = i < lines.Count ? lines[i] : null;
                if (before != after) mismatches.Add($"было: {before ?? "—"}\n    стало: {after ?? "—"}");
                if (mismatches.Count >= 20) break;
            }
            Check(mismatches.Count == 0, "Снимок режимов изменился:\n  " + string.Join("\n  ", mismatches));
            Console.WriteLine($"Все {lines.Count} строк снимка режимов совпадают с эталоном {compare}.");
        }
        Console.WriteLine($"PASS (fractal3d-modes): {kinds.Length} режимов, {lines.Count} строк снимка.");
    }

    private static IEnumerable<string> DescribeModeData(Fractal3DKind kind)
    {
        Fractal3DDefinition definition = Fractal3DCatalog.GetDefinition(kind);
        yield return $"{kind} definition {definition.Title} | {definition.PanelTitle} | {definition.Description} | " +
                     $"{definition.SaveCategory} | {definition.ExportPrefix}";
        yield return $"{kind} flags power={Fractal3DCatalog.UsesPower(kind)} julia={Fractal3DCatalog.UsesJuliaConstant(kind)} " +
                     $"ship={Fractal3DCatalog.IsBurningShip(kind)} volume={Fractal3DCatalog.IsDensityVolume(kind)} " +
                     $"gpu={Fractal3DCatalog.IsGpuSimulation(kind)} iterations={Fractal3DCatalog.UsesIterations(kind)} " +
                     $"bailout={Fractal3DCatalog.UsesBailout(kind)}";
        foreach (Fractal3DColoringMode mode in Enum.GetValues<Fractal3DColoringMode>())
            yield return $"{kind} coloring {mode} {Fractal3DCatalog.HasColoringMode(mode, kind)} {Fractal3DCatalog.ColoringModeName(mode, kind)}";

        foreach (string line in DescribeModeState(kind, "default", Fractal3DCatalog.CreateDefaultState(kind)))
            yield return line;
        Fractal3DState probe = Fractal3DCatalog.CreateDefaultState(kind);
        PerturbSharedShape(probe);
        foreach (string line in DescribeModeState(kind, "probe", probe))
            yield return line;
        IReadOnlyList<Fractal3DState> presets = Fractal3DCatalog.GetPresets(kind);
        for (int index = 0; index < presets.Count; index++)
            foreach (string line in DescribeModeState(kind, $"preset{index}", presets[index]))
                yield return line;
    }

    private static IEnumerable<string> DescribeModeState(Fractal3DKind kind, string label, Fractal3DState state)
    {
        yield return $"{kind} {label} json {JsonSerializer.Serialize(state, new JsonSerializerOptions(JsonOptionsFactory.Create()) { WriteIndented = false })}";
        string details = (string)typeof(SaveManagerConfigurations)
            .GetMethod("DescribeFractal3D", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [state.Clone()])!;
        yield return $"{kind} {label} details {details.Replace("\n", "\\n")}";
        // Отметка времени попадает в описание сохранения; точка интереса её не имеет.
        Fractal3DState dated = state.Clone();
        dated.Timestamp = new DateTime(2026, 10, 13, 12, 34, 0);
        details = (string)typeof(SaveManagerConfigurations)
            .GetMethod("DescribeFractal3D", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [dated])!;
        yield return $"{kind} {label} dated {details.Replace("\n", "\\n")}";
        yield return $"{kind} {label} constants {Fractal3DFrameConstantsHex(state.Clone(), 640, 480, 0)} " +
                     $"{Fractal3DFrameConstantsHex(state.Clone(), 333, 200, 17)}";
        MethodInfo strip = typeof(Fractal3DRenderer).GetMethod("ComputeStripRows", BindingFlags.Static | BindingFlags.NonPublic)!;
        yield return $"{kind} {label} strips " + string.Join(' ', new[] { (640, 480), (1920, 1080), (7, 5), (3000, 2000) }
            .Select(size => strip.Invoke(null, [state.Clone(), size.Item1, size.Item2])!.ToString()));
    }

    /// <summary>Байты буфера констант кадра, которые уходят в шейдер; палитра — отдельный хвост.</summary>
    private static string Fractal3DFrameConstantsHex(Fractal3DState state, int width, int height, int offsetY)
    {
        object constants = typeof(Fractal3DRenderer)
            .GetMethod("BuildConstants", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [state, width, height, offsetY])!;
        int size = Marshal.SizeOf(constants);
        nint buffer = Marshal.AllocHGlobal(size);
        try
        {
            var bytes = new byte[size];
            Marshal.Copy(bytes, 0, buffer, size);
            Marshal.StructureToPtr(constants, buffer, false);
            Marshal.Copy(buffer, bytes, 0, size);
            return Convert.ToHexString(bytes);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    /// <summary>
    /// Разные значения во всех общих полях формы: если режим перепутает, какое поле в какой
    /// слот констант кладёт, совпадение пресетов по случайности этого не скроет.
    /// </summary>
    private static void PerturbSharedShape(Fractal3DState state)
    {
        state.Iterations = 12;
        state.Power = 5.25;
        state.Bailout = 7.5;
        state.BurningShipFormula = BurningShip3DFormula.SphericalFullFold;
        state.JuliaCX = 0.11; state.JuliaCY = -0.22; state.JuliaCZ = 0.33; state.JuliaCW = -0.44;
        state.PhoenixMemoryX = -0.55; state.PhoenixMemoryY = 0.66; state.PhoenixMemoryZ = -0.77;
        state.PhoenixSecondaryPower = 1;
        state.QuaternionSlice = 0.123;
        state.BoxScale = -1.75; state.BoxMinRadius = 0.42; state.BoxFoldingLimit = 1.3;
        state.BoxInversionShape = BoxInversionShape.RoundedCube;
        state.BoxInversionStretch = 1.4; state.BoxInversionPower = 7;
        state.HybridMix = 0.3; state.HybridBulbSteps = 2; state.HybridBoxSteps = 3; state.HybridOrder = Hybrid3DOrder.BoxFirst;
        state.SierpinskiScale = 1.9; state.CubeThickness = 0.8;
        state.ColorScale = 1.7; state.ColorOffset = 0.2;
        state.MaxSteps = 300; state.Detail = 0.7; state.MaxDistance = 40;
        state.CameraRoll = 12.5;
    }

    /// <summary>Разметка окна режима: подписи, свёрнутые элементы и диапазоны общих полей.</summary>
    private static async Task<List<string>> DescribeModeWindowAsync(Fractal3DKind kind)
    {
        var lines = new List<string>();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var window = new Fractal3DWindow(kind);
        try
        {
            T Named<T>(string name) where T : class => (T)window.FindName(name);
            lines.Add($"{kind} window {window.Title} | {Named<TextBlock>("PanelTitleText").Text} | " +
                      $"{Named<TextBlock>("IterationsLabel").Text} | {Named<TextBlock>("PowerLabel").Text} | " +
                      $"{Named<Expander>("ShapeExpander").Header} | {((TextBlock)Named<Panel>("JuliaPanel").Children[0]).Text} | " +
                      $"{Named<Button>("JuliaPickerButton").Content} | {Named<Button>("PaletteManagerButton").ToolTip} | " +
                      $"{Named<ColumnDefinition>("ControlsColumn").Width}");
            foreach (string box in new[] { "ColoringModeBox", "ShadingStyleBox" })
                lines.Add($"{kind} window {box} " + string.Join(" | ", Named<ComboBox>(box).Items.OfType<ComboBoxItem>()
                    .Select(item => $"{item.Content}{(item.Visibility == Visibility.Visible ? "" : " (скрыт)")}")));
            var collapsed = new List<string>();
            void Walk(object node)
            {
                if (node is FrameworkElement element && !string.IsNullOrEmpty(element.Name) && element.Visibility != Visibility.Visible)
                    collapsed.Add(element.Name);
                if (node is not DependencyObject dependency) return;
                foreach (object child in LogicalTreeHelper.GetChildren(dependency)) Walk(child);
            }
            Walk(window);
            lines.Add($"{kind} window collapsed {string.Join(' ', collapsed.Order(StringComparer.Ordinal))}");

            MethodInfo capture = typeof(Fractal3DWindow).GetMethod("CaptureViewFields", flags)!;
            string Probe(string boxName, string text)
            {
                TextBox box = Named<TextBox>(boxName);
                string previous = box.Text;
                box.Text = text;
                try
                {
                    var state = (Fractal3DState)capture.Invoke(window, ["probe"])!;
                    return $"{text}→{(boxName == "IterationsBox" ? state.Iterations : state.Power).ToString(CultureInfo.InvariantCulture)}";
                }
                catch (TargetInvocationException exception) { return $"{text}→{exception.InnerException?.Message}"; }
                finally { box.Text = previous; }
            }
            lines.Add($"{kind} window iterations " + string.Join(" ; ", new[]
                { "0", "1", "5", "6", "64", "65", "9999", "10000", "49999", "50000", "5000000", "5000001", "10000000", "10000001" }
                .Select(text => Probe("IterationsBox", text))));
            lines.Add($"{kind} window power " + string.Join(" ; ", new[]
                { "-33", "-32", "1.5", "2", "2.5", "6", "7", "12", "13", "16", "17", "32", "33" }
                .Select(text => Probe("PowerBox", text))));
        }
        finally
        {
            window.Close();
            var timeout = System.Diagnostics.Stopwatch.StartNew();
            while (typeof(Fractal3DWindow).GetField("_simulation", flags)!.GetValue(window) is IFractal3DSimulationMode { IsBusy: true } &&
                   timeout.ElapsedMilliseconds < 60000)
                await Task.Delay(10);
        }
        return lines;
    }
}
