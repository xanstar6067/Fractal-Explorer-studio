using System.Windows;
using System.Windows.Controls;

namespace FractalExplorerWPF.Controls;

public partial class ApollonianPackingEditor : UserControl
{
    public event EventHandler? AtlasRequested;
    public ApollonianPackingEditor() => InitializeComponent();
    private void AtlasClicked(object sender, RoutedEventArgs e) => AtlasRequested?.Invoke(this, EventArgs.Empty);
}
