using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;

// Трёхмерные фракталы: дистанционные оценки шести видов на GPU, их пресеты и сохранения.
// Окна не показываются, кадры считаются в маленьком разрешении.
internal static partial class Program
{
    private const int Fractal3DProbeWidth = 120;
    private const int Fractal3DProbeHeight = 90;

    // Зонд поверхности меряется на кадре покрупнее: порог попадания луча пропорционален высоте
    // кадра, и на 90 строках две близкие дистанции различались бы хуже допуска проверки.
    private const int Fractal3DRayWidth = 480;
    private const int Fractal3DRayHeight = 360;

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
        await VerifyFractal3DProbeAsync(renderer);
        await VerifyDistantFractal3DProbesAsync(renderer);
        VerifyFractal3DPalettes();
        await VerifyFractal3DColoringAsync(renderer);
        VerifyFractal3DSaves();
        Console.WriteLine($"PASS (fractal3d): {Enum.GetValues<Fractal3DKind>().Length} modes, presets, " +
                          $"{Fractal3DPalettes.All.Count} palettes, {Enum.GetValues<Fractal3DShadingStyle>().Length} shaders, " +
                          "coloring sources, camera, navigation, smooth zoom, surface probe and saves.");
    }

    /// <summary>
    /// Два якоря вращения: вокруг фрактала камера облетает точку наблюдения, игровая камера
    /// поворачивает взгляд, не сходя с места. Плюс луч через пиксель, по которому зонд откладывает
    /// измеренное расстояние.
    /// </summary>
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

    private static void VerifyFractal3DCameraMath()
    {
        var orbit = new Fractal3DOrbit(35, 18, 2.8, new Vector3(0.1f, -0.2f, 0.3f));
        Vector3 position = Fractal3DCamera.Position(orbit);

        Fractal3DOrbit around = Fractal3DCamera.Rotate(orbit, Fractal3DRotationAnchor.Target, 24, 9);
        Check(around.Target == orbit.Target && around.Distance.Equals(orbit.Distance),
            "Orbiting must keep the target and the distance.");
        Check((Fractal3DCamera.Position(around) - position).Length() > 0.1f, "Orbiting must move the camera.");
        Check(Math.Abs(around.Yaw - (orbit.Yaw - 24)) < 1e-9 && Math.Abs(around.Pitch - (orbit.Pitch + 9)) < 1e-9,
            "Orbiting must turn the camera by the drag.");

        Fractal3DOrbit free = Fractal3DCamera.Rotate(orbit, Fractal3DRotationAnchor.FreeLook, 24, 9);
        Check((Fractal3DCamera.Position(free) - position).Length() < 1e-5f,
            "Free look must keep the camera in place.");
        Check((free.Target - orbit.Target).Length() > 0.1f, "Free look must carry the target.");
        Check(Math.Abs(free.Yaw - (orbit.Yaw + 24)) < 1e-9 && Math.Abs(free.Pitch - (orbit.Pitch - 9)) < 1e-9,
            "Free look must turn the view opposite to orbiting.");

        Check(Fractal3DCamera.Rotate(orbit, Fractal3DRotationAnchor.Target, 0, 500).Pitch <= Fractal3DCamera.MaxPitch &&
              Fractal3DCamera.Rotate(orbit, Fractal3DRotationAnchor.FreeLook, 0, 500).Pitch >= Fractal3DCamera.MinPitch,
            "The pitch must stay between the poles in both anchors.");

        Fractal3DState state = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Mandelbulb);
        Fractal3DCameraBasis basis = Fractal3DCamera.Build(state);
        Vector3 centre = Fractal3DCamera.PixelRay(
            state, Fractal3DRayWidth / 2.0, Fractal3DRayHeight / 2.0, Fractal3DRayWidth, Fractal3DRayHeight);
        Check((centre - basis.Forward).Length() < 1e-5f,
            "The ray through the centre of the frame must be the view direction.");

        (double yaw, double pitch) = Fractal3DCamera.Angles(Fractal3DCamera.Direction(35, 18));
        Check(Math.Abs(yaw - 35) < 1e-4 && Math.Abs(pitch - 18) < 1e-4,
            "Angles of a direction must round-trip.");
    }

    /// <summary>
    /// Доводка колеса: щелчок задаёт цель, а камера идёт к ней несколько кадров. Главное —
    /// прийти ровно в цель и ни разу её не проскочить, иначе зум «дышал» бы на каждом щелчке.
    /// </summary>
    private static void VerifyFractal3DZoomGlide()
    {
        var glide = new Fractal3DZoomGlide();
        var orbit = new Fractal3DOrbit(20, 10, 4, new Vector3(1, 0, -1));
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
        Fractal3DOrbit jumped = glide.Advance(orbit, 10);
        Check((jumped.Target - (orbit.Target + new Vector3(1, 2, 0))).Length() < 1e-5f &&
              !glide.IsActive(jumped.Distance),
            "A long frame must land on the aim instead of flying past it.");

        // Упор в минимальное расстояние не оставляет вечного остатка.
        glide.Clear();
        glide.Aim(Fractal3DCamera.MinDistance / 1000, Vector3.Zero);
        Fractal3DOrbit squeezed = jumped with { Distance = 1 };
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
            original.RotationAnchor = Fractal3DRotationAnchor.FreeLook;
            original.MotionQuality = Fractal3DMotionQuality.Draft;
            original.MotionResolution = Fractal3DMotionResolution.Full;
            original.ZoomToCursor = false;
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
                  loaded.RotationAnchor == original.RotationAnchor && loaded.MotionQuality == original.MotionQuality &&
                  loaded.MotionResolution == original.MotionResolution &&
                  loaded.ZoomToCursor == original.ZoomToCursor && loaded.RotationInertia == original.RotationInertia &&
                  loaded.AutoRotate == original.AutoRotate && loaded.AutoRotateSpeed.Equals(original.AutoRotateSpeed) &&
                  loaded.ShadingStyle == original.ShadingStyle &&
                  loaded.EffectStrength.Equals(original.EffectStrength) &&
                  loaded.ColorRepeat == original.ColorRepeat && loaded.LightColor == original.LightColor &&
                  loaded.SkyLightMix.Equals(original.SkyLightMix) &&
                  loaded.Palette is not null && loaded.Palette.Name == original.Palette.Name &&
                  loaded.Palette.Gamma.Equals(original.Palette.Gamma) &&
                  loaded.Palette.Colors.SequenceEqual(original.Palette.Colors),
                $"{kind}: the save must restore every parameter of the state.");
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
