using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering;

public static class PhoenixParameterAtlasRenderer
{
    public const int PreviewIterations = 250;
    public static byte[] Render(PhoenixState source, PhoenixAtlasParameters point, int width, int height, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var state = source.Clone();
        point.ApplyTo(state);
        state.PlaneMode = PhoenixPlaneMode.Julia;
        state.CenterX = state.CenterY = 0;
        state.CenterXExact = state.CenterYExact = null;
        state.Zoom = 1;
        state.Iterations = Math.Min(state.Iterations, PreviewIterations);
        var pixels = new byte[checked(width * height * 4)];
        PhoenixRenderer.Render(state, pixels, width, height, width * 4, 1, token);
        token.ThrowIfCancellationRequested();
        return pixels;
    }
}
