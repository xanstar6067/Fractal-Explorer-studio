using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

// No visible windows or screen capture. The snapshot callback supplies synthetic pixels.
internal static partial class Program
{
    // Необязательный фильтр групп проверок — полный набор идёт больше десяти минут, и при
    // работе над одной темой ждать его целиком незачем:
    //   без аргументов / all — всё;
    //   manager  — менеджер сохранений, хранилище по файлу на сохранение, Корзина и миграции данных;
    //   deep     — только глубокий зум (включает extreme);
    //   extreme  — только сверхглубокий зум (FloatExp-зум, 1e1000) и поиск ядра по Ньютону;
    //   phoenix  — только глубокий и сверхглубокий зум Феникса;
    //   newton   — только глубокий и сверхглубокий зум бассейнов Ньютона;
    //   tiled-preview [--record файл] [--compare файл] [--out папка] — тайловый предпросмотр
    //              двумерных окон; с --compare кадры сверяются с записанными хешами попиксельно;
    //   deep-viewport [--record файл] [--compare файл] — центр и зум окон с глубоким зумом:
    //              загрузка сохранений, ввод зума через порог, сброс вида, колесо и перетаскивание;
    //   deep-frames [--record файл] [--compare файл] [--time] — хеши мелких кадров всех пертурбационных
    //              ядер: быстрая побитная сверка до и после правки глубокого зума, --time — скорость;
    //   laboratory [--record файл] [--compare файл] — хеши кадров всех математических лабораторий во
    //              всех режимах: побитная сверка до и после правки рендерера лабораторий;
    //   shader-sources [--record файл] [--compare файл] [--compile префикс] [--dump папка] — хеши итоговых исходников всех GPU-шейдеров
    //              без компиляции: побайтная сверка до и после правки хранения и сборки шейдеров;
    //   basins   — все 11 режимов BasinExplorerWindow;
    //   planar   — градиентный спуск, комплексный поток и полиномиальные векторные поля;
    //   fractal3d — все виды трёхмерных фракталов: GPU-рендер, пресеты,
    //              навигация камеры, зонд поверхности под курсором и сохранения;
    //   fractal3d-modes [--record файл] [--compare файл] — всё, что зависит от режима 3D без ГП:
    //              каталог, состояния, описания сохранений, константы шейдера и разметка окна;
    //   attractors [папка PNG] — готовые виды семи 3D-аттракторов, JSON и окно;
    //   sprott [папка PNG] — коды, поиск, сохранение и плотность квадратичных 2D-карт;
    //   picker-marker — зелёная 3D минисфера выбора C и неизменность зонда поверхности;
    //   ifs-diagnostic <PNG> [warp] — контрольный кадр IFS на выбранном GPU или WARP;
    //   poi [фильтр] [--out папка] — встроенные точки интереса комплексной динамики не дают
    //              однотонный кадр (фильтр — часть имени группы, например Nova или Mandelbrot-Celtic);
    //   poi-probe <группа> <кандидаты.json> <папка> — рендер состояний-кандидатов без пересборки;
    //   catalog [--out папка] — каталог главного окна: данные, превью, поиск, разделы, избранное, недавние и привязки.
    [STAThread]
    private static int Main(string[] args)
    {
        string group = args.Length > 0 ? args[0].Trim().ToLowerInvariant() : "all";
        int result = 0;
        _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        // Никакая проверка не должна читать или писать настоящие данные пользователя в AppData.
        AppPaths.OverrideDataRoot(Path.Combine(AppContext.BaseDirectory, "VerificationData", "Shared"));
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        Dispatcher.CurrentDispatcher.InvokeAsync(async () =>
        {
            try
            {
                if (group is "all" or "phoenix-saves") await VerifyLegacyPhoenixSavesAsync();
                if (group == "phoenix-saves") return;
                if (group == "phoenix-atlas")
                {
                    await VerifyPhoenixParameterAtlasAsync();
                    return;
                }
                if (group == "julia-atlas")
                {
                    await VerifyJuliaParameterAtlasAsync();
                    return;
                }
                if (group == "phoenix-slices")
                {
                    await VerifyPhoenixParameterSlicesAsync(args);
                    return;
                }
                if (group is "all" or "mcmullen") await VerifyMcMullenAsync(args);
                if (group == "mcmullen") return;
                if (group is "all" or "rauzy") await VerifyRauzyAsync(args);
                if (group == "rauzy") return;
                if (group is "all" or "percolation") await VerifyPercolationAsync(args);
                if (group == "percolation") return;
                if (group == "transcendental-rays")
                {
                    await VerifyTranscendentalRaysAsync(args);
                    return;
                }
                if (group == "transcendental-julia")
                {
                    await VerifyTranscendentalJuliaAsync(args);
                    return;
                }
                if (group == "catalog-cache")
                {
                    await VerifyCatalogPreviewQueuesAsync();
                    await VerifyCatalogPreviewCacheAsync();
                    return;
                }
                if (group is "all" or "manager")
                {
                    VerifyUserData();
                    await VerifyManagerAsync();
                }
                if (group is "all" or "cloud") await VerifyCloudAsync();
                if (group == "cloud-live") await VerifyCloudLiveAsync();
                if (group is "all" or "deep") await VerifyDeepZoomAsync();
                if (group is "extreme") await VerifyExtremeZoomGroupAsync();
                if (group is "phoenix") await VerifyPhoenixDeepZoomAsync();
                if (group is "newton") await VerifyNewtonDeepZoomAsync();
                if (group is "tiled-preview") await VerifyTiledPreviewAsync(args);
                if (group is "deep-viewport") await VerifyDeepViewportAsync(args);
                if (group is "deep-frames") await VerifyDeepFramesAsync(args);
                if (group is "shader-sources") VerifyShaderSources(args);
                if (group is "laboratory") await VerifyLaboratoriesAsync(args);
                if (group is "all" or "basins") VerifyBasinExplorers();
                if (group == "all") await VerifyTranscendentalJuliaAsync(["transcendental-julia"]);
                if (group == "all") await VerifyTranscendentalRaysAsync(["transcendental-rays"]);
                if (group is "all" or "preparation3d") await Verify3DPreparationAsync(args);
                if (group is "all" or "fractal3d") await VerifyFractal3DAsync();
                if (group is "all" or "cube-rules") await VerifyCubeRulesAsync(args);
                if (group == "cube-rules") return;
                if (group is "all" or "quaternion-julia") await VerifyQuaternionJuliaAsync(args);
                if (group == "quaternion-julia") return;
                if (group is "all" or "quaternion-julia-atlas") await VerifyQuaternionJuliaAtlasAsync();
                if (group == "quaternion-julia-atlas") return;
                if (group is "all" or "phoenix3d") await VerifyPhoenix3DAsync(args);
                if (group == "phoenix3d") return;
                if (group is "all" or "juliabulb-atlas") await VerifyJuliabulbAtlasAsync(args);
                if (group == "juliabulb-atlas") return;
                if (group is "all" or "mandelbulb-atlas") await VerifyMandelShapeAtlasAsync(Fractal3DKind.Mandelbulb);
                if (group == "mandelbulb-atlas") return;
                if (group is "all" or "mandelbox-atlas") await VerifyMandelShapeAtlasAsync(Fractal3DKind.Mandelbox);
                if (group == "mandelbox-atlas") return;
                if (group is "all" or "burning-ship-atlas") await VerifyBurningShipAtlasAsync();
                if (group == "burning-ship-atlas") return;
                if (group is "all" or "bulbbox-hybrid-atlas") await VerifyBulbBoxHybridAtlasAsync();
                if (group == "bulbbox-hybrid-atlas") return;
                if (group is "all" or "kifs-atlas") await VerifyKifsAtlasAsync();
                if (group == "kifs-atlas") return;
                if (group is "all" or "sierpinski-atlas") await VerifySurfaceShapeAtlasAsync(Fractal3DKind.SierpinskiTetrahedron);
                if (group == "sierpinski-atlas") return;
                if (group is "all" or "periodic-surface-atlas") await VerifySurfaceShapeAtlasAsync(Fractal3DKind.PeriodicSurfaces);
                if (group == "periodic-surface-atlas") return;
                if (group is "all" or "implicit-surface-atlas") await VerifyImplicitSurfaceAtlasAsync();
                if (group == "implicit-surface-atlas") return;
                if (group is "all" or "newton3d-atlas") await VerifyNewton3DAtlasAsync();
                if (group == "newton3d-atlas") return;
                if (group is "all" or "lyapunov3d-atlas") await VerifyLyapunov3DAtlasAsync();
                if (group == "lyapunov3d-atlas") return;
                if (group is "all" or "kleinian-atlas") await VerifyKleinianAtlasAsync();
                if (group == "kleinian-atlas") return;
                if (group is "all" or "antoine-atlas") await VerifyAntoineAtlasAsync();
                if (group == "antoine-atlas") return;
                if (group is "all" or "terrain-atlas") await VerifyTerrainAtlasAsync();
                if (group == "terrain-atlas") return;
                if (group is "all" or "hopf-atlas") await VerifyHopfAtlasAsync();
                if (group == "hopf-atlas") return;
                if (group is "all" or "hyperbolic-honeycomb-atlas") await VerifyHyperbolicHoneycombAtlasAsync();
                if (group == "hyperbolic-honeycomb-atlas") return;
                if (group is "all" or "abc-flow-atlas") await VerifyAbcFlowAtlasAsync();
                if (group == "abc-flow-atlas") return;
                if (group is "all" or "cube-rule-atlas") await VerifyCubeRuleAtlasAsync();
                if (group == "cube-rule-atlas") return;
                if (group is "all" or "burning-ship-julia-atlas") await VerifyBurningShipJuliaAtlasAsync(args);
                if (group == "burning-ship-julia-atlas") return;
                if (group is "all" or "bulbbox-hybrid") await VerifyBulbBoxHybridAsync(args);
                if (group == "bulbbox-hybrid") return;
                if (group is "all" or "fractal3d-modes") await VerifyFractal3DModesAsync(args);
                if (group is "all" or "flame3d") await VerifyFlame3DAsync(args);
                if (group is "all" or "buddhabrot4d") await VerifyBuddhabrot4DAsync(args);
                if (group is "all" or "kobayashi3d") await VerifyKobayashi3DAsync(args);
                if (group is "all" or "kobayashi-random") await VerifyKobayashiRandomAsync(args);
                if (group is "all" or "excitable3d") await VerifyExcitable3DAsync(args);
                if (group is "all" or "ginzburg-landau3d") await VerifyGinzburgLandau3DAsync(args);
                if (group is "all" or "swift-hohenberg3d") await VerifySwiftHohenberg3DAsync(args);
                if (group is "all" or "phase-field-crystal3d") await VerifyPhaseFieldCrystal3DAsync(args);
                if (group is "all" or "gray-scott3d") await VerifyGrayScott3DAsync(args);
                if (group is "all" or "cahn-hilliard3d" or "cahn-hilliard-perf") await VerifyCahnHilliard3DAsync(args);
                if (group is "all" or "turing3d") await VerifyTuring3DAsync(args);
                if (group is "all" or "kifs") await VerifyKifsAsync(args);
                if (group is "all" or "kleinian") await VerifyKleinianAsync(args);
                if (group is "all" or "antoine") await VerifyAntoineAsync(args);
                if (group is "all" or "periodic-surfaces") await VerifyPeriodicSurfacesAsync(args);
                if (group is "all" or "implicit-surface") await VerifyImplicitSurfaceAsync(args);
                if (group is "all" or "hyperbolic-honeycomb") await VerifyHyperbolicHoneycombAsync(args);
                if (group is "all" or "dla3d") await VerifyDla3DAsync(args);
                if (group is "all" or "lichtenberg3d") await VerifyLichtenberg3DAsync(args);
                if (group is "all" or "lsystem3d") await VerifyLSystem3DAsync(args);
                if (group is "all" or "lenia3d") await VerifyLenia3DAsync(args);
                if (group is "all" or "lyapunov3d") await VerifyLyapunov3DAsync(args);
                if (group is "all" or "newton3d") await VerifyNewton3DAsync(args);
                if (group is "all" or "physarum3d") await VerifyPhysarum3DAsync(args);
                if (group is "all" or "physarum-random") await VerifyPhysarumRandomAsync(args);
                if (group is "all" or "abc-flow") await VerifyAbcFlowAsync(args);
                if (group is "all" or "hopf") await VerifyHopfAsync(args);
                if (group is "all" or "lsystemrandom") await VerifyLSystemRandomizerAsync(args);
                if (group == "attractors") await VerifyAttractors3DAsync(args);
                if (group == "sprott") VerifySprott(args);
                if (group is "all" or "symmetric-icons") await VerifySymmetricIconsAsync(args);
                if (group is "all" or "popcorn") await VerifyPopcornAsync(args);
                if (group is "all" or "hopalong") await VerifyHopalongAsync(args);
                if (group is "all" or "snow-crystal") await VerifySnowCrystalAsync(args);
                if (group is "all" or "sandpile") await VerifySandpileAsync(args);
                if (group is "all" or "turing-reactions") await VerifyTuringReactionsAsync(args);
                if (group is "all" or "turing") await VerifyTuringAsync(args);
                if (group is "all" or "turing-gpu") await VerifyTuringGpuAsync(args);
                if (group == "turing-perf") await MeasureTuringFramesAsync();
                if (group is "all" or "gray-scott-gpu") await VerifyGrayScottGpuAsync(args);
                if (group == "gray-scott-perf") await MeasureGrayScottFramesAsync();
                if (group is "all" or "gray-scott") await VerifyGrayScottPresetsAsync(args);
                if (group is "all" or "gray-scott-random") await VerifyGrayScottRandomizerAsync(args);
                if (group is "all" or "terrain") await VerifyTerrainAsync();
                if (group == "picker-marker")
                {
                    using var pickerRenderer = new FractalExplorerWPF.Core.Rendering3D.Fractal3DRenderer();
                    await VerifyFractal3DPickerMarkerAsync(pickerRenderer);
                    await VerifyPickerWindowFrameAsync();
                    Console.WriteLine("PASS (picker-marker): first WPF frame, live drag frames, one GPU marker and unchanged surface probe.");
                }
                if (group is "all" or "mandelbrot-saves") await VerifyMandelbrotSavesAsync(args);
                if (group is "all" or "julia-saves") await VerifyLegacyJuliaSavesAsync();
                if (group is "all" or "julia-family") await VerifyJuliaFamilyAsync();
                if (group is "all" or "perpendicular-family") await VerifyPerpendicularFamilyAsync();
                if (group == "folded-final") await VerifyFoldedFinalAsync();
                if (group == "folded-presets-probe") await ProbePolynomialPresets();
                if (group is "all" or "folded-polynomials") await VerifyFoldedPolynomialsAsync(args);
                if (group is "all" or "folded-cubic") await VerifyFoldedCubicFamilyAsync();
                if (group == "julia-gpu") await VerifyJuliaGpuAsync();
                if (group == "catalog-perf") await MeasureCatalogPreviewsAsync(args);
                if (group == "shadercache") await VerifyShaderCacheAsync();
                // Reproduce the close camera angle that once exposed voxel blocks and ray-limit rings.
                if (group == "ifs-close") await WriteIfsClosePreviewAsync(args);
                if (group == "ifs-diagnostic") await WriteIfsDiagnosticAsync(args);
                if (group == "planar") VerifyPlanarBasins();
                if (group is "basins" or "planar" && args.Length == 3 && args[1] == "--planar-previews") WritePlanarBasinPreviews(args[2]);
                if (group == "basins" && args.Length == 3 && args[1] == "--previews") WriteNewBasinPreviews(args[2]);
                if (group == "basins" && args.Length == 3 && args[1] == "--physical-previews") WriteNewBasinPreviews(args[2], physicalOnly: true);
                if (group is "all" or "numeric") VerifyNumericSpinners();
                if (group is "all" or "numeric-input") await VerifyNumericInputAsync();
                if (group == "numeric-input") return;
                if (group is "all" or "catalog")
                {
                    int outIndex = group == "catalog" ? Array.IndexOf(args, "--out") : -1;
                    await VerifyCatalogAsync(outIndex >= 0 && outIndex + 1 < args.Length ? args[outIndex + 1] : null);
                }
                if (group is "all" or "poi")
                {
                    string? filter = group == "poi" && args.Length > 1 && !args[1].StartsWith("--") ? args[1] : null;
                    int outIndex = Array.IndexOf(args, "--out");
                    await VerifyPointsOfInterestAsync(filter, outIndex >= 0 && outIndex + 1 < args.Length ? args[outIndex + 1] : null);
                }
                if (group == "poi-probe")
                {
                    if (args.Length != 4) throw new ArgumentException("poi-probe <группа> <кандидаты.json> <папка PNG>");
                    await ProbePointsOfInterestAsync(args[1], args[2], args[3]);
                }
                if (group is not ("tiled-preview" or "laboratory" or "deep-viewport" or "deep-frames" or "shader-sources" or "fractal3d-modes" or "abc-flow" or "hyperbolic-honeycomb" or "implicit-surface" or "periodic-surfaces" or "preparation3d" or "phase-field-crystal3d" or "swift-hohenberg3d" or "ginzburg-landau3d" or "excitable3d" or "lyapunov3d" or "newton3d" or "kobayashi-random" or "kobayashi3d" or "lenia3d" or "physarum-random" or "physarum3d" or "mandelbrot-saves" or "julia-saves" or "julia-gpu" or "julia-family" or "perpendicular-family" or "folded-cubic" or "folded-polynomials" or "folded-presets-probe" or "folded-final" or "hopf" or "cahn-hilliard3d" or "cahn-hilliard-perf" or "turing-reactions" or "gray-scott3d" or "turing3d" or "gray-scott" or "gray-scott-random" or "gray-scott-gpu" or "gray-scott-perf" or "buddhabrot4d" or "turing-gpu" or "turing-perf" or "turing" or "hopalong" or "snow-crystal" or "sandpile" or "popcorn" or "symmetric-icons" or "lsystemrandom" or "lsystem3d" or "dla3d" or "lichtenberg3d" or "kifs" or "kleinian" or "antoine" or "all" or "terrain" or "manager" or "deep" or "extreme" or "phoenix" or "newton" or "basins" or "planar" or "fractal3d" or "flame3d" or "attractors" or "sprott" or "picker-marker" or "shadercache" or "ifs-close" or "ifs-diagnostic" or "cloud" or "cloud-live" or "numeric" or "poi" or "poi-probe" or "catalog" or "catalog-perf"))
                    throw new ArgumentException($"Неизвестная группа проверок «{group}». Допустимы: preparation3d, phase-field-crystal3d, swift-hohenberg3d, ginzburg-landau3d, excitable3d, lyapunov3d, newton3d, physarum3d, lichtenberg3d, hopf, hopalong, all, manager, deep, extreme, phoenix, newton, basins, planar, fractal3d, ifs-diagnostic, poi, poi-probe, catalog.");
                if (group is not ("tiled-preview" or "laboratory" or "deep-viewport" or "deep-frames" or "shader-sources" or "fractal3d-modes" or "abc-flow" or "hyperbolic-honeycomb" or "implicit-surface" or "periodic-surfaces" or "preparation3d" or "phase-field-crystal3d" or "swift-hohenberg3d" or "ginzburg-landau3d" or "excitable3d" or "lyapunov3d" or "newton3d" or "kobayashi-random" or "kobayashi3d" or "lenia3d" or "physarum-random" or "physarum3d" or "mandelbrot-saves" or "julia-saves" or "julia-gpu" or "julia-family" or "perpendicular-family" or "folded-cubic" or "folded-polynomials" or "folded-presets-probe" or "folded-final" or "hopf" or "cahn-hilliard3d" or "cahn-hilliard-perf" or "turing-reactions" or "gray-scott3d" or "turing3d" or "gray-scott" or "gray-scott-random" or "gray-scott-gpu" or "gray-scott-perf" or "buddhabrot4d" or "turing-gpu" or "turing-perf" or "turing" or "hopalong" or "snow-crystal" or "sandpile" or "popcorn" or "symmetric-icons" or "lsystemrandom" or "lsystem3d" or "dla3d" or "lichtenberg3d" or "kifs" or "kleinian" or "antoine" or "terrain" or "cloud" or "cloud-live" or "numeric" or "fractal3d" or "flame3d" or "attractors" or "sprott" or "picker-marker" or "shadercache" or "ifs-close" or "ifs-diagnostic" or "poi" or "poi-probe" or "catalog" or "catalog-perf"))
                    Console.WriteLine($"PASS ({group}): preview selection, snapshot persistence, progress, cancellation, stale results, errors, presets, deep zoom and extreme zoom.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                result = 1;
            }
            finally { Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
        });
        Dispatcher.Run();
        return result;
    }

    private static async Task DrainAsync()
    {
        for (int i = 0; i < 3; i++) await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }
    private static BitmapSource LoadPng(string path)
    {
        using FileStream stream = File.OpenRead(path);
        BitmapSource bitmap = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        bitmap.Freeze(); return bitmap;
    }
    // ---------------------------------------------------------------- extended-range zoom
    // The zoom factor, the frame grid and the Distance Estimation derivative moved from
    // double to FloatExp (double mantissa + 32-bit binary exponent), lifting the depth
    // ceiling from 1e90 to 1e1000. These checks cover the numeric type itself, then the
    // renderer at depths where the old double pipeline collapsed (3/zoom underflows to 0 past
    // ~1.8e308, so every pixel of a frame would have received the same dc).

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
