using System.Windows;
using System.Windows.Controls;

namespace FractalExplorerWPF.Controls;

public partial class Phoenix3DExplorer : UserControl
{
    public event EventHandler? AtlasRequested;
    public event EventHandler? VariationRequested;
    public event EventHandler? UndoRequested;

    public Phoenix3DExplorer()
    {
        InitializeComponent();
        NumericInput.Real(this, RadiusBox, "Максимальное изменение компоненты", .001, .5);
    }

    public (double Radius, int Target) CaptureVariation()
    {
        NumericInput.CommitAll(this);
        return (PanelFields.ReadDouble(RadiusBox, "Максимальное изменение компоненты", .001, .5), TargetBox.SelectedIndex);
    }

    public void SetUndoAvailable(bool available) => UndoButton.IsEnabled = available;
    private void Atlas_OnClick(object sender, RoutedEventArgs e) => AtlasRequested?.Invoke(this, EventArgs.Empty);
    private void Variation_OnClick(object sender, RoutedEventArgs e) => VariationRequested?.Invoke(this, EventArgs.Empty);
    private void Undo_OnClick(object sender, RoutedEventArgs e) => UndoRequested?.Invoke(this, EventArgs.Empty);
}
