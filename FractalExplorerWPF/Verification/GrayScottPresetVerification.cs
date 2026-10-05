using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

internal static partial class Program
{
    private static Task VerifyGrayScottPresetsAsync(string[] args, bool gpu = false) => Task.Run(() =>
    {
        // A shared rule can agree on both engines while its presets still die out.
        // Check the advertised behaviour over model time, including actual spot division.
        string? output = args.Length > 1 ? Directory.CreateDirectory(Path.GetFullPath(args[1])).FullName : null;
        int size = gpu ? 512 : 192;
        foreach (GrayScottPreset preset in GrayScottPresets.All)
        {
            GrayScottState state = preset.State.Clone(); state.GridSize = size;
            using IGrayScottEngine engine = gpu ? new GrayScottGpuEngine(state) : new GrayScottCpuEngine(state);
            var frames = new Dictionary<int, GrayScottSnapshot>();
            int previous = 0;
            foreach (int step in new[] { 100, 500, 1000, 2000, 4000, 6000 })
            {
                AdvanceGrayScottSteps(engine, step - previous);
                GrayScottSnapshot frame = engine.Snapshot(); frames.Add(step, frame); previous = step;
                Check(frame.U.Concat(frame.V).All(v => float.IsFinite(v) && v is >= 0 and <= 1),
                    $"{preset.Id} must remain bounded at step {step}.");
                if (output is not null) WriteGrayScottFrame(frame, state, Path.Combine(output, $"{(gpu ? "gpu" : "cpu")}-{preset.Id}-{step}.png"));
            }
            var early = frames[500]; var late = frames[6000];
            GrayScottSnapshot visible = preset.Id == "waves" ? frames[1000] : late;
            Check(visible.V.Max() - visible.V.Min() > .1 && visible.V.Count(v => v > .15f) > 30,
                $"{preset.Id} must produce a visible spatial pattern.");
            switch (preset.Id)
            {
                case "mitosis":
                    int before = GrayScottComponents(early), after = GrayScottComponents(frames[4000]);
                    Check(before > 0 && after > 2 * before, $"Mitosis must split surviving spots: {before} -> {after}.");
                    Console.WriteLine($"Gray–Scott {(gpu ? "GPU" : "CPU")} mitosis: {before} -> {after} spots (500 -> 4000 steps).");
                    break;
                case "worms":
                    Check(frames[4000].V.Count(v => v > .15f) > 1.5 * frames[100].V.Count(v => v > .15f),
                        "Worm seeds must grow into extended structures, rather than remain fixed dots.");
                    double wormChange = GrayScottFieldChange(frames[4000], late);
                    Check(wormChange > .0005, $"Worms must continue to rearrange: mean change {wormChange:G4}.");
                    Console.WriteLine($"Gray–Scott {(gpu ? "GPU" : "CPU")} worms: mean late change {wormChange:G4}.");
                    break;
                case "solitons":
                    Check(GrayScottComponents(early) == 1 && GrayScottComponents(late) == 1 &&
                        Math.Abs(late.V.Sum() / early.V.Sum() - 1) < .01,
                        "An isolated soliton must survive without multiplying or disappearing.");
                    break;
                case "waves":
                    // Fronts may annihilate on the torus later. Test propagation before collision.
                    Check(frames[1000].V.Max() > .3f && GrayScottOuterFront(frames[500]) > GrayScottOuterFront(frames[100]) + 10,
                        "The ring must emit a travelling front before collision, rather than just fade in place.");
                    break;
                case "chaos":
                    Check(GrayScottFieldChange(frames[4000], late) > .001,
                        "Chaotic domains must keep evolving after their initial transient.");
                    break;
            }
            Console.WriteLine($"Gray–Scott {(gpu ? "GPU" : "CPU")} {preset.Id}: checked through {late.StepCount} steps on {size}².");
        }

        var brushState = GrayScottPresets.All.Single(p => p.Id == "mitosis").State.Clone(); brushState.GridSize = 128;
        var u = new float[128 * 128]; Array.Fill(u, 1f);
        brushState.Checkpoint = new(128, u, new float[u.Length], 0);
        using IGrayScottEngine brush = gpu ? new GrayScottGpuEngine(brushState) : new GrayScottCpuEngine(brushState);
        brush.Inject(.5, .5, brushState.BrushRadius); AdvanceGrayScottSteps(brush, 4000);
        Check(GrayScottComponents(brush.Snapshot()) >= 4, "A brush seed on an empty mitosis field must survive and divide.");
        Console.WriteLine($"PASS (gray-scott{(gpu ? "-gpu presets" : "")}): six preset behaviours and viable brush seeding.");
    });

    private static void AdvanceGrayScottSteps(IGrayScottEngine engine, int steps)
    {
        for (int done = 0; done < steps; done += 1000)
            engine.Advance(Math.Min(1000, steps - done), CancellationToken.None);
    }

    private static double GrayScottFieldChange(GrayScottSnapshot a, GrayScottSnapshot b) =>
        a.V.Zip(b.V, (x, y) => (double)Math.Abs(x - y)).Average();

    private static double GrayScottOuterFront(GrayScottSnapshot frame)
    {
        double center = (frame.Size - 1) * .5, radius = 0;
        for (int y = 0; y < frame.Size; y++) for (int x = 0; x < frame.Size; x++)
            if (frame.V[y * frame.Size + x] > .15f)
                radius = Math.Max(radius, Math.Sqrt((x - center) * (x - center) + (y - center) * (y - center)));
        return radius;
    }

    private static int GrayScottComponents(GrayScottSnapshot frame)
    {
        int n = frame.Size, count = 0; var visited = new bool[n * n]; var pending = new Stack<int>();
        for (int index = 0; index < visited.Length; index++)
        {
            if (visited[index] || frame.V[index] <= .15f) continue;
            count++; visited[index] = true; pending.Push(index);
            while (pending.TryPop(out int current))
            {
                int x = current % n, y = current / n;
                Visit(y * n + (x + 1) % n); Visit(y * n + (x + n - 1) % n);
                Visit(((y + 1) % n) * n + x); Visit(((y + n - 1) % n) * n + x);
            }
        }
        return count;

        void Visit(int index)
        {
            if (visited[index] || frame.V[index] <= .15f) return;
            visited[index] = true; pending.Push(index);
        }
    }

    private static void WriteGrayScottFrame(GrayScottSnapshot frame, GrayScottState state, string path)
    {
        byte[] pixels = GrayScottRenderer.RenderFrame(frame, state, CancellationToken.None);
        var bitmap = BitmapSource.Create(frame.Size, frame.Size, 96, 96, PixelFormats.Bgra32, null, pixels, frame.Size * 4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
