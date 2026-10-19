using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class TuringWindow
{
    private void InitializeNumericInput()
    {
        NumericInput.Integer(this, GridSizeBox, "Размер сетки", 32, TuringState.MaxGridSize);
        NumericInput.Integer(this, FrameWidthBox, "Ширина кадра", 32, 8192);
        NumericInput.Integer(this, FrameHeightBox, "Высота кадра", 32, 8192);
        NumericInput.Integer(this, SeedBox, "Seed", int.MinValue, int.MaxValue);
        NumericInput.Pair(this, FrameWidthBox, FrameHeightBox, (w, h) => (long)int.Parse(w) * int.Parse(h) <= 16777216, "Не более 16 млн пикселей в кадре.");
        NumericInput.Include(this, ReactionEditor);
    }
}
