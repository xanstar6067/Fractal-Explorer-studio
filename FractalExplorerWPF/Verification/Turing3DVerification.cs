using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Infrastructure.Cloud;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyTuring3DAsync(string[] args)
    {
        using var sandbox = DataSandbox.Create("turing3d");
        string? output = args.Length > 1 && !args[1].StartsWith("--", StringComparison.Ordinal) ? args[1] : null;
        if (output is not null) Directory.CreateDirectory(output);
        // Simulation and display share the renderer's device, exactly as in the window.
        using var renderer = new Fractal3DRenderer();
        var host = renderer.DeviceHost;
        var kind = Fractal3DKind.Turing3D;
        if (args.Contains("--probe"))
        {
            await ProbeTuring3DPresetsAsync(renderer, output);
            return;
        }
        if (args.Contains("--sweep"))
        {
            await SweepTuring3DAsync(renderer, output ?? ".", args.Contains("--presets"));
            return;
        }
        Check(FractalCatalog.Create().Single(t => t.LaunchKey == Fractal3DCatalog.LaunchKey(kind)).IsThreeDimensional,
            "Turing 3D must appear in the cross-category 3D collection.");

        // ----- Groups: closed, orthogonal, of the expected orders, identity first. -----
        foreach (var (symmetry, arms, mirror, order) in new[]
        {
            (Turing3DSymmetry.None, 6, true, 1), (Turing3DSymmetry.Axial, 1, false, 1), (Turing3DSymmetry.Axial, 1, true, 2),
            (Turing3DSymmetry.Axial, 6, false, 6), (Turing3DSymmetry.Axial, 7, true, 14), (Turing3DSymmetry.Tetrahedral, 1, false, 12),
            (Turing3DSymmetry.Tetrahedral, 1, true, 24), (Turing3DSymmetry.Octahedral, 1, false, 24), (Turing3DSymmetry.Octahedral, 1, true, 48),
            (Turing3DSymmetry.Icosahedral, 1, false, 60), (Turing3DSymmetry.Icosahedral, 1, true, 120)
        })
        {
            var group = Turing3DSymmetryGroup.Build(symmetry, arms, mirror);
            Check(group.Count == order && IsIdentity(group[0]), $"{symmetry}×{arms}{(mirror ? "+mirror" : "")}: expected order {order}, got {group.Count}.");
            foreach (var g in group) Check(IsOrthogonal(g), $"{symmetry}: every element must be orthogonal.");
            foreach (var a in group) foreach (var b in group)
                Check(group.Any(c => SameMatrix(c, Multiply(a, b))), $"{symmetry}: the set must be closed under composition.");
        }
        // The domain direction must not lie on a mirror plane: no other element may fix it.
        foreach (var symmetry in Enum.GetValues<Turing3DSymmetry>())
        {
            var group = Turing3DSymmetryGroup.Build(symmetry, 16, true);
            var (dx, dy, dz) = Turing3DSymmetryGroup.DomainDirection;
            Check(group.Skip(1).All(g => Math.Abs(g[0] * dx + g[1] * dy + g[2] * dz - dx) + Math.Abs(g[3] * dx + g[4] * dy + g[5] * dz - dy) +
                Math.Abs(g[6] * dx + g[7] * dy + g[8] * dz - dz) > 1e-3), $"{symmetry}: the domain direction must be generic.");
        }

        // ----- One step against an independent double-precision oracle (closed and mirrored edges). -----
        foreach (var boundary in Enum.GetValues<TuringBoundary>())
        {
            const int n = 32;
            var rule = new Turing3DSettings { Size = n, Boundary = boundary, Layers = Turing3DSettings.DefaultLayers(5), DetailSize = 1.3 };
            var random = new Random(5 + (int)boundary);
            var values = new float[n * n * n];
            for (int i = 0; i < values.Length; i++) values[i] = (float)(random.NextDouble() * 2 - 1);
            var start = new Turing3DField(n, 40, values, new byte[values.Length]);
            using var gpu = new Turing3DGpuSimulation(host, rule with { Field = start });
            Check(gpu.DeviceName.StartsWith("ГП", StringComparison.Ordinal), "The 3D simulation runs on the window's GPU device.");
            Check(MaxFieldDifference(gpu.ReadCurrent(), start) == 0 && gpu.Step == 40, "A checkpoint must load exactly, without a hidden step.");
            gpu.Advance(1, CancellationToken.None);
            var one = gpu.ReadCurrent();
            var (expected, expectedScales) = TuringReferenceStep(values, n, rule);
            double[] errors = one.Field.ToArray().Select((v, i) => Math.Abs(v - expected[i])).ToArray();
            int flipped = errors.Count(e => e > 1e-4);
            int scaleMismatch = one.ScaleMap.ToArray().Where((s, i) => s != expectedScales[i]).Count();
            Console.WriteLine($"Turing 3D oracle ({boundary}): max error {errors.Where(e => e <= 1e-4).DefaultIfEmpty().Max():E2}, near-tie cells {flipped}, scale mismatches {scaleMismatch}.");
            Check(one.Step == 41 && flipped <= values.Length / 2000 && scaleMismatch <= values.Length / 2000,
                $"The GPU step must match the independent multiscale rule with {boundary} edges.");
            Check(one.Field.ToArray().Min() == -1 && one.Field.ToArray().Max() == 1, "Every step must normalise the field to [-1, 1].");
        }

        // ----- Symmetry: one sample per cell, exactly invariant on grid-preserving groups. -----
        foreach (var (symmetry, arms, size) in new[] { (Turing3DSymmetry.Octahedral, 1, 33), (Turing3DSymmetry.Tetrahedral, 1, 32), (Turing3DSymmetry.Axial, 4, 34) })
        {
            var rule = new Turing3DSettings { Size = size, Symmetry = symmetry, Arms = arms, Mirror = true, Boundary = TuringBoundary.Reflect, Seed = 77 };
            using var gpu = new Turing3DGpuSimulation(host, rule);
            Check(gpu.SymmetryOrder == Turing3DSymmetryGroup.Build(symmetry, arms, true).Count, "The GPU must use the whole group.");
            foreach (int steps in new[] { 0, 3 })
            {
                gpu.Advance(steps, CancellationToken.None);
                var field = gpu.ReadCurrent();
                double error = SymmetryError(field, Turing3DSymmetryGroup.Build(symmetry, arms, true));
                Check(error < 2e-5, $"{symmetry}: the field after {field.Step} steps must be invariant (error {error:E2}).");
            }
        }
        {
            // Icosahedral images leave the grid; compare the GPU start with a CPU representative sampler.
            const int n = 40;
            var rule = new Turing3DSettings { Size = n, Symmetry = Turing3DSymmetry.Icosahedral, Mirror = true, Boundary = TuringBoundary.Reflect, Seed = 9 };
            using var gpu = new Turing3DGpuSimulation(host, rule);
            var field = gpu.ReadCurrent().Field.ToArray();
            var noise = new float[n * n * n]; var random = new Random(rule.Seed);
            for (int i = 0; i < noise.Length; i++) noise[i] = (float)(random.NextDouble() * 2 - 1);
            var expected = Normalize(RepresentativeSample(noise, n, Turing3DSymmetryGroup.Build(rule.Symmetry, 1, true)));
            int differing = field.Where((v, i) => Math.Abs(v - expected[i]) > 1e-3).Count();
            Check(differing <= noise.Length / 500, $"Icosahedral symmetry must sample the orbit representative ({differing} cells differ).");
        }

        // ----- Brush: repeated over the group, periodic on closed edges, time unchanged. -----
        {
            const int n = 33;
            var zero = new Turing3DField(n, 5, new float[n * n * n], new byte[n * n * n]);
            var rule = new Turing3DSettings { Size = n, Symmetry = Turing3DSymmetry.Octahedral, Mirror = true, Boundary = TuringBoundary.Reflect, Field = zero };
            using var gpu = new Turing3DGpuSimulation(host, rule);
            gpu.Paint(.8, .62, .55, .08, 1, TuringBrush.Light);
            var painted = gpu.ReadCurrent();
            Check(painted.Step == 5 && painted.Field.ToArray().Max() > .5 && SymmetryError(painted, Turing3DSymmetryGroup.Build(rule.Symmetry, 1, true)) < 1e-6,
                "A brush stroke must be repeated over the whole symmetry group without advancing time.");
            using var wrapped = new Turing3DGpuSimulation(host, new Turing3DSettings { Size = n, Field = zero });
            wrapped.Paint(0, .5, .5, .1, 1, TuringBrush.Dark);
            var dark = wrapped.ReadCurrent().Field.ToArray();
            Check(dark[(16 * n + 16) * n] < -.5 && dark[(16 * n + 16) * n + n - 1] < -.3 && dark.Max() == 0,
                "On closed edges a stroke at the boundary must reach both sides of the cube.");
        }

        // ----- Closing a window: the renderer may be released first, the simulation then holds the last device reference. -----
        foreach (bool grayScott in new[] { false, true })
        {
            var owner = new Fractal3DRenderer();
            IDisposable simulation = grayScott
                ? new GrayScott3DGpuSimulation(owner.DeviceHost, new GrayScott3DSettings { Size = 32 })
                : new Turing3DGpuSimulation(owner.DeviceHost, new Turing3DSettings { Size = 32 });
            owner.Dispose();
            simulation.Dispose(); // threw ObjectDisposedException when the gate was released after the device
            simulation.Dispose();
            try { owner.DeviceHost.AddRef(); throw new Exception("The device survived its last reference."); }
            catch (ObjectDisposedException) { }
        }

        // ----- Publication slots, exact checkpoints and continuation. -----
        var settings = new Turing3DSettings { Size = 48, Seed = 11 };
        using var live = new Turing3DGpuSimulation(host, settings);
        live.Advance(6, CancellationToken.None);
        var shown = live.Publish();
        var shownField = live.ReadCheckpoint(shown);
        Check(shownField.Step == 6 && MaxFieldDifference(shownField, live.ReadCurrent()) == 0, "A published frame must hold the exact field.");
        live.Advance(2, CancellationToken.None); var next = live.Publish(shown);
        live.Advance(2, CancellationToken.None); var third = live.Publish(shown);
        Check(next.Slot == third.Slot && third.Slot != shown.Slot && MaxFieldDifference(live.ReadCheckpoint(shown), shownField) == 0,
            "Publishing past a kept frame must never overwrite it.");
        try { live.ReadCheckpoint(next); throw new Exception("A replaced frame was read."); }
        catch (InvalidOperationException) { }
        using (var cancellation = new CancellationTokenSource())
        {
            var before = live.ReadCurrent(); cancellation.Cancel();
            Check(live.Advance(10, cancellation.Token) == 0 && MaxFieldDifference(before, live.ReadCurrent()) == 0,
                "Cancellation before a step must preserve the field and time.");
        }

        var options = JsonOptionsFactory.Create(); var state = Fractal3DCatalog.CreateDefaultState(kind);
        state.Turing = settings with { Field = live.ReadCurrent() }; state.SaveName = "3D exact continuation";
        string json = JsonSerializer.Serialize(state, options);
        Check(!json.Contains("\"Live\"", StringComparison.Ordinal), "Saves must not contain GPU handles.");
        var restored = JsonSerializer.Deserialize<Fractal3DState>(json, options)!;
        Check(MaxFieldDifference(state.Turing.Field!, restored.Turing.Field!) == 0 && restored.Turing.Field!.Step == 10 &&
            restored.Turing.Field.ScaleMap.SequenceEqual(state.Turing.Field.ScaleMap) && restored.Turing.SameRule(state.Turing),
            "Brotli checkpoints must retain exact float32 bits, the scale map, the rule and the time.");
        using (var continued = new Turing3DGpuSimulation(host, restored.Turing))
        {
            continued.Advance(5, CancellationToken.None); live.Advance(5, CancellationToken.None);
            Check(MaxFieldDifference(live.ReadCurrent(), continued.ReadCurrent()) == 0, "Disk continuation on the same GPU must be exact.");
        }
        var corrupt = JsonNode.Parse(json)!.AsObject(); corrupt["Turing"]!["Field"]!["Data"] = "AAAA";
        try { JsonSerializer.Deserialize<Fractal3DState>(corrupt.ToJsonString(), options); throw new Exception("Corrupt field accepted."); }
        catch (JsonException) { }

        // A rule change keeps the field and time; the next step follows the new rule.
        var reconfigured = settings with { Symmetry = Turing3DSymmetry.Axial, Arms = 3, DetailSize = .7 };
        var beforeRule = live.ReadCurrent();
        live.Configure(reconfigured);
        Check(MaxFieldDifference(beforeRule, live.ReadCurrent()) == 0 && live.Step == beforeRule.Step && live.SymmetryOrder == 3,
            "Applying a new rule must keep the current field and time.");
        live.Advance(1, CancellationToken.None);
        Check(live.Step == beforeRule.Step + 1, "The next step must follow the new rule.");

        // Grid transfer keeps time and shape.
        var resized = Turing3DField.Resize(beforeRule, 64);
        Check(resized.Size == 64 && resized.Step == beforeRule.Step && Math.Abs(resized.Field.ToArray().Average() - beforeRule.Field.ToArray().Average()) < .02,
            "Grid transfer must keep the time and the overall field.");

        var store = new Fractal3DSaveStore(kind); store.Save(state);
        Check(MaxFieldDifference(store.Load().Single().Turing.Field!, state.Turing.Field!) == 0, "The shared save manager store must retain the field.");
        Check(CloudSaveRepository.ListLocal(out int unreadable).Single().Category == "Fractal3DTuring" && unreadable == 0,
            "The new category must be visible to the existing cloud repository.");

        // ----- Presets, styles, regions, coloring, probe and cache. -----
        var presets = Fractal3DCatalog.GetPresets(kind);
        Fractal3DState? reference = null;
        foreach (var preset in presets)
        {
            var watch = Stopwatch.StartNew();
            using var simulation = new Turing3DGpuSimulation(host, preset.Turing);
            await Task.Run(() => simulation.Advance(preset.Turing.WarmupSteps, CancellationToken.None));
            var early = simulation.ReadCurrent();
            double perStep = watch.Elapsed.TotalMilliseconds / Math.Max(1, preset.Turing.WarmupSteps);
            await Task.Run(() => simulation.Advance(40, CancellationToken.None));
            var late = simulation.ReadCurrent();
            double change = MaxFieldDifference(early, late);
            double solid = early.Field.ToArray().Count(v => v * .5 + .5 > preset.Turing.Level) / (double)early.Field.Length;
            Console.WriteLine($"{preset.SaveName}: {perStep:F1} ms/step at {preset.Turing.Size}³, solid {solid:P0}, change {change:F3}");
            Check(solid is > .08 and < .92 && change > .01, "Presets must form a two-phase volume that keeps evolving.");
            var view = preset.Clone(); view.Turing = preset.Turing with { Field = early };
            var bitmap = await renderer.RenderAsync(view, 320, 320, null, CancellationToken.None);
            Check(HasFractal3DStructure(Pixels3D(bitmap), 320, 320), $"«{preset.SaveName}» must render a visible structure.");
            if (output is not null) SaveTuring3DPng(bitmap, Path.Combine(output, $"preset-{presets.ToList().IndexOf(preset)}.png"));
            reference ??= view;
        }
        state = reference!;
        byte[] basePixels = Pixels3D(await renderer.RenderAsync(state, 200, 200, null, CancellationToken.None));
        using (var previewRenderer = new Fractal3DRenderer())
        {
            var preparedPixels = Pixels3D(await previewRenderer.RenderAsync(Fractal3DCatalog.CreateDefaultState(kind), 200, 200, null, CancellationToken.None));
            Check(basePixels.SequenceEqual(preparedPixels), "Parameter-only previews must match the exact initial stage shown by the window.");
        }
        using (var shared = new Turing3DGpuSimulation(host, state.Turing))
        {
            var liveState = state.Clone(); liveState.Turing = state.Turing with { Field = null, Live = shared.Publish() };
            byte[] livePixels = Pixels3D(await renderer.RenderAsync(liveState, 200, 200, null, CancellationToken.None));
            Check(basePixels.SequenceEqual(livePixels), "A live GPU frame must render exactly like the same field loaded from a save.");
            using var foreign = new Fractal3DRenderer();
            try { await foreign.RenderAsync(liveState, 40, 40, null, CancellationToken.None); throw new Exception("Foreign device accepted a live frame."); }
            catch (InvalidOperationException) { }
        }
        var frames = new List<byte[]>();
        foreach (var style in Enum.GetValues<Fractal3DShadingStyle>())
        {
            var view = state.Clone(); view.ShadingStyle = style;
            var bitmap = await renderer.RenderAsync(view, 200, 200, null, CancellationToken.None);
            frames.Add(Pixels3D(bitmap));
            if (output is not null) SaveTuring3DPng(bitmap, Path.Combine(output, $"style-{(int)style}.png"));
        }
        Check(frames.Distinct(new ByteSequenceComparer()).Count() == frames.Count, "All nine styles must give different frames.");
        var looks = new List<byte[]>();
        foreach (var (name, change) in new (string, Action<Fractal3DState>)[]
        {
            ("cube", s => s.Turing = s.Turing with { Region = Turing3DRegion.Cube }),
            ("shell", s => s.Turing = s.Turing with { Region = Turing3DRegion.Shell }),
            ("cut", s => s.Turing = s.Turing with { CutAxis = 3, CutPosition = 0 }),
            ("level", s => s.Turing = s.Turing with { Level = .35 }),
            ("by-scale", s => s.ColoringMode = Fractal3DColoringMode.OrbitTrap),
            ("by-radius", s => s.ColoringMode = Fractal3DColoringMode.CrossTrap),
            ("by-height", s => s.ColoringMode = Fractal3DColoringMode.Height)
        })
        {
            var view = state.Clone(); view.ColoringMode = Fractal3DColoringMode.Depth; change(view);
            var bitmap = await renderer.RenderAsync(view, 200, 200, null, CancellationToken.None);
            Check(ReferenceEquals(view.Turing.Field, state.Turing.Field), "View changes must reuse the immutable field.");
            looks.Add(Pixels3D(bitmap));
            if (output is not null) SaveTuring3DPng(bitmap, Path.Combine(output, $"view-{name}.png"));
        }
        Check(looks.Distinct(new ByteSequenceComparer()).Count() == looks.Count, "Regions, cut, level and color sources must change the frame.");
        bool hit = false;
        for (int y = 60; y < 150 && !hit; y += 15) for (int x = 60; x < 150 && !hit; x += 15)
            hit = await renderer.ProbeDistanceAsync(state, x, y, 200, 200, CancellationToken.None) > 0;
        Check(hit, "The common 3D probe must hit the pattern surface.");
        Check(File.Exists(AppPaths.GetShaderCacheFile("turing3d-pixel")) &&
            Turing3DComputeShader.CacheEntries.All(e => File.Exists(AppPaths.GetShaderCacheFile(e.Key))),
            "Both computation and display must participate in the shared shader cache.");

        await VerifyTuring3DWindowAsync(renderer, state, store, output);
        Console.WriteLine("PASS (turing3d): groups, independent multiscale rule, exact symmetry, repeated brush, publication slots, exact continuation, rule changes, grid transfer, presets, live frames, styles, regions, probe, cache, saves, cloud, preparation progress/cancellation and WPF.");
    }

    /// <summary>Quick visual and timing pass over the presets: <c>turing3d &lt;folder&gt; --probe</c>.</summary>
    private static async Task ProbeTuring3DPresetsAsync(Fractal3DRenderer renderer, string? output)
    {
        var presets = Fractal3DCatalog.GetPresets(Fractal3DKind.Turing3D);
        for (int index = 0; index < presets.Count; index++)
        {
            var preset = presets[index];
            using var simulation = new Turing3DGpuSimulation(renderer.DeviceHost, preset.Turing);
            var view = preset.Clone();
            foreach (int stage in new[] { preset.Turing.WarmupSteps, 200 })
            {
                var watch = Stopwatch.StartNew();
                await Task.Run(() => simulation.Advance(stage, CancellationToken.None));
                double ms = watch.Elapsed.TotalMilliseconds / Math.Max(stage, 1);
                view.Turing = preset.Turing with { Live = simulation.Publish(view.Turing.Live) };
                watch.Restart();
                var bitmap = await renderer.RenderAsync(view, 480, 480, null, CancellationToken.None);
                long frameMs = watch.ElapsedMilliseconds;
                float[] field = simulation.ReadCurrent().Field.ToArray();
                string fractions = string.Join(" ", new[] { .4, .5, .6 }.Select(level => $"{level:F1}:{field.Count(v => v * .5 + .5 > level) / (double)field.Length:P0}"));
                double saturated = field.Count(v => Math.Abs(v) > .9) / (double)field.Length;
                Console.WriteLine($"{index} {preset.SaveName}: step {simulation.Step}, {ms:F1} ms/step, frame {frameMs} ms, solid {fractions}, |v|>.9 {saturated:P0}");
                if (output is not null)
                {
                    SaveTuring3DPng(bitmap, Path.Combine(output, $"probe-{index}-{simulation.Step}.png"));
                    // The middle Z slice of the raw field, grey from -1 to 1.
                    int n = preset.Turing.Size; var slice = new byte[n * n];
                    for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                        slice[(n - 1 - y) * n + x] = (byte)Math.Clamp((field[((n / 2) * n + y) * n + x] * .5 + .5) * 255, 0, 255);
                    var sliceBitmap = BitmapSource.Create(n, n, 96, 96, PixelFormats.Gray8, null, slice, n);
                    SaveTuring3DPng(new TransformedBitmap(sliceBitmap, new ScaleTransform(4, 4)), Path.Combine(output, $"probe-{index}-{simulation.Step}-slice.png"));
                }
            }
        }
    }

    /// <summary>Tuning aid: renders variants into one contact sheet, <c>turing3d &lt;folder&gt; --sweep</c>.</summary>
    private static async Task SweepTuring3DAsync(Fractal3DRenderer renderer, string output, bool presets)
    {
        Fractal3DState Base(Action<Fractal3DState> change)
        {
            var state = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Turing3D);
            state.ColoringMode = Fractal3DColoringMode.Depth;
            change(state);
            return state;
        }
        var variants = new (string Name, Fractal3DState State)[]
        {
            ("ball d1 L4", Base(_ => { })),
            ("ball d.6 L3 material", Base(s => { s.Turing = s.Turing with { DetailSize = .6, Layers = Turing3DSettings.DefaultLayers(3) }; s.ColoringMode = Fractal3DColoringMode.Material; })),
            ("cube d1 L4", Base(s => s.Turing = s.Turing with { Region = Turing3DRegion.Cube })),
            ("shell .15 d.5", Base(s => s.Turing = s.Turing with { Region = Turing3DRegion.Shell, ShellThickness = .15, DetailSize = .5 })),
            ("ico shell .12 d.5", Base(s => s.Turing = s.Turing with { Symmetry = Turing3DSymmetry.Icosahedral, Mirror = true, Boundary = TuringBoundary.Reflect, Region = Turing3DRegion.Shell, ShellThickness = .12, DetailSize = .5 })),
            ("ico shell .18 d.4 L3", Base(s => s.Turing = s.Turing with { Symmetry = Turing3DSymmetry.Icosahedral, Mirror = true, Boundary = TuringBoundary.Reflect, Region = Turing3DRegion.Shell, ShellThickness = .18, DetailSize = .4, Layers = Turing3DSettings.DefaultLayers(3) })),
            ("oct shell .2 d.6", Base(s => s.Turing = s.Turing with { Symmetry = Turing3DSymmetry.Octahedral, Mirror = true, Boundary = TuringBoundary.Reflect, Region = Turing3DRegion.Shell, ShellThickness = .2, DetailSize = .6 })),
            ("tet ball d.6", Base(s => s.Turing = s.Turing with { Symmetry = Turing3DSymmetry.Tetrahedral, Mirror = true, Boundary = TuringBoundary.Reflect, DetailSize = .6 })),
            ("ico ball sheet d.5 L3", Base(s => s.Turing = s.Turing with { Symmetry = Turing3DSymmetry.Icosahedral, Mirror = true, Boundary = TuringBoundary.Reflect, DetailSize = .5, Layers = Turing3DSettings.DefaultLayers(3), SheetThickness = .05 })),
        };
        if (presets) variants = Fractal3DCatalog.GetPresets(Fractal3DKind.Turing3D).Select(p => (p.SaveName, p)).ToArray();
        const int tile = 260, columns = 3;
        int rows = (variants.Length + columns - 1) / columns;
        var sheet = new DrawingVisual();
        using (var context = sheet.RenderOpen())
        {
            for (int i = 0; i < variants.Length; i++)
            {
                var (name, state) = variants[i];
                var watch = Stopwatch.StartNew();
                var bitmap = await renderer.RenderAsync(state, tile, tile, null, CancellationToken.None);
                Console.WriteLine($"{name}: {watch.ElapsedMilliseconds} ms");
                var rect = new Rect(i % columns * tile, i / columns * tile, tile, tile);
                context.DrawImage(bitmap, rect);
                context.DrawText(new FormattedText(name, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 13, Brushes.White, 1), rect.TopLeft + new Vector(6, 4));
            }
        }
        var target = new RenderTargetBitmap(tile * columns, tile * rows, 96, 96, PixelFormats.Pbgra32);
        target.Render(sheet);
        SaveTuring3DPng(target, Path.Combine(output, "sweep.png"));
    }

    private static async Task VerifyTuring3DWindowAsync(Fractal3DRenderer renderer, Fractal3DState state, Fractal3DSaveStore store, string? output)
    {
        var theme = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if (!Application.Current.Resources.MergedDictionaries.Any(d => d.Source == theme))
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = theme });
        var window = new Fractal3DWindow(Fractal3DKind.Turing3D);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Invoke(string name) => typeof(Fractal3DWindow).GetMethod(name, flags)!.Invoke(window, []);
        T Field<T>(string name) => (T)typeof(Fractal3DWindow).GetField(name, flags)!.GetValue(window)!;
        Turing3DVolume? Shown() => (Turing3DVolume?)typeof(Fractal3DWindow).GetField("_turingShown", flags)!.GetValue(window);
        Turing3DVolume? Pending() => (Turing3DVolume?)typeof(Fractal3DWindow).GetField("_turingPending", flags)!.GetValue(window);
        T Control<T>(string name) where T : class => (T)window.FindName(name);
        void Click(string name) => Control<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        async Task Idle()
        {
            var watch = Stopwatch.StartNew();
            while (Field<bool>("_turingBusy") && watch.ElapsedMilliseconds < 60000) await Task.Delay(10);
            Check(!Field<bool>("_turingBusy"), "Background simulation must finish or cancel.");
        }
        async Task Display()
        {
            var method = typeof(Fractal3DWindow).GetMethod("RenderFrameAsync", flags)!;
            await (Task)method.Invoke(window, [Enum.Parse(method.GetParameters()[0].ParameterType, "Full")])!;
        }
        async Task Settle() { await Idle(); await Display(); if (Pending() is not null) { await Idle(); await Display(); } }
        try
        {
            Check(Control<Border>("TuringPreparationOverlay").Visibility == Visibility.Visible &&
                !Control<Border>("ControlsHost").IsEnabled && !Control<Button>("TuringPlayButton").IsEnabled,
                "Opening must immediately show preparation and block conflicting actions.");
            await Idle();
            Check(Control<Border>("TuringPreparationOverlay").Visibility == Visibility.Visible &&
                Control<ProgressBar>("TuringPreparationProgress").Value == 90,
                "Preparation must remain visible until the first frame is displayed.");
            await Display();
            Check(Shown() is { Step: > 0 } && Pending() is null, "Opening the window must prepare and show the first preset on the GPU.");
            Check(Control<Border>("TuringPreparationOverlay").Visibility == Visibility.Collapsed &&
                Control<Border>("ControlsHost").IsEnabled && Control<Button>("TuringPlayButton").IsEnabled,
                "Displaying the prepared frame must restore controls.");

            var interrupted = state.Clone();
            interrupted.Turing = interrupted.Turing with { Size = 32, WarmupSteps = 2000, Field = null, Live = null };
            window.LoadState(interrupted);
            var preparationWatch = Stopwatch.StartNew();
            while (Control<ProgressBar>("TuringPreparationProgress").Value == 0 && Field<bool>("_turingBusy") &&
                preparationWatch.ElapsedMilliseconds < 10000) await Task.Delay(1);
            Check(Control<ProgressBar>("TuringPreparationProgress").Value is > 0 and < 90,
                "Warmup must report actual step progress while computation continues.");
            Click("TuringStopPreparationButton");
            Check(Control<TextBlock>("TuringPreparationText").Text == "Завершаем текущий шаг…" &&
                !Control<Button>("TuringStopPreparationButton").IsEnabled,
                "Stopping preparation must explain that the current step is finishing.");
            await Settle();
            var partial = window.CaptureState("partial").Turing.Field!;
            Check(partial.Step is > 0 and < 2000 && !Field<bool>("_turingRunning") &&
                Control<Border>("TuringPreparationOverlay").Visibility == Visibility.Collapsed,
                "Stopping must display the partial field on pause and close preparation.");
            Click("TuringStepButton"); await Settle();
            Check(window.CaptureState("continued").Turing.Field!.Step == partial.Step + (int)Control<Slider>("TuringSpeedSlider").Value &&
                Control<Border>("TuringPreparationOverlay").Visibility == Visibility.Collapsed,
                "A stopped preparation must remain usable; ordinary steps must not open the overlay.");

            window.LoadState(interrupted);
            Click("TuringStopPreparationButton");
            window.LoadState(state);
            Check(Shown() is null && Control<Border>("TuringPreparationOverlay").Visibility == Visibility.Visible &&
                Control<TextBlock>("TuringPreparationText").Text == "Восстанавливаем поле…" &&
                Control<Button>("TuringStopPreparationButton").IsEnabled,
                "A new load during cancellation must replace the old preparation and discard its live frame.");
            await Settle();
            var loaded = window.CaptureState("ui").Turing;
            Check(Shown() is not null && loaded.Live is null && MaxFieldDifference(loaded.Field!, state.Turing.Field!) == 0 &&
                loaded.Field!.Step == state.Turing.Field!.Step && loaded.SameRule(state.Turing), "WPF loading must preserve the exact field and rule on pause.");
            Check(Control<FrameworkElement>("TuringPanel").Visibility == Visibility.Visible &&
                Control<FrameworkElement>("GrayPanel").Visibility == Visibility.Collapsed &&
                Control<FrameworkElement>("IterationsBox").Visibility == Visibility.Collapsed &&
                Control<FrameworkElement>("BailoutPanel").Visibility == Visibility.Collapsed,
                "The window must show pattern controls rather than unrelated fractal fields.");

            Control<Slider>("TuringLevelSlider").Value = .42;
            Control<CheckBox>("TuringSheetBox").IsChecked = true;
            var viewed = window.CaptureState("ui").Turing;
            Check(viewed.Level == .42 && viewed.SheetThickness > 0 && viewed.Field!.Step == state.Turing.Field.Step,
                "View changes must preserve the simulation time and field.");
            Control<CheckBox>("TuringSheetBox").IsChecked = false;
            Control<TextBox>("TuringSeedBox").Text = "invalid";
            Control<TextBox>("TuringSizeBox").Text = "invalid";
            Check(window.CaptureState("draft").Turing.Seed == state.Turing.Seed, "Unapplied drafts must not corrupt saves or frames.");
            Control<TextBox>("TuringSizeBox").Text = state.Turing.Size.ToString();
            var root = (FrameworkElement)window.Content;
            root.Measure(new Size(1180, 800)); root.Arrange(new Rect(0, 0, 1180, 800)); root.UpdateLayout();

            var beforeStep = window.CaptureState("before").Turing.Field!;
            Click("TuringStepButton");
            await Idle();
            Check(Pending() is not null && window.CaptureState("during").Turing.Field!.Step == beforeStep.Step,
                "Saves must retain the displayed field until the computed frame is shown.");
            await Display();
            int speed = (int)Control<Slider>("TuringSpeedSlider").Value;
            Check(window.CaptureState("after").Turing.Field!.Step == beforeStep.Step + speed && !Field<bool>("_turingRunning"),
                "One frame must advance exactly the selected number of steps and stay paused.");
            Click("TuringPlayButton");
            Invoke("PauseTuring"); await Idle();
            Check(!Field<bool>("_turingRunning"), "Pausing during computation must stop the live simulation.");
            if (Pending() is not null) await Display();
            Check(Pending() is null, "Steps submitted before a pause must be published and shown.");

            // A rule change keeps the field and time and acts from the next step.
            var beforeRule = window.CaptureState("rule-before").Turing;
            Control<ComboBox>("TuringSymmetryBox").SelectedIndex = (int)Turing3DSymmetry.Octahedral;
            Control<CheckBox>("TuringMirrorBox").IsChecked = true;
            Control<ComboBox>("TuringBoundaryBox").SelectedIndex = (int)TuringBoundary.Reflect;
            var afterRule = window.CaptureState("rule-after").Turing;
            Check(afterRule.Symmetry == Turing3DSymmetry.Octahedral && afterRule.Mirror && MaxFieldDifference(afterRule.Field!, beforeRule.Field!) == 0,
                "Changing the rule must keep the shown field and time.");
            Click("TuringStepButton"); await Settle();
            var symmetric = window.CaptureState("rule-step").Turing;
            Check(symmetric.Field!.Step == beforeRule.Field!.Step + speed &&
                SymmetryError(symmetric.Field, Turing3DSymmetryGroup.Build(Turing3DSymmetry.Octahedral, 1, true)) < 2e-5,
                "The next steps must follow the new symmetry.");

            // The brush edits the GPU field without advancing time.
            var beforeBrush = window.CaptureState("brush-before").Turing.Field!;
            Click("TuringBrushButton"); await Settle();
            var afterBrush = window.CaptureState("brush-after").Turing.Field!;
            Check(afterBrush.Step == beforeBrush.Step && MaxFieldDifference(afterBrush, beforeBrush) > .01,
                "The central brush must change the displayed field without advancing time.");

            // Grid transfer keeps the time.
            Control<TextBox>("TuringSizeBox").Text = "64";
            typeof(Fractal3DWindow).GetMethod("TuringResize_OnClick", flags)!.Invoke(window, [window, new RoutedEventArgs()]);
            Check(Control<Border>("TuringPreparationOverlay").Visibility == Visibility.Visible &&
                Control<TextBlock>("TuringPreparationText").Text == "Переносим поле…",
                "Grid transfer must show preparation too.");
            await Settle();
            var moved = window.CaptureState("resized").Turing;
            Check(moved.Size == 64 && moved.Field!.Size == 64 && moved.Field.Step == afterBrush.Step, "Grid transfer must keep the time.");

            var saved = window.CaptureState("Saved live frame");
            store.Save(saved);
            var reloaded = store.Load().Single(s => s.SaveName == "Saved live frame");
            Check(MaxFieldDifference(reloaded.Turing.Field!, moved.Field) == 0 && reloaded.Turing.Field!.Step == moved.Field.Step &&
                reloaded.Turing.SameRule(moved), "Saving from the window must store the exact displayed GPU field and rule.");

            window.LoadState(state); await Settle();
            if (output is not null)
            {
                window.CanvasImage.Source = await renderer.RenderAsync(state, 720, 640, null, CancellationToken.None);
                foreach (var size in new[] { new Size(1180, 800), new Size(960, 600) })
                {
                    root.Measure(size); root.Arrange(new Rect(new Point(), size)); root.UpdateLayout();
                    var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
                    SaveTuring3DPng(bitmap, Path.Combine(output, $"window-{size.Width}.png"));
                }
                window.LoadState(interrupted);
                foreach (var size in new[] { new Size(1180, 800), new Size(960, 600) })
                {
                    root.Measure(size); root.Arrange(new Rect(new Point(), size)); root.UpdateLayout();
                    var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
                    SaveTuring3DPng(bitmap, Path.Combine(output, $"preparation-{size.Width}.png"));
                }
                Click("TuringStopPreparationButton"); await Settle();
            }
        }
        finally { window.Close(); }
    }

    private static byte[] Pixels3D(BitmapSource bitmap)
    {
        var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(bytes, bitmap.PixelWidth * 4, 0);
        return bytes;
    }

    private static void SaveTuring3DPng(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }

    private sealed class ByteSequenceComparer : IEqualityComparer<byte[]>
    {
        public bool Equals(byte[]? x, byte[]? y) => x is not null && y is not null && x.AsSpan().SequenceEqual(y);
        public int GetHashCode(byte[] obj) { var hash = new HashCode(); hash.AddBytes(obj); return hash.ToHashCode(); }
    }

    private static double MaxFieldDifference(Turing3DField a, Turing3DField b) =>
        a.Field.ToArray().Zip(b.Field.ToArray()).Max(p => Math.Abs((double)p.First - p.Second));

    /// <summary>Largest difference between a cell and its images; the groups used here map the grid onto itself.</summary>
    private static double SymmetryError(Turing3DField field, IReadOnlyList<double[]> group)
    {
        int n = field.Size; double c = (n - 1) * .5, error = 0;
        var values = field.Field.ToArray(); var scales = field.ScaleMap.ToArray();
        var random = new Random(3);
        for (int sample = 0; sample < 4000; sample++)
        {
            int x = random.Next(n), y = random.Next(n), z = random.Next(n);
            double px = x - c, py = y - c, pz = z - c;
            foreach (var g in group)
            {
                int qx = (int)Math.Round(g[0] * px + g[1] * py + g[2] * pz + c);
                int qy = (int)Math.Round(g[3] * px + g[4] * py + g[5] * pz + c);
                int qz = (int)Math.Round(g[6] * px + g[7] * py + g[8] * pz + c);
                int a = (z * n + y) * n + x, b = (qz * n + qy) * n + qx;
                error = Math.Max(error, Math.Abs(values[a] - values[b]));
                if (scales[a] != scales[b]) error = Math.Max(error, 1);
            }
        }
        return error;
    }

    /// <summary>Test oracle: every cell samples (trilinearly, mirrored edges) the orbit image furthest along the domain direction.</summary>
    private static double[] RepresentativeSample(float[] source, int n, IReadOnlyList<double[]> group)
    {
        var (dx, dy, dz) = Turing3DSymmetryGroup.DomainDirection;
        double c = (n - 1) * .5;
        var result = new double[source.Length];
        for (int z = 0; z < n; z++) for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
        {
            double px = x - c, py = y - c, pz = z - c, best = double.NegativeInfinity; double[] chosen = group[0];
            foreach (var g in group)
            {
                double s = (g[0] * px + g[1] * py + g[2] * pz) * dx + (g[3] * px + g[4] * py + g[5] * pz) * dy + (g[6] * px + g[7] * py + g[8] * pz) * dz;
                if (s > best) { best = s; chosen = g; }
            }
            result[(z * n + y) * n + x] = Trilinear(source, n,
                chosen[0] * px + chosen[1] * py + chosen[2] * pz + c,
                chosen[3] * px + chosen[4] * py + chosen[5] * pz + c,
                chosen[6] * px + chosen[7] * py + chosen[8] * pz + c, TuringBoundary.Reflect);
        }
        return result;
    }

    private static double Trilinear(float[] field, int n, double x, double y, double z, TuringBoundary boundary)
    {
        int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y), iz = (int)Math.Floor(z);
        double tx = x - ix, ty = y - iy, tz = z - iz;
        double V(int a, int b, int c) => field[(Fold(c, n, boundary) * n + Fold(b, n, boundary)) * n + Fold(a, n, boundary)];
        double L(double a, double b, double t) => a + (b - a) * t;
        return L(L(L(V(ix, iy, iz), V(ix + 1, iy, iz), tx), L(V(ix, iy + 1, iz), V(ix + 1, iy + 1, iz), tx), ty),
                 L(L(V(ix, iy, iz + 1), V(ix + 1, iy, iz + 1), tx), L(V(ix, iy + 1, iz + 1), V(ix + 1, iy + 1, iz + 1), tx), ty), tz);
    }

    private static int Fold(int p, int n, TuringBoundary boundary)
    {
        if ((uint)p < (uint)n) return p;
        if (boundary == TuringBoundary.Wrap) return (p % n + n) % n;
        int period = 2 * n, q = (p % period + period) % period;
        return q < n ? q : period - 1 - q;
    }

    /// <summary>Test oracle only: the multiscale step with double-precision sums along each axis.</summary>
    private static (double[] Field, byte[] Scales) TuringReferenceStep(float[] field, int n, Turing3DSettings rule)
    {
        var current = field.Select(v => (double)v).ToArray();
        var best = Enumerable.Repeat(double.PositiveInfinity, current.Length).ToArray();
        var delta = new double[current.Length]; var scales = new byte[current.Length];
        foreach (var layer in rule.EffectiveLayers(n))
        {
            double[] activator = SmoothReference(current, n, layer.Radius, rule.Boundary);
            double[] inhibitor = SmoothReference(current, n, layer.Inhibitor, rule.Boundary);
            double[] variation = SmoothReference(activator.Zip(inhibitor, (a, b) => Math.Abs(a - b)).ToArray(), n, layer.Radius, rule.Boundary);
            for (int i = 0; i < current.Length; i++)
            {
                if (variation[i] >= best[i]) continue;
                best[i] = variation[i]; scales[i] = (byte)layer.Layer;
                delta[i] = activator[i] > inhibitor[i] ? layer.Amount : -layer.Amount;
            }
        }
        return (Normalize(current.Select((v, i) => v + delta[i]).ToArray()), scales);
    }

    private static double[] SmoothReference(double[] source, int n, int radius, TuringBoundary boundary)
    {
        double[] current = source;
        foreach (int width in TuringSimulation.GaussianBoxWidths(radius * .75))
        {
            int box = width / 2; if (box == 0) continue;
            for (int axis = 0; axis < 3; axis++)
            {
                var next = new double[current.Length];
                for (int a = 0; a < n; a++) for (int b = 0; b < n; b++) for (int t = 0; t < n; t++)
                {
                    double sum = 0;
                    for (int k = -box; k <= box; k++)
                    {
                        int s = Fold(t + k, n, boundary);
                        sum += current[axis == 0 ? (b * n + a) * n + s : axis == 1 ? (b * n + s) * n + a : (s * n + b) * n + a];
                    }
                    next[axis == 0 ? (b * n + a) * n + t : axis == 1 ? (b * n + t) * n + a : (t * n + b) * n + a] = sum / (2 * box + 1);
                }
                current = next;
            }
        }
        return current;
    }

    private static double[] Normalize(double[] values)
    {
        double min = values.Min(), max = values.Max(), range = max - min;
        return values.Select(v => range < 1e-12 ? 0 : Math.Clamp((v - min) / range * 2 - 1, -1, 1)).ToArray();
    }

    private static bool IsIdentity(double[] m) => SameMatrix(m, [1, 0, 0, 0, 1, 0, 0, 0, 1]);

    private static bool IsOrthogonal(double[] m) => SameMatrix(Multiply(m, [m[0], m[3], m[6], m[1], m[4], m[7], m[2], m[5], m[8]]), [1, 0, 0, 0, 1, 0, 0, 0, 1]);

    private static bool SameMatrix(double[] a, double[] b) => a.Zip(b).All(p => Math.Abs(p.First - p.Second) < 1e-9);

    private static double[] Multiply(double[] a, double[] b)
    {
        var r = new double[9];
        for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) r[i * 3 + j] = a[i * 3] * b[j] + a[i * 3 + 1] * b[3 + j] + a[i * 3 + 2] * b[6 + j];
        return r;
    }
}
