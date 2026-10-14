using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyTranscendentalRaysAsync(string[] args)
    {
        string directory = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(AppContext.BaseDirectory, "VerificationData", "TranscendentalRays", "Frames");
        Directory.CreateDirectory(directory);
        foreach (var kind in Enum.GetValues<TranscendentalJuliaKind>())
        {
            var state = TranscendentalJuliaCatalog.CreateDefault(kind);
            foreach (var lambda in new Complex[] { new(state.LambdaReal, 0), new(.4, .2), new(-.5, 0), new(0, 1.166667) })
            {
                state.LambdaReal = lambda.Real; state.LambdaImaginary = lambda.Imaginary;
                var rays = new TranscendentalJuliaRays(state, 320, 240);
                var map = new TranscendentalMap(kind, lambda);
                foreach (long[] address in new long[][] { [], [0], [1], [2, -1], [-1, 2, 0] })
                    foreach (double t in new[] { .02, .2, 1.0, 3.0, 5.0 })
                    {
                        Complex z = rays.Evaluate(t, address);
                        Complex shifted = rays.Evaluate(TranscendentalJuliaRays.PotentialStep(t), address.Skip(1).ToArray());
                        Complex mapped = map.Evaluate(z);
                        double error = (mapped - shifted).Magnitude / Math.Max(1, shifted.Magnitude);
                        Check(TranscendentalJuliaDynamics.IsFinite(z) && error < 1e-8,
                            $"Ray functional equation {kind}, lambda={lambda}, t={t}, address={string.Join(',',address)}: error {error:G8}.");
                    }
            }
            state = TranscendentalJuliaCatalog.CreateDefault(kind);
            var realRays = new TranscendentalJuliaRays(state, 320, 240);
            Complex near = realRays.Evaluate(.02, []), far = realRays.Evaluate(2, []);
            if (kind == TranscendentalJuliaKind.Exponential)
                Check(Math.Abs(near.Imaginary) < 1e-12 && near.Real > 1 && far.Real > near.Real,
                    "Real exponential base ray must run along the positive real axis outside its repelling endpoint.");
            else Check(Math.Abs(near.Real) < 1e-12 && near.Imaginary > 2 && far.Imaginary > near.Imaginary,
                    "Real sine base ray must run along the positive imaginary axis.");
            var realMap = new TranscendentalMap(kind, new(state.LambdaReal, 0));
            Complex w = kind == TranscendentalJuliaKind.Exponential ? new(-2, .001) : new(2 * state.LambdaReal, .001);
            Complex anchor = realMap.Preimage(w, 0);
            Complex continued = realRays.ContinuePreimage(new(w.Real, -.001), anchor, 0);
            Check((continued - anchor).Magnitude < .01 &&
                (realMap.Evaluate(continued) - new Complex(w.Real,-.001)).Magnitude < 1e-10,
                "Continuation across an artificial principal branch cut must keep the same analytic sheet.");
            Check(realRays.CrossesSingularValue(kind == TranscendentalJuliaKind.Exponential ? -Complex.One : Complex.Zero,
                    kind == TranscendentalJuliaKind.Exponential ? Complex.One : new Complex(2 * state.LambdaReal,0)),
                "A segment crossing the logarithmic pole or a sine critical value must be split.");

            var frame = new byte[320 * 240 * 4];
            var renderer = new TranscendentalJuliaRenderer(state);
            renderer.Render(frame, 320, 240, 1280, 1, CancellationToken.None);
            Check(renderer.RayCount >= 20 && renderer.CompletedDepth >= 3 && renderer.SampleCount <= state.PointBudget,
                $"Rays must survive multiple pullbacks within the budget: {renderer.Description}");
            SaveTranscendentalPng(Path.Combine(directory, $"{kind}-rays.png"), frame, 320, 240);
            var points = state.Clone(); points.Drawing = TranscendentalJuliaDrawing.Points;
            var pointFrame = new byte[frame.Length]; new TranscendentalJuliaRenderer(points).Render(pointFrame,320,240,1280,1,CancellationToken.None);
            Check(!frame.SequenceEqual(pointFrame), "Curves must differ from the previous point sampling.");
            var both = state.Clone(); both.Drawing = TranscendentalJuliaDrawing.RaysAndPoints;
            var bothFrame = new byte[frame.Length]; var bothRenderer = new TranscendentalJuliaRenderer(both);
            bothRenderer.Render(bothFrame,320,240,1280,1,CancellationToken.None);
            Check(bothRenderer.RayCount > 0 && bothRenderer.SeedCount > renderer.SeedCount &&
                bothRenderer.SampleCount <= both.PointBudget && !bothFrame.SequenceEqual(frame),
                "Combined ray/point drawing must allocate a budget to both layers.");
            var fine = state.Clone(); fine.RayAccuracy = .1;
            var fineRenderer = new TranscendentalJuliaRenderer(fine); var fineFrame = new byte[frame.Length];
            fineRenderer.Render(fineFrame,320,240,1280,1,CancellationToken.None);
            Check(fineRenderer.SampleCount >= renderer.SampleCount, "Tighter screen error must refine the curves.");

            var param = state.Clone(); param.Plane = TranscendentalJuliaPlane.Parameter;
            param.ParameterCenterX = .1; param.ParameterCenterY = .2; param.ParameterZoom = 2.5;
            var paramFrame = new byte[160 * 120 * 4]; var paramRenderer = new TranscendentalJuliaRenderer(param);
            paramRenderer.Render(paramFrame,160,120,640,2,CancellationToken.None);
            Check(paramRenderer.RayCount == 0 && paramRenderer.Attractors.Count == 0 &&
                paramFrame.Where((_,i)=>i%4!=3).Distinct().Count()>20, "Lambda maps must render periods and outcomes without preparing a Julia tree.");
            SaveTranscendentalPng(Path.Combine(directory,$"{kind}-lambda.png"),paramFrame,160,120);
            var repeated = new byte[paramFrame.Length]; new TranscendentalJuliaRenderer(param).Render(repeated,160,120,640,1,CancellationToken.None);
            Check(repeated.SequenceEqual(paramFrame), "Parameter maps must be independent of threads.");
            var tiled = new TranscendentalJuliaRenderer(param); tiled.Prepare(160,120,CancellationToken.None);
            var tiledFrame = new byte[paramFrame.Length];
            foreach (var tile in MandelbrotTileScheduler.Create(160,120,16,TileSchedulingStrategy.Classic))
            {
                var tilePixels=tiled.RenderTile(tile,CancellationToken.None);
                for(int y=0;y<tile.Height;y++) Buffer.BlockCopy(tilePixels,y*tile.Width*4,tiledFrame,((tile.Y+y)*160+tile.X)*4,tile.Width*4);
            }
            Check(tiledFrame.SequenceEqual(paramFrame), "Lambda tiles and export must match exactly.");
            EnsureThemeStyles();
            var window = new TranscendentalJuliaWindow(kind);
            try
            {
                window.LoadState(param);
                var captured=window.CaptureState(param.SaveName); captured.Timestamp=param.Timestamp;
                Check(JsonSerializer.Serialize(captured,JsonOptionsFactory.Create())==JsonSerializer.Serialize(param,JsonOptionsFactory.Create()),
                    "Save/load must preserve plane, both viewports and all ray quality settings.");
                var mapViewport=(FractalExplorerWPF.Controls.DoubleDeepZoomViewport)typeof(TranscendentalJuliaWindow)
                    .GetField("_parameterViewport",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
                mapViewport.BeginPan(new System.Windows.Point(10,10));
                window.ExploreParameter(.25,.1);
                Check(!mapViewport.EndPan(),"Switching from the map must end an active drag on the old viewport.");
                var dynamic=window.CaptureState("selected");
                Check(dynamic.Plane==TranscendentalJuliaPlane.Dynamic && dynamic.LambdaReal==.25 && dynamic.LambdaImaginary==.1 &&
                    dynamic.ParameterCenterX==param.ParameterCenterX && dynamic.ParameterZoom==param.ParameterZoom,
                    "Exploring lambda must open the chosen dynamical plane and preserve the map view.");
                ((ComboBox)window.FindName("PlaneBox")).SelectedIndex=1;
                Check(window.CaptureState("back").Plane==TranscendentalJuliaPlane.Parameter,
                    "The lambda map must be reachable again without losing its viewport.");
                var preview=await window.RenderStatePreviewAsync(param,96,72,CancellationToken.None);
                Check(preview.PixelWidth==96 && preview.IsFrozen,"Saved lambda previews must use the parameter renderer.");
            }
            finally { window.Close(); }
        }
        var exp=TranscendentalParameterMap.Analyze(TranscendentalJuliaKind.Exponential,new(.3,0),300,16,1e-7);
        Check(exp.AllAttracting && exp.First.Period==1 && exp.Second is null,"Exponential map must follow singular value zero.");
        var sin=TranscendentalParameterMap.Analyze(TranscendentalJuliaKind.Sine,new(0,1.166667),1000,16,1e-7);
        Check(sin.AllAttracting && sin.First.Period==4 && sin.Second!.Value.Period==4 &&
            (sin.First.Last+sin.Second.Value.Last).Magnitude<1e-9,"Sine map must independently follow both singular values and their period-four cycles.");
        var repelling=TranscendentalParameterMap.Analyze(TranscendentalJuliaKind.Exponential,new(0,2*Math.PI),300,16,1e-7);
        Check(!repelling.AllAttracting,"A singular orbit landing on a repeller must not be labelled attracting.");
        var legacy=JsonSerializer.Deserialize<TranscendentalJuliaState>("{\"LambdaReal\":0.3,\"Kind\":0}",JsonOptionsFactory.Create())!;
        Check(legacy.Drawing==TranscendentalJuliaDrawing.Points && legacy.Plane==TranscendentalJuliaPlane.Dynamic,
            "Old JSON must retain point rendering and the dynamical plane.");
        var criticalState=TranscendentalJuliaCatalog.CreateDefault(TranscendentalJuliaKind.Sine);
        criticalState.LambdaReal=Math.PI; criticalState.PointBudget=100000; criticalState.InverseDepth=6;
        var criticalBuilder=new TranscendentalJuliaRays(criticalState,160,120);
        var criticalParts=criticalBuilder.Build(CancellationToken.None);
        Check(criticalParts.Count>0 && criticalBuilder.SplitCount>0 && criticalBuilder.Work<=criticalState.PointBudget,
            "Sine lambda=pi has singular values mapping to the repeller 0: actual ray tracing must separate critical branches within budget.");
        using var cancellation=new CancellationTokenSource(); cancellation.Cancel();
        bool cancelled=false;
        try { new TranscendentalJuliaRays(legacy,320,240).Build(cancellation.Token); } catch(OperationCanceledException){cancelled=true;}
        Check(cancelled,"Ray tracing must honor cancellation.");
        Console.WriteLine($"PASS (transcendental-rays): functional equations, analytic continuation, critical splits, screen refinement, lambda orbits, deterministic tiles, legacy JSON, dual viewports and navigation. PNG: {directory}");
    }
}
