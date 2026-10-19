using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;
using FractalExplorer.Engines;

namespace FractalExplorerWPF.Views;

public partial class SerpinskyWindow
{
    private void InitializeNumericInput()
    {
        NumericInput.Real(this, ZoomBox, "Масштаб", .01, 10000000, appliedValue: () => _zoom);
        NumericInput.Integer(this, IterationsBox, "Итерации", () => SelectedRenderMode == SerpinskyRenderMode.Geometric ? 0 : 1000, () => SelectedRenderMode == SerpinskyRenderMode.Geometric ? 20 : int.MaxValue);
    }
}
