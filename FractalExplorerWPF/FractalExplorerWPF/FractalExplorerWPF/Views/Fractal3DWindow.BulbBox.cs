using System.Windows;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class Fractal3DWindow
{
    private void UpdateHybridLayout()
    {
        if (Kind != Fractal3DKind.BulbBoxHybrid) return;
        bool custom = HybridEditor.IsCustom;
        HybridClassicPanel.Visibility = BoxPanel.Visibility = PowerPanel.Visibility =
            custom ? Visibility.Collapsed : Visibility.Visible;
    }

    private void HybridEditorChanged(object sender, EventArgs e)
    {
        if (_updatingUi || Kind != Fractal3DKind.BulbBoxHybrid) return;
        UpdateHybridLayout();
        _renderCts?.Cancel();
        ScheduleRender();
    }
}
