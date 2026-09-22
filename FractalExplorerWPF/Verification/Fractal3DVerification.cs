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

        VerifyFractal3DSaves();
        Console.WriteLine($"PASS (fractal3d): {Enum.GetValues<Fractal3DKind>().Length} modes, presets, coloring, camera and saves.");
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
            store.Save(original);

            Fractal3DState loaded = store.Load().Single(item => item.SaveName == original.SaveName);
            Check(loaded.Kind == original.Kind && loaded.Timestamp == original.Timestamp &&
                  loaded.Iterations == original.Iterations && loaded.Power.Equals(original.Power) &&
                  loaded.CameraYaw.Equals(original.CameraYaw) && loaded.CameraPitch.Equals(original.CameraPitch) &&
                  loaded.CameraDistance.Equals(original.CameraDistance) &&
                  loaded.TargetX.Equals(original.TargetX) && loaded.FieldOfView.Equals(original.FieldOfView) &&
                  loaded.MaxSteps == original.MaxSteps && loaded.Detail.Equals(original.Detail) &&
                  loaded.ColoringMode == original.ColoringMode && loaded.ColorA == original.ColorA &&
                  loaded.SoftShadows == original.SoftShadows && loaded.AmbientOcclusion == original.AmbientOcclusion,
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
