using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class Fractal3DWindow
{
    private void Hopf_OnChanged(object? sender, EventArgs e)
    {
        if (!_updatingUi && Kind == Fractal3DKind.Hopf)
            ScheduleRender(immediate: HopfEditor.IsPlaying);
    }
}
