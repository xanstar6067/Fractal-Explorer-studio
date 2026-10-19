using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class MathematicalLaboratoryWindow
{
    private void InitializeNumericInput()
    {
        NumericInput.Integer(this, PrimaryBox, _definition.PrimaryLabel, _definition.PrimaryMinimum, _definition.PrimaryMaximum);
        NumericInput.Integer(this, SecondaryBox, _definition.SecondaryLabel, _definition.SecondaryMinimum, _definition.SecondaryMaximum);
        NumericInput.Integer(this, TertiaryBox, _definition.TertiaryLabel, _definition.TertiaryMinimum, _definition.TertiaryMaximum);
        NumericInput.Real(this, ParameterBox, _definition.ParameterLabel, _definition.ParameterMinimum, _definition.ParameterMaximum);
        NumericInput.Real(this, RotationBox, "Поворот", -1000000, 1000000);
    }
}
