using System.Collections.Concurrent;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorer.Engines;
using FractalExplorer.Utilities.Coloring;
using FractalExplorer.Utilities.RenderUtilities;
using FractalExplorer.Utilities.SaveIO.ColorPalettes;
using FractalExplorerWPF.Models;
using DrawingColor = System.Drawing.Color;

namespace FractalExplorerWPF.Core.Rendering;

public static class DynamicSystemRenderer
{
    public static async Task<BitmapSource> RenderAsync(
        DynamicSystemState state, int width, int height, DynamicPalette? palette,
        CancellationToken token, IProgress<int>? progress = null,
        Action<MandelbrotRenderTile, byte[]>? tileReady = null, bool drawAxes = true,
        Action<MandelbrotRenderTile>? tileStarted = null, double dpiX = 96, double dpiY = 96,
        Action<BitmapSource>? frameReady = null)
    {
        int factor = Math.Clamp(state.SsaaFactor, 1, 4);
        int rw = checked(width * factor), rh = checked(height * factor);
        byte[] pixels = state.Kind == DynamicSystemKind.Lyapunov
            ? await RenderLyapunovAsync(state, rw, rh, palette, token, progress, tileReady, tileStarted)
            : await Task.Run(() => RenderOther(state, rw, rh, palette, token, progress, drawAxes,
                frameReady is null ? null : data =>
                {
                    token.ThrowIfCancellationRequested();
                    BitmapSource frame = BitmapSource.Create(rw, rh, dpiX, dpiY, PixelFormats.Bgra32, null, data, rw * 4);
                    frame.Freeze(); frameReady(frame);
                }));
        BitmapSource raw = BitmapSource.Create(rw, rh, dpiX, dpiY, PixelFormats.Bgra32, null, pixels, rw * 4);
        raw.Freeze();
        if (factor == 1 || token.IsCancellationRequested) return raw;
        BitmapSource resized = await Task.Run(() => BitmapResampler.ResizeLanczos3(raw, width, height, token));
        return WithDpi(resized, dpiX, dpiY);
    }

    private static async Task<byte[]> RenderLyapunovAsync(DynamicSystemState s, int width, int height, DynamicPalette? source,
        CancellationToken token, IProgress<int>? progress, Action<MandelbrotRenderTile, byte[]>? tileReady,
        Action<MandelbrotRenderTile>? tileStarted)
    {
        var engine = new FractalLyapunovEngine
        {
            AMin = (decimal)s.AMin, AMax = (decimal)s.AMax, BMin = (decimal)s.BMin, BMax = (decimal)s.BMax,
            Iterations = Math.Clamp(s.Iterations, 1, FractalLyapunovEngine.MaxStableDepth),
            TransientIterations = Math.Clamp(s.TransientIterations, 0, FractalLyapunovEngine.MaxStableDepth),
            Pattern = s.Pattern, ColorPalette = ToLyapunovPalette(source)
        };
        LyapunovColoringContext? context = await Task.Run(() => engine.PrepareColoringContext(width, height, token));
        IReadOnlyList<MandelbrotRenderTile> tiles = MandelbrotTileScheduler.Create(width, height, 16 * Math.Clamp(s.SsaaFactor, 1, 4), TileSchedulingStrategy.Classic);
        byte[] output = new byte[checked(width * height * 4)]; int done = 0;
        await Parallel.ForEachAsync(tiles, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, s.Threads) }, (tile, ignoredCancellationToken) =>
        {
            if (token.IsCancellationRequested) return ValueTask.CompletedTask;
            tileStarted?.Invoke(tile);
            byte[] data = engine.RenderSingleTile(new TileInfo(tile.X, tile.Y, tile.Width, tile.Height), width, height, out _, context, token);
            if (token.IsCancellationRequested) return ValueTask.CompletedTask;
            for (int y = 0; y < tile.Height; y++) Buffer.BlockCopy(data, y * tile.Width * 4, output, ((tile.Y + y) * width + tile.X) * 4, tile.Width * 4);
            tileReady?.Invoke(tile, data);
            progress?.Report(Interlocked.Increment(ref done) * 100 / tiles.Count);
            return ValueTask.CompletedTask;
        });
        return output;
    }

    private static byte[] RenderOther(DynamicSystemState s, int width, int height, DynamicPalette? palette,
        CancellationToken token, IProgress<int>? progress, bool drawAxes, Action<byte[]>? frameReady)
    {
        DrawingColor bg = ToDrawing(s.BackgroundColor), point = ToDrawing(s.FractalColor);
        return s.Kind switch
        {
            DynamicSystemKind.Lorenz => FractalLorenzEngine.RenderBuffer(width, height, (decimal)s.CenterX, (decimal)s.CenterY, (decimal)s.Zoom,
                new FractalLorenzEngine.RenderSettings { Sigma=(decimal)s.Sigma, Rho=(decimal)s.Rho, Beta=(decimal)s.Beta, Dt=(decimal)s.Dt, Steps=s.Steps, StartX=(decimal)s.StartX, StartY=(decimal)s.StartY, StartZ=(decimal)s.StartZ, Projection=EnumValue<FractalLorenzEngine.ProjectionMode>(s.ProjectionMode) }, token, progress, drawAxes, bg),
            DynamicSystemKind.Rossler => FractalRosslerEngine.RenderBuffer(width, height, (decimal)s.CenterX, (decimal)s.CenterY, (decimal)s.Zoom,
                new FractalRosslerEngine.RenderSettings { A=(decimal)s.A, B=(decimal)s.B, C=(decimal)s.C, Dt=(decimal)s.Dt, Steps=s.Steps, StartX=(decimal)s.StartX, StartY=(decimal)s.StartY, StartZ=(decimal)s.StartZ, Projection=EnumValue<FractalRosslerEngine.ProjectionMode>(s.ProjectionMode) }, token, progress, drawAxes, bg),
            DynamicSystemKind.LogisticMap => RenderLogistic(s, width, height, palette, token, progress, drawAxes, bg),
            DynamicSystemKind.Bifurcation => FractalBifurcationEngine.RenderBuffer(width, height, (decimal)s.CenterX, (decimal)s.CenterY, (decimal)s.Zoom,
                new FractalBifurcationEngine.RenderSettings { RMin=(decimal)s.RMin, RMax=(decimal)s.RMax, XMin=(decimal)s.XMin, XMax=(decimal)s.XMax, TransientIterations=s.TransientIterations, SamplesPerR=s.SamplesPerR, Iterations=s.Iterations }, token, progress, s.Threads, drawAxes, point, bg),
            DynamicSystemKind.Henon => FractalHenonEngine.RenderBuffer(width, height, (decimal)s.CenterX, (decimal)s.CenterY, (decimal)s.Zoom,
                new FractalHenonEngine.RenderSettings { A=(decimal)s.A, B=(decimal)s.B, X0=(decimal)s.X0, Y0=(decimal)s.Y0, Iterations=s.Iterations, DiscardIterations=s.DiscardIterations, Threads=s.Threads }, token, progress),
            DynamicSystemKind.Ikeda => FractalIkedaEngine.RenderBuffer(width, height, (decimal)s.CenterX, (decimal)s.CenterY, (decimal)s.Zoom,
                new FractalIkedaEngine.RenderSettings { U=(decimal)s.U, X0=(decimal)s.X0, Y0=(decimal)s.Y0, Iterations=s.Iterations, DiscardIterations=s.DiscardIterations, RangeXMin=(decimal)s.RangeXMin, RangeXMax=(decimal)s.RangeXMax, RangeYMin=(decimal)s.RangeYMin, RangeYMax=(decimal)s.RangeYMax, Threads=s.Threads }, token, progress),
            DynamicSystemKind.Attractors2D => Attractor2DRenderer.RenderBuffer(s, width, height, palette, token, progress),
            DynamicSystemKind.Popcorn => PopcornRenderer.RenderBuffer(s, width, height, palette, token, progress),
            DynamicSystemKind.Hopalong => HopalongRenderer.RenderBuffer(s, width, height, palette, token, progress, frameReady),
            _ => new byte[width * height * 4]
        };
    }

    private static byte[] RenderLogistic(DynamicSystemState s, int width, int height, DynamicPalette? palette, CancellationToken token, IProgress<int>? progress, bool axes, DrawingColor bg)
    {
        var settings = new FractalLogisticMapEngine.RenderSettings { Iterations=s.Iterations, TransientIterations=s.TransientIterations, R=(decimal)s.R, X0=(decimal)s.X0, BifurcationRMin=(decimal)s.BifurcationRMin, BifurcationRMax=(decimal)s.BifurcationRMax, BifurcationSamples=s.BifurcationSamples, BifurcationTransient=s.BifurcationTransient, BifurcationPlottedPoints=s.BifurcationPlottedPoints, CobwebSteps=s.CobwebSteps, PaletteColors=(palette?.Colors ?? []).Select(ToDrawing).ToList() };
        return s.VisualizationMode switch
        {
            "Bifurcation" => FractalLogisticMapEngine.RenderBifurcationBuffer(width,height,(decimal)s.CenterX,(decimal)s.CenterY,(decimal)s.Zoom,settings,token,progress,s.Threads,axes,bg),
            "Cobweb" => FractalLogisticMapEngine.RenderCobwebBuffer(width,height,settings,token,progress,bg),
            _ => FractalLogisticMapEngine.RenderOrbitBuffer(width,height,(decimal)s.CenterX,(decimal)s.CenterY,(decimal)s.Zoom,settings,token,progress,s.Threads,axes,bg)
        };
    }

    private static LyapunovColorPalette ToLyapunovPalette(DynamicPalette? p)
    {
        if (p is null) return LyapunovPaletteManager.CreateDefaultBuiltInPalette();
        Enum.TryParse(p.Mode, out LyapunovColoringMode mode);
        return new LyapunovColorPalette { Name=p.Name, Mode=mode, Colors=p.Colors.Select(ToDrawing).ToList(), ExponentRange=p.ExponentRange, ZeroBandWidth=p.ZeroBandWidth };
    }
    private static T EnumValue<T>(string value) where T : struct, Enum => Enum.TryParse(value, true, out T result) ? result : default;
    private static DrawingColor ToDrawing(System.Windows.Media.Color c) => DrawingColor.FromArgb(c.A,c.R,c.G,c.B);

    private static BitmapSource WithDpi(BitmapSource source, double dpiX, double dpiY)
    {
        int stride = checked((source.PixelWidth * source.Format.BitsPerPixel + 7) / 8);
        byte[] pixels = new byte[checked(stride * source.PixelHeight)];
        source.CopyPixels(pixels, stride, 0);
        BitmapSource result = BitmapSource.Create(source.PixelWidth, source.PixelHeight, dpiX, dpiY,
            source.Format, source.Palette, pixels, stride);
        result.Freeze();
        return result;
    }
}
