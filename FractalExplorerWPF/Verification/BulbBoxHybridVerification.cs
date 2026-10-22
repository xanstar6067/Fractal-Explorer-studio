using System.IO;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Infrastructure.Cloud;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;
using Vortice.D3DCompiler;
using Vortice.Direct3D11;

internal static partial class Program
{
    private static async Task VerifyBulbBoxHybridAsync(string[] args)
    {
        using var sandbox = DataSandbox.Create("bulbbox-hybrid");
        EnsureWindowThemeStyles();
        string output = ReadOption(args, "--out") ?? Path.Combine(AppPaths.DataRoot, "BulbBoxHybridVerification");
        Directory.CreateDirectory(output);
        var kind = Fractal3DKind.BulbBoxHybrid;
        var original = Fractal3DCatalog.CreateDefaultState(kind);
        Check(original.HybridSequence is null && !JsonSerializer.Serialize(original).Contains("HybridSequence"), "Legacy JSON must remain unchanged.");
        foreach (var invalid in new BulbBoxSequence[] {
            new(), new() { Stages = [new() { Operation = BulbBoxOperation.Translate }] },
            new() { Stages = [new() { Repeats = 0 }] }, new() { Stages = [new() { Power = double.NaN }] },
            new() { Stages = [new() { Operation = (BulbBoxOperation)99 }] },
            new() { Stages = Enumerable.Range(0, 9).Select(_ => new BulbBoxStage()).ToList() }
        })
        {
            bool rejected = false;
            try { invalid.Validate(); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "Invalid hybrid program accepted.");
        }
        var presets = Fractal3DCatalog.GetPresets(kind);
        Check(presets.Count == 8 && presets.Take(5).All(s => s.HybridSequence is null), "Five original presets must keep their formula.");
        var state = presets[5].Clone();
        state.HybridSequence!.Stages[0].Power = 3.123456789012345;
        var clone = state.Clone(); clone.HybridSequence!.Stages[0].Power = 7;
        Check(state.HybridSequence.Stages[0].Power != 7, "Snapshot shares mutable stages.");
        var store = new Fractal3DSaveStore(kind); state.SaveName = "Sequence"; store.Save(state);
        var loaded = store.Load().Single();
        Check(JsonSerializer.Serialize(loaded.HybridSequence) == JsonSerializer.Serialize(state.HybridSequence), "Sequence JSON lost stage parameters.");
        Check(CloudSaveRepository.ListLocal(out int unreadable).Single().JsonData.Contains("HybridSequence") && unreadable == 0, "Cloud payload lost the program.");

        await VerifyHybridMapAsync(presets);
        using var renderer = new Fractal3DRenderer();
        var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen())
        {
            for (int i = 0; i < presets.Count; i++)
            {
                var bitmap = await renderer.RenderAsync(presets[i], 240, 180, null, CancellationToken.None);
                WriteLSystemPng(Path.Combine(output, $"preset-{i}.png"), bitmap);
                dc.DrawImage(bitmap, new Rect(i % 4 * 240, i / 4 * 180, 240, 180));
                int hits = 0;
                foreach (int y in new[] { 60, 90, 120 })
                foreach (int x in new[] { 80, 120, 160 })
                    if (double.IsFinite(await renderer.ProbeDistanceAsync(presets[i], x, y, 240, 180, CancellationToken.None))) hits++;
                Check(hits > 0, $"Empty hybrid preset: {presets[i].SaveName}");
            }
        }
        var sheet = new RenderTargetBitmap(960, 360, 96, 96, PixelFormats.Pbgra32); sheet.Render(drawing);
        WriteLSystemPng(Path.Combine(output, "presets.png"), sheet);

        // Converting a classic sequence changes only the representation, not the fractal.
        foreach (var classic in presets.Take(5))
        {
            var converted = classic.Clone(); converted.HybridSequence = BulbBoxSequence.FromClassic(classic);
            classic.MaxSteps = converted.MaxSteps = 1024; classic.Detail = converted.Detail = .1;
            foreach (int x in new[] { 95, 120, 145 })
            {
                double before = await renderer.ProbeDistanceAsync(classic, x, 90, 240, 180, CancellationToken.None);
                double after = await renderer.ProbeDistanceAsync(converted, x, 90, 240, 180, CancellationToken.None);
                Check(double.IsNaN(before) ? double.IsNaN(after) : double.IsFinite(after) && Math.Abs(before - after) < .025,
                    $"Classic conversion changed surface: {classic.SaveName}, x={x}, {before} → {after}");
            }
        }
        foreach (var style in Enum.GetValues<Fractal3DShadingStyle>())
        {
            var styled = presets[5].Clone(); styled.ShadingStyle = style;
            await renderer.RenderAsync(styled, 64, 48, null, CancellationToken.None);
        }
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            var frame = await renderer.RenderPixelsAsync(presets[5], 240, 180, null, null, canceled.Token);
            Check(!frame.Completed, "Canceled render was published.");
        }
        VerifyHybridEditor(state);
        Console.WriteLine($"PASS (bulbbox-hybrid): independent double/GPU maps, legacy conversion, 8 presets, 9 styles, JSON/cloud, WPF editor and cancellation. PNG: {output}");
    }

    private static void VerifyHybridEditor(Fractal3DState state)
    {
        var window = new Fractal3DWindow(Fractal3DKind.BulbBoxHybrid);
        try
        {
            var editor = (BulbBoxHybridEditor)window.FindName("HybridEditor");
            T Named<T>(string name) where T : class => (T)editor.FindName(name);
            void Click(string name) => Named<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var enabled = Named<CheckBox>("EnabledBox");
            enabled.IsChecked = true;
            Check(window.CaptureState("converted").HybridSequence!.Stages.Count == 2, "Enabling must copy current classic parameters.");
            window.LoadState(state);
            Check(JsonSerializer.Serialize(window.CaptureState("loaded").HybridSequence) == JsonSerializer.Serialize(state.HybridSequence), "Editor roundtrip lost precision.");
            var power = Named<TextBox>("PowerBox");
            Check(NumericInput.IsRegistered(power), "Stage power must use NumericInput.");
            power.Text = "1e-";
            Check(power.Text == "1e-", "Typing a partial number must preserve the draft.");
            NumericInput.CommitAll(window);
            Check(NumericInput.IsValid(power), "Commit must restore an incomplete number.");
            power.Text = "99"; NumericInput.CommitAll(window);
            Check(window.CaptureState("clamped").HybridSequence!.Stages[0].Power == 12, "Stage power must clamp to its limit.");
            window.LoadState(state);
            Named<ComboBox>("AddKindBox").SelectedIndex = 3; Click("AddButton");
            Named<TextBox>("XBox").Text = "0.23456789012345678";
            var extended = window.CaptureState("add").HybridSequence!;
            Check(extended.Stages.Count == 4 && extended.Stages[3].X == .23456789012345678, "Adding or editing a shift lost precision.");
            Click("UpButton");
            Check(window.CaptureState("move").HybridSequence!.Stages[2].Operation == BulbBoxOperation.Translate, "Moving a stage failed.");
            Click("RemoveButton");
            Check(window.CaptureState("remove").HybridSequence!.Stages.Count == 3, "Removing a stage failed.");
            Click("UndoButton");
            Check(window.CaptureState("undo").HybridSequence!.Stages[2].Operation == BulbBoxOperation.Translate, "Undo must restore the removed stage.");
            enabled.IsChecked = false;
            Check(window.CaptureState("classic").HybridSequence is null, "Disabling must restore the classic formula.");
            enabled.IsChecked = true;
            Check(window.CaptureState("again").HybridSequence!.Stages.Count == 4, "Switching to classic must retain the draft chain.");
            var only = state.Clone(); only.HybridSequence = new() { Stages = [new()] };
            window.LoadState(only);
            Check(!Named<Button>("RemoveButton").IsEnabled, "The last nonlinear stage must not be removable.");
            Named<TextBox>("XBox").Text = "invalid";
            Check(window.CaptureState("inactive").HybridSequence!.Stages[0].Power == 3, "An inactive vector must not block the formula.");
        }
        finally { window.Close(); }
    }

    private static async Task VerifyHybridMapAsync(IReadOnlyList<Fractal3DState> presets)
    {
        // Evaluate the real production Map at a grid of points, without camera or ray-march error.
        string source = Fractal3DShader.Build(Fractal3DKind.BulbBoxHybrid) + """

        float4 HybridDiagnostic(PSInput input) : SV_TARGET
        {
            float3 p = float3(-1.65 + (input.position.x - .5) * .3,
                             -.95 + (input.position.y - .5) * .27, .23);
            float4 trap; return PackFloat(Map(p, trap));
        }
        """;
        using var renderer = new Fractal3DRenderer();
        var shaders = (Dictionary<Fractal3DKind, ID3D11PixelShader>)typeof(Fractal3DRenderer)
            .GetField("_pixelShaders", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renderer)!;
        shaders.Add(Fractal3DKind.BulbBoxHybrid, renderer.DeviceHost.Device.CreatePixelShader(
            Compiler.Compile(source, "HybridDiagnostic", "hybrid-diagnostic", "ps_5_0").Span));
        var samples = presets.Take(5).Select(s => s.Clone()).ToList();
        samples.AddRange(presets.Select(s => { var copy = s.Clone(); copy.HybridSequence ??= BulbBoxSequence.FromClassic(s); return copy; }));
        foreach (var inversion in Enum.GetValues<BoxInversionShape>())
        {
            var copy = presets[5].Clone();
            foreach (var stage in copy.HybridSequence!.Stages) { stage.Inversion = inversion; stage.Stretch = 1.4; stage.InversionPower = 6; stage.Mix = .2; }
            samples.Add(copy);
        }
        int count = 0;
        foreach (var sample in samples)
        {
            sample.Iterations = 4;
            var frame = await renderer.RenderPixelsAsync(sample, 12, 8, null, null, CancellationToken.None);
            for (int y = 0; y < 8; y++)
            for (int x = 0; x < 12; x++)
            {
                double expected = HybridReference(sample, new(-1.65 + x * .3, -.95 + y * .27, .23));
                float actual = BitConverter.ToSingle(frame.Buffer, (x + y * 12) * 4);
                Check(float.IsFinite(actual) && Math.Abs(actual - expected) <= .0002 + Math.Abs(expected) * .005,
                    $"Independent hybrid map: {sample.SaveName} [{x},{y}], expected {expected:G9}, GPU {actual:G9}");
                count++;
            }
        }
        Console.WriteLine($"PASS (hybrid map): {count} independent double/GPU samples.");
    }

    private readonly record struct HybridVector(double X, double Y, double Z)
    {
        public double Norm => Math.Sqrt(X * X + Y * Y + Z * Z);
        public static HybridVector operator +(HybridVector a, HybridVector b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static HybridVector operator *(HybridVector a, double b) => new(a.X * b, a.Y * b, a.Z * b);
    }

    private static double HybridReference(Fractal3DState state, HybridVector p)
    {
        var sequence = state.HybridSequence ?? BulbBoxSequence.FromClassic(state);
        var operations = sequence.Stages.SelectMany(s => Enumerable.Repeat(s, s.Repeats)).ToArray();
        var z = p; double dr = 1; int nonlinear = 0;
        for (int i = 0; i < (state.Iterations + 1) * operations.Length; i++)
        {
            var s = operations[i % operations.Length];
            if (s.Operation == BulbBoxOperation.Translate) { z += new HybridVector(s.X, s.Y, s.Z); continue; }
            if (s.Operation == BulbBoxOperation.Rotate)
            {
                double a = s.X * Math.PI / 180, b = s.Y * Math.PI / 180, c = s.Z * Math.PI / 180;
                z = new(z.X, z.Y * Math.Cos(a) - z.Z * Math.Sin(a), z.Y * Math.Sin(a) + z.Z * Math.Cos(a));
                z = new(z.X * Math.Cos(b) + z.Z * Math.Sin(b), z.Y, -z.X * Math.Sin(b) + z.Z * Math.Cos(b));
                z = new(z.X * Math.Cos(c) - z.Y * Math.Sin(c), z.X * Math.Sin(c) + z.Y * Math.Cos(c), z.Z);
                continue;
            }
            double radius = z.Norm;
            if (nonlinear >= state.Iterations || radius > state.Bailout) break;
            nonlinear++;
            double theta = Math.Acos(Math.Clamp(z.Z / Math.Max(radius, 1e-12), -1, 1)) * s.Power;
            double phi = Math.Atan2(z.Y, z.X) * s.Power;
            var bulb = new HybridVector(Math.Sin(theta) * Math.Cos(phi), Math.Sin(theta) * Math.Sin(phi), Math.Cos(theta)) * Math.Pow(radius, s.Power);
            var box = new HybridVector(2 * Math.Clamp(z.X, -s.FoldLimit, s.FoldLimit) - z.X,
                2 * Math.Clamp(z.Y, -s.FoldLimit, s.FoldLimit) - z.Y, 2 * Math.Clamp(z.Z, -s.FoldLimit, s.FoldLimit) - z.Z);
            double q, gradientNorm2;
            if (s.Inversion == BoxInversionShape.Sphere) { q = box.Norm; gradientNorm2 = 1; }
            else if (s.Inversion == BoxInversionShape.Cube) { q = Math.Max(Math.Abs(box.X), Math.Max(Math.Abs(box.Y), Math.Abs(box.Z))); gradientNorm2 = 1; }
            else
            {
                double n = s.Inversion == BoxInversionShape.Ellipsoid ? 2 : s.InversionPower;
                double ax = Math.Abs(box.X), ay = Math.Abs(box.Y), az = Math.Abs(box.Z / s.Stretch);
                q = Math.Pow(Math.Max(Math.Pow(ax, n) + Math.Pow(ay, n) + Math.Pow(az, n), 1e-12), 1 / n);
                gradientNorm2 = (Math.Pow(ax, 2 * n - 2) + Math.Pow(ay, 2 * n - 2) + Math.Pow(az, 2 * n - 2) / (s.Stretch * s.Stretch)) / Math.Pow(q, 2 * n - 2);
            }
            double q2 = q * q, min2 = s.MinRadius * s.MinRadius;
            double factor = q2 < min2 ? 1 / Math.Max(min2, 1e-6) : q2 < 1 ? 1 / Math.Max(q2, 1e-6) : 1;
            double gain = factor;
            if (s.Inversion != BoxInversionShape.Sphere && q2 >= min2 && q2 < 1)
            {
                double tangent = Math.Sqrt(Math.Max(box.Norm * box.Norm * gradientNorm2 / Math.Max(q2, 1e-12) - 1, 0));
                gain *= Math.Sqrt(1 + tangent * tangent) + tangent;
            }
            box *= s.Scale * factor;
            double weight = s.Operation == BulbBoxOperation.Bulb ? 1 - s.Mix : s.Mix;
            z = box * (1 - weight) + bulb * weight + p;
            dr = dr * (Math.Abs(s.Scale) * gain * (1 - weight) + s.Power * Math.Pow(Math.Max(radius, 1e-12), s.Power - 1) * weight) + 1;
        }
        double r = z.Norm;
        return Math.Min(.5 * Math.Log(Math.Max(r, 1.000001)), 1) * r / Math.Max(dr, 1e-9);
    }
}
