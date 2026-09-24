using System.IO;
using System.Numerics;
using System.Text.Json.Nodes;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;

// Трёхмерные фракталы: дистанционные оценки семи видов на GPU, их пресеты и сохранения.
// Окна не показываются, кадры считаются в маленьком разрешении.
internal static partial class Program
{
    private const int Fractal3DProbeWidth = 120;
    private const int Fractal3DProbeHeight = 90;

    // Зонд поверхности меряется на кадре покрупнее: порог попадания луча пропорционален высоте
    // кадра, и на 90 строках две близкие дистанции различались бы хуже допуска проверки.
    private const int Fractal3DRayWidth = 480;
    private const int Fractal3DRayHeight = 360;

    private static async Task WriteIfsDiagnosticAsync(string[] args)
    {
        if (args.Length is < 2 or > 3) throw new ArgumentException("ifs-diagnostic <output PNG> [warp]");
        if (args.Length == 3 && args[2] != "warp") throw new ArgumentException("Expected 'warp' as the third argument.");
        Fractal3DState state = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Ifs3D);
        bool warp = args.Length == 3 && args[2] == "warp";
        using var renderer = new Fractal3DRenderer(warp);
        BitmapSource bitmap = await renderer.RenderAsync(state, warp ? 440 : 872, warp ? 440 : 740,
            null, CancellationToken.None);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream stream = File.Create(args[1]);
        encoder.Save(stream);
        Console.WriteLine($"Saved IFS diagnostic frame to {args[1]}");
    }

    private static async Task WriteIfsClosePreviewAsync(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("ifs-close <output directory>");
        Directory.CreateDirectory(args[1]);
        Fractal3DState state = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Ifs3D);
        state.CameraYaw = -168.1;
        state.CameraPitch = 34.71;
        state.CameraRoll = -104.4;
        state.CameraDistance = 0.147266;
        state.TargetX = 0.0154;
        state.TargetY = -0.098;
        state.TargetZ = 0.7804;
        using var renderer = new Fractal3DRenderer();
        for (int index = 0; index < 2; index++)
        {
            state.TargetX = 0.0154 + index * 0.002;
            BitmapSource bitmap = await renderer.RenderAsync(state, 872, 740, null, CancellationToken.None);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using FileStream stream = File.Create(Path.Combine(args[1], $"ifs-close-{index}.png"));
            encoder.Save(stream);
        }
        state = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Ifs3D);
        foreach (Fractal3DShadingStyle style in Enum.GetValues<Fractal3DShadingStyle>())
        {
            state.ShadingStyle = style;
            BitmapSource bitmap = await renderer.RenderAsync(state, 440, 440, null, CancellationToken.None);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using FileStream stream = File.Create(Path.Combine(args[1], $"ifs-style-{(int)style}.png"));
            encoder.Save(stream);
        }
        state.ShadingStyle = Fractal3DShadingStyle.Classic;
        state.Palette = Fractal3DPalettes.All[6].Clone();
        foreach (Fractal3DColoringMode mode in new[]
        {
            Fractal3DColoringMode.Normal, Fractal3DColoringMode.Depth,
            Fractal3DColoringMode.Height, Fractal3DColoringMode.Occlusion,
            Fractal3DColoringMode.Fresnel, Fractal3DColoringMode.Steps
        })
        {
            state.ColoringMode = mode;
            BitmapSource bitmap = await renderer.RenderAsync(state, 440, 440, null, CancellationToken.None);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using FileStream stream = File.Create(Path.Combine(args[1], $"ifs-color-{(int)mode}.png"));
            encoder.Save(stream);
        }
        var randomSettings = new Ifs3DRandomizationSettings { MinimumTransforms = 7, MaximumTransforms = 7 };
        state = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Ifs3D);
        foreach (Ifs3DPlacementMode placement in Enum.GetValues<Ifs3DPlacementMode>())
        {
            randomSettings.PlacementMode = placement;
            state.IfsTransforms = Ifs3DRandomizer.Create(randomSettings, new Random(730 + (int)placement));
            BitmapSource bitmap = await renderer.RenderAsync(state, 440, 440, null, CancellationToken.None);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using FileStream stream = File.Create(Path.Combine(args[1], $"ifs-random-{placement}.png"));
            encoder.Save(stream);
        }
        Console.WriteLine("PASS (ifs-close): close views, styles, color sources and four generated IFS placements.");
    }

    private static async Task VerifyFractal3DAsync()
    {
        using var sandbox = DataSandbox.Create("fractal3d");
        using var renderer = new Fractal3DRenderer();

        var categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Fractal3DKind kind in Enum.GetValues<Fractal3DKind>())
        {
            Fractal3DDefinition definition = Fractal3DCatalog.GetDefinition(kind);
            Check(!string.IsNullOrWhiteSpace(definition.Title) &&
                  !string.IsNullOrWhiteSpace(definition.PanelTitle) &&
                  !string.IsNullOrWhiteSpace(definition.Description) &&
                  !string.IsNullOrWhiteSpace(definition.ExportPrefix),
                $"{kind}: the definition must describe the mode.");
            Check(categories.Add(definition.SaveCategory),
                $"{kind}: save category «{definition.SaveCategory}» is already used by another mode.");
            Check(Fractal3DCatalog.TryParseLaunchKey(Fractal3DCatalog.LaunchKey(kind), out Fractal3DKind parsed) && parsed == kind,
                $"{kind}: the launch key must round-trip.");

            IReadOnlyList<Fractal3DState> presets = Fractal3DCatalog.GetPresets(kind);
            Check(presets.Count > 0 && presets.All(preset => preset.Kind == kind),
                $"{kind}: presets must exist and belong to the mode.");
            Check(presets.Select(preset => preset.SaveName).Distinct(StringComparer.OrdinalIgnoreCase).Count() == presets.Count,
                $"{kind}: preset names must be unique — they are the points of interest of the save manager.");

            // Точка интереса, которая показывает только фон, бесполезна как превью режима.
            foreach (Fractal3DState preset in presets.Prepend(Fractal3DCatalog.CreateDefaultState(kind)))
            {
                byte[] pixels = await Fractal3DFrameAsync(renderer, preset);
                Check(HasFractal3DStructure(pixels),
                    $"{kind} «{preset.SaveName}»: the frame shows only the background.");
            }
        }

        await VerifyIfsPaletteAndShadingAsync(renderer);
        await VerifyIfs3DRandomizationAsync(renderer);

        Fractal3DState packing = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.ApollonianPacking);
        packing.Iterations = 1;
        byte[] firstGeneration = await Fractal3DFrameAsync(renderer, packing);
        packing.Iterations = 5;
        byte[] fifthGeneration = await Fractal3DFrameAsync(renderer, packing);
        Check(!firstGeneration.SequenceEqual(fifthGeneration),
            "Apollonian sphere generations must change the rendered geometry.");

        foreach (Fractal3DKind kind in new[] { Fractal3DKind.Vicsek, Fractal3DKind.CantorDust })
        {
            Fractal3DState cubes = Fractal3DCatalog.CreateDefaultState(kind);
            byte[] classic = await Fractal3DFrameAsync(renderer, cubes);
            cubes.Iterations = 2;
            byte[] shallow = await Fractal3DFrameAsync(renderer, cubes);
            Check(!classic.SequenceEqual(shallow),
                $"{kind}: recursion depth must change the geometry.");
            cubes.Iterations = 4;
            cubes.CubeThickness = 1.5;
            byte[] thick = await Fractal3DFrameAsync(renderer, cubes);
            Check(!classic.SequenceEqual(thick),
                $"{kind}: element thickness must change the geometry.");
        }

        // Один и тот же кадр должен считаться одинаково: превью сохранения и экспорт обязаны совпасть.
        Fractal3DState state = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Mandelbulb);
        byte[] reference = await Fractal3DFrameAsync(renderer, state);
        byte[] repeated = await Fractal3DFrameAsync(renderer, state);
        Check(reference.SequenceEqual(repeated), "The same state must produce the same frame.");

        // Параметры окраски, формы и камеры должны доходить до шейдера.
        Fractal3DState recolored = state.Clone();
        recolored.ColoringMode = Fractal3DColoringMode.Normal;
        byte[] recoloredFrame = await Fractal3DFrameAsync(renderer, recolored);
        Check(!reference.SequenceEqual(recoloredFrame), "The coloring mode must change the frame.");

        Fractal3DState reshaped = state.Clone();
        reshaped.Power = 3;
        byte[] reshapedFrame = await Fractal3DFrameAsync(renderer, reshaped);
        Check(!reference.SequenceEqual(reshapedFrame), "The power must change the frame.");

        Fractal3DState moved = state.Clone();
        moved.CameraYaw += 40;
        byte[] movedFrame = await Fractal3DFrameAsync(renderer, moved);
        Check(!reference.SequenceEqual(movedFrame), "The camera must change the frame.");

        Fractal3DState rolled = state.Clone();
        rolled.CameraRoll = 30;
        byte[] rolledFrame = await Fractal3DFrameAsync(renderer, rolled);
        Check(!reference.SequenceEqual(rolledFrame), "The roll of the camera must reach the shader.");

        // Отмена не должна оставлять недосчитанный кадр как готовый.
        using (var cancelled = new CancellationTokenSource())
        {
            await cancelled.CancelAsync();
            bool thrown = false;
            try
            {
                await renderer.RenderAsync(state, Fractal3DProbeWidth, Fractal3DProbeHeight, null, cancelled.Token);
            }
            catch (OperationCanceledException)
            {
                thrown = true;
            }
            Check(thrown, "A cancelled render must throw instead of returning a partial frame.");
        }

        // Живое превью переписывает один и тот же буфер: кадр от этого меняться не должен.
        byte[] reused = new byte[Fractal3DProbeWidth * Fractal3DProbeHeight * 4 + 1024];
        Fractal3DPixels live = await renderer.RenderPixelsAsync(
            state, Fractal3DProbeWidth, Fractal3DProbeHeight, reused, null, CancellationToken.None);
        Check(live.Completed && ReferenceEquals(live.Buffer, reused),
            "A buffer large enough must be reused instead of allocated again.");
        Check(live.Buffer.Take(reference.Length).SequenceEqual(reference),
            "A frame rendered into a ready buffer must match the ordinary frame.");

        // Движение камеры отменяет начатое уточнение постоянно, поэтому отменённый живой кадр
        // сообщает о себе значением, а не исключением: иначе отладчик останавливался бы на
        // каждом повороте мыши.
        using (var abandoned = new CancellationTokenSource())
        {
            await abandoned.CancelAsync();
            Fractal3DPixels dropped = await renderer.RenderPixelsAsync(
                state, Fractal3DProbeWidth, Fractal3DProbeHeight, reused, null, abandoned.Token);
            Check(!dropped.Completed, "A cancelled live frame must report itself instead of throwing.");
        }

        VerifyFractal3DCameraMath();
        VerifyFractal3DZoomGlide();
        VerifyFractal3DWindowZoom();
        await VerifyFractal3DPickerMarkerAsync(renderer);
        await VerifyFractal3DProbeAsync(renderer);
        await VerifyDistantFractal3DProbesAsync(renderer);
        await VerifyFractal3DDistanceShadingAsync(renderer);
        VerifyFractal3DPalettes();
        await VerifyFractal3DColoringAsync(renderer);
        VerifyFractal3DSaves();
        Console.WriteLine($"PASS (fractal3d): {Enum.GetValues<Fractal3DKind>().Length} modes, presets, " +
                          $"{Fractal3DPalettes.All.Count} palettes, {Enum.GetValues<Fractal3DShadingStyle>().Length} shaders, " +
                          "coloring sources, camera, navigation, smooth zoom, surface probe and saves.");
    }

    private static async Task VerifyIfs3DRandomizationAsync(Fractal3DRenderer renderer)
    {
        var settings = new Ifs3DRandomizationSettings
        {
            MinimumTransforms = 6, MaximumTransforms = 6,
            Families = [.. Enum.GetValues<Ifs3DTransformFamily>()]
        };
        var signatures = new HashSet<string>();
        foreach (Ifs3DPlacementMode placement in Enum.GetValues<Ifs3DPlacementMode>())
        foreach (Ifs3DProbabilityMode probability in Enum.GetValues<Ifs3DProbabilityMode>())
        {
            settings.PlacementMode = placement;
            settings.ProbabilityMode = probability;
            List<Ifs3DTransform> transforms = Ifs3DRandomizer.Create(settings, new Random(500 + (int)placement * 10 + (int)probability));
            Check(transforms.Count == 6 && Math.Abs(transforms.Sum(t => t.Probability) - 1) < 1e-12 &&
                  transforms.All(t => t.Probability > 0),
                $"IFS 3D randomizer {placement}/{probability}: count and weights must be valid.");
            foreach (Ifs3DTransform t in transforms)
            {
                double frobenius = Math.Sqrt(t.M11 * t.M11 + t.M12 * t.M12 + t.M13 * t.M13 +
                    t.M21 * t.M21 + t.M22 * t.M22 + t.M23 * t.M23 +
                    t.M31 * t.M31 + t.M32 * t.M32 + t.M33 * t.M33);
                Check(frobenius <= .880001 && double.IsFinite(t.Tx + t.Ty + t.Tz),
                    $"IFS 3D randomizer {placement}/{probability}: a map must remain contractive.");
            }
            signatures.Add(string.Join(";", transforms.Select(t => $"{t.Tx:F2},{t.Ty:F2},{t.Tz:F2}")));
            if (placement == Ifs3DPlacementMode.Bilateral)
                for (int i = 0; i < transforms.Count; i += 2)
                    Check(Math.Abs(transforms[i].Tx + transforms[i + 1].Tx) < 1e-6 &&
                          Math.Abs(transforms[i].Ty - transforms[i + 1].Ty) < 1e-6 &&
                          Math.Abs(transforms[i].Tz - transforms[i + 1].Tz) < 1e-6,
                        "Mirrored IFS maps must have paired anchors.");
        }
        Check(signatures.Count == 12, "3D placement settings must generate distinct maps.");

        settings.PlacementMode = Ifs3DPlacementMode.Spherical;
        settings.ProbabilityMode = Ifs3DProbabilityMode.VolumeWeighted;
        Ifs3DRandomizationSettingsStore.Save(settings);
        Ifs3DRandomizationSettings restored = Ifs3DRandomizationSettingsStore.Load();
        Check(restored.PlacementMode == settings.PlacementMode &&
              restored.ProbabilityMode == settings.ProbabilityMode &&
              restored.Families.Count == settings.Families.Count,
            "IFS 3D randomizer settings must persist independently.");

        Fractal3DState state = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Ifs3D);
        state.Iterations = 100_000;
        state.IfsTransforms = Ifs3DRandomizer.Create(settings, new Random(71));
        Check(HasFractal3DStructure(await Fractal3DFrameAsync(renderer, state)),
            "A generated 3D IFS must render a visible attractor.");
    }

    private static async Task VerifyIfsPaletteAndShadingAsync(Fractal3DRenderer renderer)
    {
        Fractal3DState state = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Ifs3D);
        state.Iterations = 100_000;
        byte[] classic = await Fractal3DFrameAsync(renderer, state);
        foreach (Fractal3DShadingStyle style in Enum.GetValues<Fractal3DShadingStyle>().Skip(1))
        {
            state.ShadingStyle = style;
            byte[] styled = await Fractal3DFrameAsync(renderer, state);
            Check(!classic.SequenceEqual(styled), $"IFS shader {style} must change the image.");
        }

        state.ShadingStyle = Fractal3DShadingStyle.Classic;
        state.ColoringMode = Fractal3DColoringMode.Height;
        state.Palette = Fractal3DPalettes.All[0].Clone();
        byte[] firstPalette = await Fractal3DFrameAsync(renderer, state);
        state.Palette = Fractal3DPalettes.All[1].Clone();
        byte[] secondPalette = await Fractal3DFrameAsync(renderer, state);
        Check(!firstPalette.SequenceEqual(secondPalette), "IFS palette selection must change the image.");

        var ifsManager = new Ifs3DPaletteManager();
        var otherManager = new Fractal3DPaletteManager();
        string name = "IFS isolated palette verification";
        ifsManager.Palettes.Add(Fractal3DPalette.FromPair(name, Colors.Red, Colors.Blue));
        ifsManager.SaveCustomPalettes();
        Check(new Ifs3DPaletteManager().Find(name) is not null &&
              new Fractal3DPaletteManager().Find(name) is null && otherManager.Find(name) is null,
            "IFS custom palettes must persist only in the IFS library.");
    }

    /// <summary>
    /// Библиотека палитр: встроенный набор, его неизменность для света и фона и круг
    /// «сохранить — прочитать» пользовательской палитры через файл.
    /// </summary>
    private static void VerifyFractal3DPalettes()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Fractal3DPalette palette in Fractal3DPalettes.All)
        {
            Check(names.Add(palette.Name), $"Palette «{palette.Name}» is listed twice.");
            Check(palette.IsBuiltIn, $"«{palette.Name}»: a listed palette must be marked built-in.");
            Check(palette.Colors.Count is > 1 and <= Fractal3DPalette.MaxColors,
                $"«{palette.Name}»: {palette.Colors.Count} colors do not fit the shader.");

            // Пользователь просил, чтобы встроенные раскраски оставляли свет и фон как были.
            Check(!palette.OverridesEnvironment &&
                  palette.BackgroundTop == Fractal3DEnvironment.BackgroundTop &&
                  palette.BackgroundBottom == Fractal3DEnvironment.BackgroundBottom &&
                  palette.LightColor == Fractal3DEnvironment.LightColor,
                $"«{palette.Name}»: a built-in palette must leave the light and the sky alone.");
        }
        Check(Fractal3DPalettes.All[0].Name == Fractal3DPalettes.ClassicName,
            "The palette of the default view must come first in the library.");

        // Копия встроенной палитры правится свободно и не задевает библиотеку.
        Fractal3DPalette classic = Fractal3DPalettes.Classic();
        classic.Colors.Add(Colors.Red);
        Check(Fractal3DPalettes.Get(Fractal3DPalettes.ClassicName).Colors.Count == 2 && !classic.IsBuiltIn,
            "A palette taken from the library must be an editable copy.");

        var manager = new Fractal3DPaletteManager();
        Check(manager.Palettes.Count == Fractal3DPalettes.All.Count && manager.Find("Виридис") is not null,
            "A fresh library must hold exactly the built-in palettes.");
        var custom = new Fractal3DPalette
        {
            Name = "Проверочная палитра",
            Colors = [Colors.Black, Color.FromRgb(200, 30, 90), Colors.White],
            IsGradient = false,
            Gamma = 1.75,
            OverridesEnvironment = true,
            LightColor = Color.FromRgb(255, 200, 120)
        };
        manager.Palettes.Add(custom);
        manager.SaveCustomPalettes();

        var reloaded = new Fractal3DPaletteManager();
        Fractal3DPalette? restored = reloaded.Find(custom.Name);
        Check(restored is not null && !restored.IsBuiltIn && restored.Colors.SequenceEqual(custom.Colors) &&
              restored.IsGradient == custom.IsGradient && restored.Gamma.Equals(custom.Gamma) &&
              restored.OverridesEnvironment && restored.LightColor == custom.LightColor,
            "A custom palette must survive the round trip through the palette file.");
        Check(reloaded.Palettes.Count(palette => palette.IsBuiltIn) == Fractal3DPalettes.All.Count,
            "The built-in palettes must not be written to the file and read back twice.");
    }

    /// <summary>
    /// Палитры, источники цвета и встроенные шейдеры должны доходить до кадра, а сохранение без
    /// палитры — по-прежнему рисоваться старыми двумя цветами.
    /// </summary>
    private static async Task VerifyFractal3DColoringAsync(Fractal3DRenderer renderer)
    {
        Fractal3DState state = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Mandelbulb);
        byte[] reference = await Fractal3DFrameAsync(renderer, state);

        Fractal3DState repainted = state.Clone();
        repainted.Palette = Fractal3DPalettes.Get("Виридис");
        byte[] repaintedFrame = await Fractal3DFrameAsync(renderer, repainted);
        Check(!reference.SequenceEqual(repaintedFrame), "The palette must change the frame.");

        Fractal3DState banded = repainted.Clone();
        banded.Palette!.IsGradient = false;
        byte[] bandedFrame = await Fractal3DFrameAsync(renderer, banded);
        Check(!repaintedFrame.SequenceEqual(bandedFrame),
            "Bands instead of a gradient must change the frame.");

        Fractal3DState cycled = repainted.Clone();
        cycled.ColorRepeat = Fractal3DColorRepeat.Cycle;
        byte[] cycledFrame = await Fractal3DFrameAsync(renderer, cycled);
        Check(!repaintedFrame.SequenceEqual(cycledFrame),
            "The repeat mode must change the frame.");

        Fractal3DState lit = state.Clone();
        lit.LightColor = Color.FromRgb(255, 120, 40);
        byte[] litFrame = await Fractal3DFrameAsync(renderer, lit);
        Check(!reference.SequenceEqual(litFrame),
            "The colour of the light must change the frame.");

        Fractal3DState skyLit = state.Clone();
        skyLit.SkyLightMix = 1;
        byte[] skyLitFrame = await Fractal3DFrameAsync(renderer, skyLit);
        Check(!reference.SequenceEqual(skyLitFrame),
            "Letting the sky tint the ambient light must change the frame.");

        // Сохранение старше палитр: цвета лежат в ColorA и ColorB, и кадр обязан совпасть с тем,
        // что даёт собранная из них палитра, иначе старые файлы поменяли бы вид.
        Fractal3DState legacy = state.Clone();
        legacy.Palette = null;
        legacy.ColorA = Color.FromRgb(26, 58, 122);
        legacy.ColorB = Color.FromRgb(255, 186, 92);
        byte[] legacyFrame = await Fractal3DFrameAsync(renderer, legacy);
        Check(reference.SequenceEqual(legacyFrame),
            "A save made before palettes must look exactly as it did.");

        var frames = new List<byte[]>();
        foreach (Fractal3DColoringMode mode in Enum.GetValues<Fractal3DColoringMode>())
        {
            Fractal3DState coloured = state.Clone();
            coloured.ColoringMode = mode;
            coloured.Palette = Fractal3DPalettes.Get("Спектр");
            byte[] frame = await Fractal3DFrameAsync(renderer, coloured);
            Check(HasFractal3DStructure(frame),
                $"{Fractal3DCatalog.ColoringModeName(mode)}: the coloring source shows only the background.");
            Check(frames.All(other => !other.SequenceEqual(frame)),
                $"{Fractal3DCatalog.ColoringModeName(mode)}: the coloring source repeats another one.");
            frames.Add(frame);
        }

        frames.Clear();
        foreach (Fractal3DShadingStyle style in Enum.GetValues<Fractal3DShadingStyle>())
        {
            Fractal3DState shaded = state.Clone();
            shaded.ShadingStyle = style;
            byte[] frame = await Fractal3DFrameAsync(renderer, shaded);
            Check(HasFractal3DStructure(frame),
                $"{Fractal3DCatalog.ShadingStyleName(style)}: the shader shows only the background.");
            Check(frames.All(other => !other.SequenceEqual(frame)),
                $"{Fractal3DCatalog.ShadingStyleName(style)}: the shader repeats another one.");
            frames.Add(frame);
        }

        // Плотность проходит фигуру насквозь, но зонду по-прежнему нужно первое попадание.
        Fractal3DState pierced = state.Clone();
        pierced.ShadingStyle = Fractal3DShadingStyle.Density;
        double distance = await ProbeFractal3DAsync(renderer, pierced, Fractal3DRayWidth / 2.0, Fractal3DRayHeight / 2.0);
        Check(double.IsFinite(distance) && distance > 0,
            "The surface probe must keep working under the shader that pierces the surface.");
    }

    /// <summary>
    /// Камера в духе CAD: кватернионный поворот без упора в полюс, трекбол вокруг схваченной
    /// точки, поворот взгляда на месте, крен и выравнивание горизонта. Во всех жестах картинка
    /// должна идти за мышью. Плюс луч через пиксель, по которому зонд откладывает расстояние.
    /// </summary>
    private static void VerifyFractal3DCameraMath()
    {
        const double fov = 55, width = Fractal3DRayWidth, height = Fractal3DRayHeight;
        static bool Near(Vector3 a, Vector3 b, float tolerance = 1e-4f) => (a - b).Length() < tolerance;

        // Без крена поворот — ровно прежняя орбитальная камера, иначе старые сохранения сменили бы вид.
        foreach ((double yaw, double pitch) in new[] { (35.0, 18.0), (-120.0, -60.0), (200.0, 85.0) })
        {
            var pose = new Fractal3DPose(Fractal3DCamera.Orientation(yaw, pitch, 0), 2, Vector3.Zero);
            Vector3 forward = -Fractal3DCamera.Direction(yaw, pitch);
            Vector3 side = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
            Check(Near(pose.Forward, forward) && Near(pose.Right, side) &&
                  Near(pose.Up, Vector3.Cross(side, forward)),
                $"Yaw {yaw}, pitch {pitch}: a camera without roll must match the former orbit camera.");
        }

        // Углы и поворот переводятся друг в друга, в том числе вверх ногами и у самого полюса.
        foreach ((double yaw, double pitch, double roll) in new[]
                 {
                     (35.0, 18.0, 0.0), (35.0, 18.0, 40.0), (-150.0, -70.0, 170.0), (10.0, 89.5, -30.0),
                     (0.0, 90.0, 0.0), (80.0, -90.0, 0.0)
                 })
        {
            Quaternion orientation = Fractal3DCamera.Orientation(yaw, pitch, roll);
            (double y, double p, double r) = Fractal3DCamera.Angles(orientation);
            var original = new Fractal3DPose(orientation, 1, Vector3.Zero);
            var restored = new Fractal3DPose(Fractal3DCamera.Orientation(y, p, r), 1, Vector3.Zero);
            Check(Near(original.Forward, restored.Forward) && Near(original.Up, restored.Up) &&
                  Near(original.Right, restored.Right),
                $"Angles ({yaw}; {pitch}; {roll}) must round-trip through the orientation.");
        }

        Fractal3DState state = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Mandelbulb);
        state.CameraRoll = 25;
        Fractal3DPose start = Fractal3DCamera.Pose(state);
        Fractal3DState written = state.Clone();
        Fractal3DCamera.Apply(start, written);
        Check(Near(Fractal3DCamera.Pose(written).Up, start.Up) &&
              Near(Fractal3DCamera.Pose(written).Position, start.Position),
            "Writing a pose into a state must keep it.");

        (double X, double Y) Screen(Fractal3DPose pose, Vector3 point) =>
            Fractal3DCamera.Project(pose, fov, point, width, height);

        // Трекбол: опорная точка стоит на месте, и на экране, и по расстоянию; ближняя к
        // зрителю сторона идёт за мышью.
        Vector3 pivot = start.Target + start.Right * 0.3f + start.Up * 0.2f;
        Vector3 near = pivot - start.Forward * 0.5f;
        (double pivotX, double pivotY) = Screen(start, pivot);
        (double nearX, double nearY) = Screen(start, near);
        Fractal3DPose right = Fractal3DCamera.Orbit(start, pivot, 12, 0);
        Fractal3DPose down = Fractal3DCamera.Orbit(start, pivot, 0, 12);
        (double rightPivotX, double rightPivotY) = Screen(right, pivot);
        Check(Math.Abs(rightPivotX - pivotX) < 0.05 && Math.Abs(rightPivotY - pivotY) < 0.05 &&
              Math.Abs((right.Position - pivot).Length() - (start.Position - pivot).Length()) < 1e-4f,
            "Orbiting must keep the grabbed point in place.");
        Check(Screen(right, near).X > nearX + 1 && Screen(down, near).Y > nearY + 1,
            "Orbiting must drag the near side of the fractal along with the mouse.");

        // Упора в полюс нет: полный круг по вертикали возвращает камеру туда же.
        Fractal3DPose circled = start;
        for (int step = 0; step < 36; step++) circled = Fractal3DCamera.Orbit(circled, pivot, 0, 10);
        Fractal3DPose flipped = Fractal3DCamera.Orbit(start, pivot, 0, 180);
        Check(Near(circled.Position, start.Position, 1e-3f) && Near(circled.Up, start.Up, 1e-3f),
            "A full vertical circle must bring the camera back — without stopping at the pole.");
        Check(Vector3.Dot(flipped.Up, start.Up) < -0.9f, "Orbiting over the pole must turn the camera upside down.");

        // Поворот взгляда на месте: камера стоит, картинка идёт за мышью.
        Vector3 ahead = start.Position + start.Forward * 3;
        (double aheadX, double aheadY) = Screen(start, ahead);
        Fractal3DPose looked = Fractal3DCamera.Look(start, 8, 6);
        Check(Near(looked.Position, start.Position) && looked.Distance.Equals(start.Distance),
            "Looking around must keep the camera in place.");
        Check(Screen(looked, ahead).X > aheadX + 1 && Screen(looked, ahead).Y > aheadY + 1,
            "Looking around must drag the picture along with the mouse.");

        // Крен: взгляд тот же, точка над центром кадра уходит вправо — картинка по часовой стрелке.
        Vector3 above = start.Position + start.Forward * 2 + start.Up * 0.5f;
        Fractal3DPose rolled = Fractal3DCamera.Roll(start, 20);
        Check(Near(rolled.Position, start.Position) && Near(rolled.Forward, start.Forward),
            "Roll must keep the camera and the view direction.");
        Check(Screen(rolled, above).X > Screen(start, above).X + 1, "A positive roll must turn the picture clockwise.");
        Check(Math.Abs(Fractal3DCamera.Angles(rolled.Orientation).Roll - (state.CameraRoll + 20)) < 1e-3,
            "Roll must add up with the roll of the state.");

        // Выравнивание горизонта убирает крен и не трогает взгляд, даже у перевёрнутой камеры.
        foreach (Fractal3DPose tilted in new[] { rolled, flipped })
        {
            Quaternion level = Fractal3DCamera.Level(tilted.Orientation);
            var levelled = new Fractal3DPose(level, tilted.Distance, tilted.Target);
            Check(Near(levelled.Forward, tilted.Forward, 1e-3f) && Math.Abs(levelled.Right.Y) < 1e-3f &&
                  levelled.Up.Y > 0, "Levelling must keep the view and put the horizon straight.");
        }

        // Разворот к точке и луч через пиксель — взаимно обратные проекции.
        Vector3 ray = Fractal3DCamera.PixelRay(start, fov, width * 0.3, height * 0.7, width, height);
        (double rayX, double rayY) = Screen(start, start.Position + ray * 2);
        Check(Math.Abs(rayX - width * 0.3) < 0.05 && Math.Abs(rayY - height * 0.7) < 0.05,
            "Projecting a point of a pixel ray must land on that pixel.");
        var turned = new Fractal3DPose(Fractal3DCamera.TurnToward(start.Orientation, ray), 1, Vector3.Zero);
        Check(Near(turned.Forward, ray), "Turning toward a direction must look along it.");

        Fractal3DCameraBasis basis = Fractal3DCamera.Build(state);
        Vector3 centre = Fractal3DCamera.PixelRay(state, width / 2, height / 2, width, height);
        Check(Near(centre, basis.Forward, 1e-5f) && Near(basis.Up, start.Up),
            "The ray through the centre of the frame must be the view direction, rolled as the state says.");

        (double dirYaw, double dirPitch) = Fractal3DCamera.Angles(Fractal3DCamera.Direction(35, 18));
        Check(Math.Abs(dirYaw - 35) < 1e-4 && Math.Abs(dirPitch - 18) < 1e-4,
            "Angles of a direction must round-trip.");
    }

    /// <summary>
    /// Доводка колеса: щелчок задаёт цель, а камера идёт к ней несколько кадров. Главное —
    /// прийти ровно в цель и ни разу её не проскочить, иначе зум «дышал» бы на каждом щелчке.
    /// </summary>
    private static void VerifyFractal3DZoomGlide()
    {
        var glide = new Fractal3DZoomGlide();
        var orbit = new Fractal3DPose(Quaternion.Identity, 4, new Vector3(1, 0, -1));
        Check(!glide.IsActive(orbit.Distance), "A fresh glide has nothing to travel.");

        var destination = new Vector3(1.5f, 0.25f, -1.25f);
        glide.Aim(1, destination - orbit.Target);
        Check(glide.PlannedDistance(orbit.Distance).Equals(1.0) &&
              glide.PlannedTarget(orbit.Target) == destination,
            "The glide must know where the wheel aimed it.");
        Check(glide.IsActive(orbit.Distance), "An aimed glide must have something to travel.");

        double previous = orbit.Distance;
        int steps = 0;
        while (glide.IsActive(orbit.Distance) && steps < 600)
        {
            orbit = glide.Advance(orbit, 1.0 / 60);
            Check(orbit.Distance < previous && orbit.Distance >= 1,
                $"The glide must approach the aim without overshooting it ({orbit.Distance:G6}).");
            previous = orbit.Distance;
            steps++;
        }
        Check(steps is > 3 and < 120,
            $"The glide must take a few frames — neither a jump nor a crawl ({steps} frames).");
        orbit = glide.Finish(orbit);
        // Расстояние приходит точно — его показывает поле камеры; точка наблюдения хранится
        // приращением, поэтому у неё остаётся обычная погрешность float.
        Check(orbit.Distance.Equals(1d) && (orbit.Target - destination).Length() < 1e-6f &&
              !glide.HasRemainder,
            "The last hair of the way must land the camera exactly where the wheel aimed it.");

        // Щелчки складываются, а огромный шаг времени не выносит камеру за цель.
        glide.Clear();
        glide.Push(new Vector3(1, 0, 0));
        glide.Push(new Vector3(0, 2, 0));
        Check(glide.Shift == new Vector3(1, 2, 0), "Wheel notches must add up while the camera travels.");
        Fractal3DPose jumped = glide.Advance(orbit, 10);
        Check((jumped.Target - (orbit.Target + new Vector3(1, 2, 0))).Length() < 1e-5f &&
              !glide.IsActive(jumped.Distance),
            "A long frame must land on the aim instead of flying past it.");

        // Упор в минимальное расстояние не оставляет вечного остатка.
        glide.Clear();
        glide.Aim(Fractal3DCamera.MinDistance / 1000, Vector3.Zero);
        Fractal3DPose squeezed = jumped with { Distance = 1 };
        for (int index = 0; index < 600 && glide.IsActive(squeezed.Distance); index++)
        {
            squeezed = glide.Advance(squeezed, 1.0 / 60);
        }
        squeezed = glide.Finish(squeezed);
        Check(squeezed.Distance.Equals(Fractal3DCamera.MinDistance) && !glide.HasRemainder,
            "Hitting the closest distance must end the travel instead of leaving a remainder.");
    }

    /// <summary>
    /// Зонд поверхности: от него зависят перелёт по двойному щелчку и шаг движения колесом и
    /// клавишами, поэтому проверяются и геометрия луча, и масштаб ответа.
    /// </summary>
    private static async Task VerifyPickerWindowFrameAsync()
    {
        Fractal3DState source = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Juliabulb);
        var window = new FractalExplorerWPF.Views.Fractal3DConstantPickerWindow(source)
        {
            Left = -10000,
            Top = -10000,
            ShowInTaskbar = false,
            ShowActivated = false
        };
        try
        {
            window.Show();
            var image = (System.Windows.Controls.Image)window.FindName("PreviewImage");
            for (int attempt = 0; attempt < 40 && image.Source is null; attempt++)
                await Task.Delay(200);
            var status = (System.Windows.Controls.TextBlock)window.FindName("StatusText");
            Check(image.Source is BitmapSource,
                $"The picker must render its first frame without an existing Image.Source ({status.Text}).");
            var bitmap = (BitmapSource)image.Source!;
            int stride = bitmap.PixelWidth * 4;
            byte[] pixels = new byte[stride * bitmap.PixelHeight];
            bitmap.CopyPixels(pixels, stride, 0);
            int nonBlack = 0;
            int samples = 0;
            for (int y = 0; y < bitmap.PixelHeight; y += 16)
            for (int x = 0; x < bitmap.PixelWidth; x += 16)
            {
                int offset = y * stride + x * 4;
                if (pixels[offset] + pixels[offset + 1] + pixels[offset + 2] > 20) nonBlack++;
                samples++;
            }
            Check(nonBlack > samples / 10, "The first picker frame must contain the Mandelbulb and its background.");

            // Движение не должно ждать отпускания мыши: следующий черновой кадр появляется,
            // пока окно находится в режиме перетаскивания.
            var draggingField = window.GetType().GetField("_dragging",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var rotate = window.GetType().GetMethod("RotateBy",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            draggingField.SetValue(window, true);
            rotate.Invoke(window, [new System.Windows.Vector(65, 18)]);
            for (int attempt = 0; attempt < 40 &&
                 (image.Source is not BitmapSource current || current.PixelWidth >= bitmap.PixelWidth); attempt++)
                await Task.Delay(100);
            Check(image.Source is BitmapSource draft && draft.PixelWidth < bitmap.PixelWidth,
                "Dragging must display a draft frame before the mouse button is released.");
            var live = (BitmapSource)image.Source!;
            byte[] firstDragFrame = new byte[live.PixelWidth * live.PixelHeight * 4];
            live.CopyPixels(firstDragFrame, live.PixelWidth * 4, 0);
            rotate.Invoke(window, [new System.Windows.Vector(52, -26)]);
            bool changedWhileDragging = false;
            for (int attempt = 0; attempt < 40 && !changedWhileDragging; attempt++)
            {
                await Task.Delay(100);
                if (image.Source is not BitmapSource next) continue;
                byte[] nextPixels = new byte[next.PixelWidth * next.PixelHeight * 4];
                next.CopyPixels(nextPixels, next.PixelWidth * 4, 0);
                changedWhileDragging = !nextPixels.AsSpan().SequenceEqual(firstDragFrame);
            }
            Check(changedWhileDragging, "A second drag must update the visible frame before release.");
        }
        finally { window.Close(); }
    }

    private static async Task VerifyFractal3DPickerMarkerAsync(Fractal3DRenderer renderer)
    {
        Fractal3DState state = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Mandelbulb);
        state.CameraDistance = 4.5;
        state.PickerMarker = new Vector4(Fractal3DCamera.Direction(state.CameraYaw, state.CameraPitch) * 2, 0.2f);
        byte[] frame = await Fractal3DFrameAsync(renderer, state);
        int centre = ((Fractal3DProbeHeight / 2) * Fractal3DProbeWidth + Fractal3DProbeWidth / 2) * 4;
        Check(frame[centre + 1] > frame[centre + 2] + 35 &&
              frame[centre + 1] > frame[centre] + 35,
            "The C picker must render a green sphere in front of the Mandelbulb.");

        double withMarker = await renderer.ProbeDistanceAsync(state,
            Fractal3DProbeWidth / 2.0, Fractal3DProbeHeight / 2.0,
            Fractal3DProbeWidth, Fractal3DProbeHeight, CancellationToken.None);
        state.PickerMarker = Vector4.Zero;
        double withoutMarker = await renderer.ProbeDistanceAsync(state,
            Fractal3DProbeWidth / 2.0, Fractal3DProbeHeight / 2.0,
            Fractal3DProbeWidth, Fractal3DProbeHeight, CancellationToken.None);
        Check(double.IsFinite(withMarker) && Math.Abs(withMarker - withoutMarker) < 1e-4,
            $"The surface probe must ignore the C picker marker ({withMarker:G9} vs {withoutMarker:G9}).");
    }

    private static async Task VerifyFractal3DProbeAsync(Fractal3DRenderer renderer)
    {
        Fractal3DState state = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Mandelbulb);
        double centre = await ProbeFractal3DAsync(renderer, state, Fractal3DRayWidth / 2.0, Fractal3DRayHeight / 2.0);
        Check(double.IsFinite(centre) && centre > 0 && centre < state.CameraDistance,
            "The probe must find the surface between the camera and the target.");

        Fractal3DState distant = state.Clone();
        distant.CameraDistance = 40;
        distant.MaxDistance = 1;
        double distantHit = await ProbeFractal3DAsync(
            renderer, distant, Fractal3DRayWidth / 2.0, Fractal3DRayHeight / 2.0);
        Check(double.IsFinite(distantHit) &&
              Math.Abs((distant.CameraDistance - distantHit) - (state.CameraDistance - centre)) < 0.4,
            "Moving the camera far beyond the trace limit must still hit the front surface.");

        double corner = await ProbeFractal3DAsync(renderer, state, 0.5, 0.5);
        Check(double.IsNaN(corner), "A ray that misses the fractal must report no surface.");

        // Шаг вдоль взгляда обязан ровно на столько же сократить измеренное расстояние: на этом
        // держится «игровое» приближение, которое не проскакивает сквозь поверхность.
        double step = centre * 0.25;
        Vector3 forward = -Fractal3DCamera.Direction(state.CameraYaw, state.CameraPitch) * (float)step;
        Fractal3DState closer = state.Clone();
        closer.TargetX += forward.X;
        closer.TargetY += forward.Y;
        closer.TargetZ += forward.Z;
        double after = await ProbeFractal3DAsync(renderer, closer, Fractal3DRayWidth / 2.0, Fractal3DRayHeight / 2.0);
        Check(double.IsFinite(after) && Math.Abs(after - (centre - step)) < 0.03 * centre,
            "Moving along the view must shorten the measured distance by the same amount.");

        // Перелёт к точке под курсором: после него она должна оказаться ровно в центре кадра.
        double pixelX = Fractal3DRayWidth * 0.42;
        double pixelY = Fractal3DRayHeight * 0.42;
        double oblique = await ProbeFractal3DAsync(renderer, state, pixelX, pixelY);
        Check(double.IsFinite(oblique), "The probe must find the surface next to the centre of the frame.");

        Vector3 position = Fractal3DCamera.Position(state);
        Vector3 hit = position + Fractal3DCamera.PixelRay(
            state, pixelX, pixelY, Fractal3DRayWidth, Fractal3DRayHeight) * (float)oblique;
        (double yaw, double pitch) = Fractal3DCamera.Angles(position - hit);
        Fractal3DState focused = state.Clone();
        focused.CameraYaw = yaw;
        focused.CameraPitch = pitch;
        focused.CameraDistance = oblique;
        focused.TargetX = hit.X;
        focused.TargetY = hit.Y;
        focused.TargetZ = hit.Z;
        double refocused = await ProbeFractal3DAsync(
            renderer, focused, Fractal3DRayWidth / 2.0, Fractal3DRayHeight / 2.0);
        Check(double.IsFinite(refocused) && Math.Abs(refocused - oblique) < 0.03 * oblique,
            "After the flight the point under the cursor must be in the centre of the frame.");
    }

    private static Task<double> ProbeFractal3DAsync(
        Fractal3DRenderer renderer, Fractal3DState state, double pixelX, double pixelY) =>
        renderer.ProbeDistanceAsync(
            state, pixelX, pixelY, Fractal3DRayWidth, Fractal3DRayHeight, CancellationToken.None);

    private static async Task VerifyDistantFractal3DProbesAsync(Fractal3DRenderer renderer)
    {
        foreach (Fractal3DKind kind in Enum.GetValues<Fractal3DKind>().Where(k => k != Fractal3DKind.Mandelbulb))
        {
            Fractal3DState nearby = Fractal3DCatalog.CreateDefaultState(kind);
            Fractal3DState state = nearby.Clone();
            state.CameraDistance = kind == Fractal3DKind.Mandelbox ? 240 : 48;
            state.MaxDistance = 1;

            bool found = false;
            double distantCentre = double.NaN;
            foreach (double y in new[] { 0.49, 0.5, 0.51 })
            {
                foreach (double x in new[] { 0.49, 0.5, 0.51 })
                {
                    double hit = await ProbeFractal3DAsync(
                        renderer, state, Fractal3DRayWidth * x, Fractal3DRayHeight * y);
                    if (x == 0.5 && y == 0.5) distantCentre = hit;
                    found |= double.IsFinite(hit) && hit > 0 && hit < state.CameraDistance;
                }
            }

            Check(found, $"{kind}: moving beyond the trace limit must still reveal the fractal.");
            double nearbyCentre = await ProbeFractal3DAsync(
                renderer, nearby, Fractal3DRayWidth / 2.0, Fractal3DRayHeight / 2.0);
            if (double.IsFinite(nearbyCentre))
            {
                double tolerance = kind == Fractal3DKind.Mandelbox ? 2.0 : 0.5;
                Check(double.IsFinite(distantCentre) &&
                      Math.Abs((state.CameraDistance - distantCentre) -
                               (nearby.CameraDistance - nearbyCentre)) < tolerance,
                    $"{kind}: the distant ray must hit the same front surface as the nearby ray.");
            }
        }
    }

    /// <summary>
    /// Отъезд камеры не должен менять цвет фигуры скачком. Окраска по глубине законно меняется
    /// с расстоянием, поэтому сравниваются кадры, разнесённые на полпроцента: плавное изменение
    /// даёт в такой паре единицы, разрыв — сотни. Раньше у губки, тетраэдра и Мандельбокса
    /// отсчёт глубины переключался на трёх радиусах описанной сферы, и цвет прыгал; эти
    /// расстояния проверяются отдельно.
    /// </summary>
    private static async Task VerifyFractal3DDistanceShadingAsync(Fractal3DRenderer renderer)
    {
        foreach (Fractal3DKind kind in Enum.GetValues<Fractal3DKind>())
        {
            foreach ((Fractal3DColoringMode coloring, Fractal3DShadingStyle style) in new[]
                     {
                         (Fractal3DColoringMode.Material, Fractal3DShadingStyle.Classic),
                         (Fractal3DColoringMode.Depth, Fractal3DShadingStyle.Classic),
                         (Fractal3DColoringMode.Material, Fractal3DShadingStyle.Density)
                     })
            {
                Fractal3DState state = Fractal3DCatalog.CreateDefaultState(kind);
                state.ColoringMode = coloring;
                state.ShadingStyle = style;
                state.EffectStrength = 1;
                double start = state.CameraDistance;

                // 3·√3 — прежний порог губки и тетраэдра, 3·16.93 — Мандельбокса с масштабом 2.
                IEnumerable<double> distances = Enumerable.Range(0, 13)
                    .Select(step => start * Math.Pow(4, step / 12.0))
                    .Append(3 * Math.Sqrt(3))
                    .Append(3 * 16.93);

                double worst = 0;
                foreach (double distance in distances)
                {
                    state.CameraDistance = distance / Math.Sqrt(ShadingPairRatio);
                    double[] near = CentreColour(await Fractal3DFrameAsync(renderer, state));
                    state.CameraDistance = distance * Math.Sqrt(ShadingPairRatio);
                    double[] far = CentreColour(await Fractal3DFrameAsync(renderer, state));
                    worst = Math.Max(worst, near.Zip(far, (x, y) => Math.Abs(x - y)).Max());
                }
                Check(worst < ShadingJumpTolerance,
                    $"{kind} {coloring}/{style}: moving the camera away must not change the colour abruptly " +
                    $"(centre colour jumped by {worst:F1}).");
            }
        }
    }

    private const double ShadingPairRatio = 1.005;
    // На полупроцентном шаге плавная окраска меняется до 14 единиц, прежний разрыв давал 119–131.
    private const double ShadingJumpTolerance = 20;

    /// <summary>Средний цвет центрального пятна кадра по каналам B, G, R.</summary>
    private static double[] CentreColour(byte[] pixels)
    {
        int stride = Fractal3DProbeWidth * 4;
        var sum = new double[3];
        int count = 0;
        for (int row = Fractal3DProbeHeight / 2 - 3; row < Fractal3DProbeHeight / 2 + 3; row++)
        {
            for (int column = Fractal3DProbeWidth / 2 - 3; column < Fractal3DProbeWidth / 2 + 3; column++)
            {
                for (int channel = 0; channel < 3; channel++) sum[channel] += pixels[row * stride + column * 4 + channel];
                count++;
            }
        }
        return sum.Select(value => value / count).ToArray();
    }

    private static void VerifyFractal3DSaves()
    {
        foreach (Fractal3DKind kind in Enum.GetValues<Fractal3DKind>())
        {
            var store = new Fractal3DSaveStore(kind);
            Fractal3DState original = Fractal3DCatalog.GetPresets(kind)[^1].Clone();
            original.SaveName = $"Проверка {kind}";
            original.Timestamp = new DateTime(2026, 3, 4, 5, 6, 7);
            original.ColorA = Color.FromRgb(11, 22, 33);
            original.Palette = Fractal3DPalettes.Get("Закат");
            original.Palette.Gamma = 1.4;
            original.ColorRepeat = Fractal3DColorRepeat.Cycle;
            original.ShadingStyle = Fractal3DShadingStyle.Toon;
            original.EffectStrength = 1.75;
            original.LightColor = Color.FromRgb(240, 210, 160);
            original.SkyLightMix = 0.5;
            original.CameraRoll = 30;
            original.MotionQuality = Fractal3DMotionQuality.Draft;
            original.MotionResolution = Fractal3DMotionResolution.Full;
            original.RotationInertia = false;
            original.AutoRotate = true;
            original.AutoRotateSpeed = -27.5;
            store.Save(original);

            Fractal3DState loaded = store.Load().Single(item => item.SaveName == original.SaveName);
            Check(loaded.Kind == original.Kind && loaded.Timestamp == original.Timestamp &&
                  loaded.Iterations == original.Iterations && loaded.Power.Equals(original.Power) &&
                  loaded.CameraYaw.Equals(original.CameraYaw) && loaded.CameraPitch.Equals(original.CameraPitch) &&
                  loaded.CameraDistance.Equals(original.CameraDistance) &&
                  loaded.TargetX.Equals(original.TargetX) && loaded.FieldOfView.Equals(original.FieldOfView) &&
                  loaded.MaxSteps == original.MaxSteps && loaded.Detail.Equals(original.Detail) &&
                  loaded.ColoringMode == original.ColoringMode && loaded.ColorA == original.ColorA &&
                  loaded.SoftShadows == original.SoftShadows && loaded.AmbientOcclusion == original.AmbientOcclusion &&
                  loaded.MotionQuality == original.MotionQuality &&
                  loaded.MotionResolution == original.MotionResolution &&
                  loaded.RotationInertia == original.RotationInertia &&
                  loaded.AutoRotate == original.AutoRotate && loaded.AutoRotateSpeed.Equals(original.AutoRotateSpeed) &&
                  loaded.ShadingStyle == original.ShadingStyle &&
                  loaded.EffectStrength.Equals(original.EffectStrength) &&
                  loaded.ColorRepeat == original.ColorRepeat && loaded.LightColor == original.LightColor &&
                  loaded.SkyLightMix.Equals(original.SkyLightMix) &&
                  loaded.CameraRoll.Equals(original.CameraRoll) &&
                  loaded.Palette is not null && loaded.Palette.Name == original.Palette.Name &&
                  loaded.Palette.Gamma.Equals(original.Palette.Gamma) &&
                  loaded.Palette.Colors.SequenceEqual(original.Palette.Colors),
                $"{kind}: the save must restore every parameter of the state.");

            // Файл прежнего формата: без крена, зато с полями прежней навигации. Он обязан
            // читаться без ошибок и открываться без крена — так, как он и выглядел.
            string file = Directory.GetFiles(AppPaths.GetSavesDirectory(Fractal3DCatalog.GetDefinition(kind).SaveCategory), "*.json")
                .Single(path => JsonNode.Parse(File.ReadAllText(path))?["SaveName"]?.GetValue<string>() == original.SaveName);
            var legacy = (JsonObject)JsonNode.Parse(File.ReadAllText(file))!;
            legacy.Remove(nameof(Fractal3DState.CameraRoll));
            legacy["RotationAnchor"] = 1;
            legacy["ZoomToCursor"] = false;
            File.WriteAllText(file, legacy.ToJsonString());
            Fractal3DState old = store.Load().Single(item => item.SaveName == original.SaveName);
            Check(old.CameraRoll == 0 && old.CameraYaw.Equals(original.CameraYaw) &&
                  old.CameraDistance.Equals(original.CameraDistance),
                $"{kind}: a save made before the roll must load without it and keep the rest of the camera.");
        }
    }

    private static async Task<byte[]> Fractal3DFrameAsync(Fractal3DRenderer renderer, Fractal3DState state)
    {
        BitmapSource bitmap = await renderer.RenderAsync(
            state, Fractal3DProbeWidth, Fractal3DProbeHeight, null, CancellationToken.None);
        int stride = Fractal3DProbeWidth * 4;
        byte[] pixels = new byte[stride * Fractal3DProbeHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        return pixels;
    }

    /// <summary>
    /// Фон кадра — вертикальный градиент, поэтому его строки почти однотонны по горизонтали.
    /// Считаем, что фрактал попал в кадр, если заметная часть строк меняется вдоль себя.
    /// </summary>
    private static bool HasFractal3DStructure(byte[] pixels)
    {
        int stride = Fractal3DProbeWidth * 4;
        int varyingRows = 0;
        for (int row = 0; row < Fractal3DProbeHeight; row++)
        {
            int minimum = 255;
            int maximum = 0;
            for (int column = 0; column < Fractal3DProbeWidth; column++)
            {
                int value = pixels[row * stride + column * 4 + 2];
                minimum = Math.Min(minimum, value);
                maximum = Math.Max(maximum, value);
            }
            if (maximum - minimum > 12) varyingRows++;
        }
        return varyingRows >= Fractal3DProbeHeight / 4;
    }
}
