using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

/// <summary>Атлас динамических форм Жюлиабульба: Cx/Cy/Cz и вещественная степень.</summary>
public sealed class JuliabulbAtlasWindow(Fractal3DState source)
    : Julia3DShapeAtlasWindow(source, Fractal3DKind.Juliabulb);
