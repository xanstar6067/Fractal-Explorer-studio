using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering;

public static class JuliaParameterAtlasRenderer
{
    public const int PreviewIterations = 250;

    public static byte[] Render(MandelbrotState source, decimal real, decimal imaginary,
        int width, int height, CancellationToken token)
    {
        if (source.Variant != MandelbrotVariant.Julia)
            throw new ArgumentException("Опытный атлас поддерживает только классическое Жюлиа.", nameof(source));
        token.ThrowIfCancellationRequested();
        var state = source.Clone();
        // Сравниваем полные формы, независимо от глубокого зума основного окна.
        state.CenterX = state.CenterY = 0;
        state.CenterXExact = state.CenterYExact = null;
        state.Zoom = 0.8;
        state.Iterations = Math.Min(source.Iterations, PreviewIterations);
        state.Threads = 1;
        state.JuliaCReal = real;
        state.JuliaCImaginary = imaginary;
        var pixels = new byte[checked(width * height * 4)];
        MandelbrotFamilyRenderer.Render(state, pixels, width, height, width * 4, token);
        token.ThrowIfCancellationRequested();
        return pixels;
    }
}
