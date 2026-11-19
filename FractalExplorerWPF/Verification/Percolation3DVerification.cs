using System.IO;
using System.Windows;
using System.Windows.Controls;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.PercolationMath;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyPercolation3DAsync(string[] args)
    {
        const Fractal3DKind kind = Fractal3DKind.Percolation3D;
        const int side = 12;
        var token = CancellationToken.None;
        var sample = new Percolation3DSettings { Size = side, Axis = Percolation3DAxis.X };
        int Index(int x, int y, int z) => (z * side + y) * side + x;
        var occupied = new bool[side * side * side];

        // Два хода перпендикулярно X обязательны: прямой мост разорван в середине.
        for (int x = 0; x < side; x++)
            occupied[Index(x, x is 5 or 6 ? 3 : 2, 4)] = true;
        occupied[Index(4, 3, 4)] = occupied[Index(7, 3, 4)] = true;
        // Эти две ячейки соседствуют только в плоском массиве.
        occupied[Index(side - 1, 6, 6)] = occupied[Index(0, 7, 6)] = true;
        var result = Percolation3D.Analyze(sample, occupied, token);
        Check(result.Statistics is { ClusterCount: 3, SpanningClusters: 1, CrossingAxes: 1, PathSteps: side + 1 },
            "Only face neighbors connect; shortest path makes the two necessary turns.");
        Check(result.Path[0] == Index(0, 2, 4) && result.Path[^1] == Index(side - 1, 2, 4),
            "Path runs from the negative X face to the positive face.");
        var alongZ = Percolation3D.Analyze(sample with { Axis = Percolation3DAxis.Z }, occupied, token);
        Check(alongZ.Path.Length == 0 && alongZ.Statistics.Crosses(Percolation3DAxis.X),
            "Changing requested axis preserves the independent crossing statistics.");

        foreach (var medium in Enum.GetValues<Percolation3DMedium>())
        {
            var s = sample with { Medium = medium, Size = 19, Seed = -9876, Probability = .31 };
            var a = Percolation3D.GenerateOccupancy(s, token);
            var b = Percolation3D.GenerateOccupancy(s with { Probability = .58 }, token);
            Check(a.SequenceEqual(Percolation3D.GenerateOccupancy(s, token)) &&
                  !a.SequenceEqual(Percolation3D.GenerateOccupancy(s with { Seed = -9877 }, token)) &&
                  a.Zip(b).All(pair => !pair.First || pair.Second),
                "Seed and threshold reproduce nested realizations.");
            var empty = Percolation3DVolume.Build(s with { Probability = 0 }, token);
            var full = Percolation3DVolume.Build(s with { Probability = 1 }, token);
            Check(empty.Statistics.Occupied == 0 && empty.Voxels.All(v => v == 0) &&
                  full.Statistics is { ClusterCount: 1, CrossingAxes: 7 } && full.Statistics.PathSteps == s.Size - 1,
                "Exact empty and full fields for each medium.");
        }

        IReadOnlyList<Fractal3DState> presets = Fractal3DCatalog.GetPresets(kind);
        foreach (var preset in presets) Fractal3DCatalog.Mode(kind).Validate(preset);
        var start = presets[0].Percolation3D!;
        var startField = Percolation3DVolume.Build(start, token);
        var porous = Percolation3DVolume.Build(presets[5].Percolation3D!, token);
        Check(startField.Statistics.PathSteps > 0 && porous.Statistics.PathSteps > 0,
            "Both route presets have a crossing along the selected axis.");
        Check(Percolation3DVolume.Build(presets[1].Percolation3D!, token).Statistics.CrossingAxes == 0,
            "Below-transition preset has no crossing.");

        using var renderer = new Fractal3DRenderer();
        const int pixels = 320;
        string? output = ReadOption(args, "--out");
        if (output is not null) Directory.CreateDirectory(output);
        byte[] route = await Render(presets[0], "route");
        var cached = renderer.PercolationField!.Value.Statistics;
        var display = presets[0].Clone();
        display.Percolation3D = start with { ShowPath = false };
        byte[] withoutRoute = await Render(display, "without-route");
        Check(!route.SequenceEqual(withoutRoute), "Path overlay changes actual GPU pixels.");
        display.Percolation3D = start with { Cut = Percolation3DCut.Layer, CutAxis = Percolation3DAxis.X, CutPosition = .48 };
        byte[] layer = await Render(display, "layer");
        Check(!route.SequenceEqual(layer) &&
              ReferenceEquals(cached, renderer.PercolationField!.Value.Statistics), "Cut and coloring reuse the uploaded field.");
        display = presets[4].Clone();
        await Render(display, "porous-cut");
        await Render(presets[5], "porous-route");
        Check(!ReferenceEquals(cached, renderer.PercolationField!.Value.Statistics), "New occupancy triggers field upload.");

        display.Percolation3D = sample with { Probability = 1, Cut = Percolation3DCut.Layer,
            CutAxis = Percolation3DAxis.Z, CutPosition = 0 };
        display.CameraYaw = 0;
        display.CameraPitch = 0;
        var camera = Fractal3DCamera.Build(display);
        double expected = camera.Position.Z > 0 ? camera.Position.Z + 1 - 2.0 / side : -camera.Position.Z - 1;
        double hit = await renderer.ProbeDistanceAsync(display, pixels / 2, pixels / 2, pixels, pixels, token);
        Check(double.IsFinite(hit) && Math.Abs(hit - expected) < .005, $"Probe reaches the selected outer slice at voxel depth {expected}, got {hit}.");
        display.Percolation3D = display.Percolation3D with { Probability = 0 };
        Check(double.IsNaN(await renderer.ProbeDistanceAsync(display, pixels / 2, pixels / 2, pixels, pixels, token)),
            "Empty cut has no focus surface.");
        Check(File.Exists(AppPaths.GetShaderCacheFile("percolation3d-pixel")), "Mode uses the shared shader cache.");

        display.SaveName = $"Проверка перколяции 3D · {Guid.NewGuid():N}";
        display.Percolation3D = start with { Seed = int.MinValue + 1, Probability = .337123456789,
            Cut = Percolation3DCut.Cutaway, CutAxis = Percolation3DAxis.X, CutPosition = .6123456789, ShowPath = false };
        var store = new Fractal3DSaveStore(kind);
        store.Save(display);
        var restored = store.Load().Single(s => s.SaveName == display.SaveName);
        Check(restored.Percolation3D == display.Percolation3D, "Save round trip preserves the field and view.");
        EnsureWindowThemeStyles();
        var window = new Fractal3DWindow(kind);
        try
        {
            window.LoadState(restored);
            var editor = (Percolation3DEditor)window.FindName("PercolationEditor");
            Check(editor.Visibility == Visibility.Visible && window.CaptureState("ui").Percolation3D == restored.Percolation3D,
                "Window restores the exact sliders and selected controls.");
            var pBox = (TextBox)editor.FindName("ProbabilityBox");
            pBox.Text = "NaN";
            NumericInput.CommitAll(window);
            Check(NumericInput.GetHasNotice(pBox) && editor.Capture().Probability == .337123456789,
                "Invalid threshold restores the precise applied probability.");
            pBox.Text = "0,45";
            Check(editor.Capture().Probability == .45, "A decimal comma updates the probability.");
        }
        finally { window.Close(); }
        Console.WriteLine("PASS (percolation3d): reproducibility, shortest path, cut depth, GPU texture cache, UI and saves.");

        async Task<byte[]> Render(Fractal3DState state, string name)
        {
            var bitmap = await renderer.RenderAsync(state, pixels, pixels, null, token);
            if (output is not null) WriteLSystemPng(Path.Combine(output, $"percolation-{name}.png"), bitmap);
            var bytes = Pixels(bitmap);
            Check(HasFractal3DStructure(bytes, pixels, pixels, pixels / 10), $"Rendered visible geometry: {name}.");
            return bytes;
        }
    }
}
