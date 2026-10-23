using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

/// <summary>Атлас Жюлиа горящего корабля: три компоненты C и целая степень, формула фиксирована.</summary>
public sealed class BurningShipJuliaAtlasWindow(Fractal3DState source)
    : Julia3DShapeAtlasWindow(source, Fractal3DKind.BurningShipJulia,
        "Формула: " + Fractal3DCatalog.BurningShipFormulaName(source.BurningShipFormula) + ".");
