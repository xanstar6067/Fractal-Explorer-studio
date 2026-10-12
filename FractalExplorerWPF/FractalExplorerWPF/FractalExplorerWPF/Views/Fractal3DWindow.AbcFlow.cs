using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class Fractal3DWindow
{
    private void AbcFlow_OnChanged(object? sender, EventArgs e)
    {
        if (!_updatingUi && Kind == Fractal3DKind.AbcFlow)
            ScheduleRender(immediate: AbcFlowEditor.IsPlaying);
    }
}
