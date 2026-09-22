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
        byte[] filled = await renderer.RenderPixelsAsync(
            state, Fractal3DProbeWidth, Fractal3DProbeHeight, reused, null, CancellationToken.None);
        Check(ReferenceEquals(filled, reused), "A buffer large enough must be reused instead of allocated again.");
        Check(filled.Take(reference.Length).SequenceEqual(reference),
            "A frame rendered into a ready buffer must match the ordinary frame.");

        VerifyFractal3DCameraMath();
        await VerifyFractal3DProbeAsync(renderer);
        VerifyFractal3DSaves();
        Console.WriteLine($"PASS (fractal3d): {Enum.GetValues<Fractal3DKind>().Length} modes, presets, coloring, " +
                          "camera, navigation, surface probe and saves.");
    }

    /// <summary>
    /// Два якоря вращения: вокруг фрактала камера облетает точку наблюдения, игровая камера
    /// поворачивает взгляд, не сходя с места. Плюс луч через пиксель, по которому зонд откладывает
    /// измеренное расстояние.
    /// </summary>
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
    /// Зонд поверхности: от него зависят перелёт по двойному щелчку и шаг движения колесом и
    /// клавишами, поэтому проверяются и геометрия луча, и масштаб ответа.
    /// </summary>
    private static async Task VerifyFractal3DProbeAsync(Fractal3DRenderer renderer)
    {
        Fractal3DState state = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Mandelbulb);
        double centre = await ProbeFractal3DAsync(renderer, state, Fractal3DRayWidth / 2.0, Fractal3DRayHeight / 2.0);
        Check(double.IsFinite(centre) && centre > 0 && centre < state.CameraDistance,
            "The probe must find the surface between the camera and the target.");

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

    private static void VerifyFractal3DSaves()
    {
        foreach (Fractal3DKind kind in Enum.GetValues<Fractal3DKind>())
        {
            var store = new Fractal3DSaveStore(kind);
            Fractal3DState original = Fractal3DCatalog.GetPresets(kind)[^1].Clone();
            original.SaveName = $"Проверка {kind}";
            original.Timestamp = new DateTime(2026, 3, 4, 5, 6, 7);
            original.ColorA = Color.FromRgb(11, 22, 33);
            original.RotationAnchor = Fractal3DRotationAnchor.FreeLook;
            original.MotionQuality = Fractal3DMotionQuality.Draft;
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
                  loaded.ZoomToCursor == original.ZoomToCursor && loaded.RotationInertia == original.RotationInertia &&
                  loaded.AutoRotate == original.AutoRotate && loaded.AutoRotateSpeed.Equals(original.AutoRotateSpeed),
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
