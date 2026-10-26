using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class Fractal3DWindow
{
    private void AbcFlowAtlas_OnClick(object? sender, EventArgs e)
    {
        if (Kind == Fractal3DKind.AbcFlow) OpenShapeAtlas(CreateAbcFlowAtlas);
    }

    private AbcFlowAtlasWindow CreateAbcFlowAtlas()
    {
        AbcFlowEditor.StopAtDisplayedPhase();
        return ConnectShapeAtlas(new AbcFlowAtlasWindow(CaptureViewState("atlas")), state => AbcFlowEditor.Load(state.AbcFlow));
    }

    private void AbcFlow_OnChanged(object? sender, EventArgs e)
    {
        if (!_updatingUi && Kind == Fractal3DKind.AbcFlow)
            ScheduleRender(immediate: AbcFlowEditor.IsPlaying);
    }
}
