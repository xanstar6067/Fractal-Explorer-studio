using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class Fractal3DWindow
{
    private void LSystem_OnChanged(object? sender, EventArgs e)
    {
        if (!_updatingUi && Kind == Fractal3DKind.LSystem3D)
            ScheduleRender(immediate: LSystemEditor.IsPlaying);
    }
}
