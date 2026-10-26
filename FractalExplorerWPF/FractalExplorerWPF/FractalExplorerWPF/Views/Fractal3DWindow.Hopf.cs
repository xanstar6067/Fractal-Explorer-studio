using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class Fractal3DWindow
{
    private HopfSettings? _hopfDisplayedSettings;

    private void HopfAtlas_OnClick(object sender, EventArgs e)
    {
        if (Kind == Fractal3DKind.Hopf) OpenShapeAtlas(CreateHopfAtlas);
    }

    private HopfAtlasWindow CreateHopfAtlas()
    {
        StopHopfAnimation();
        return ConnectShapeAtlas(new HopfAtlasWindow(CaptureViewState("atlas")), state =>
        {
            HopfEditor.Load(state.Hopf);
            HopfEditor.SetColoring(state);
            _hopfDisplayedSettings = null;
        });
    }

    private void StopHopfAnimation()
    {
        bool playing = HopfEditor.IsPlaying;
        HopfEditor.Stop();
        if (!playing || _hopfDisplayedSettings is not { } displayed) return;
        var current = HopfEditor.Capture();
        bool updating = _updatingUi; _updatingUi = true;
        try
        {
            HopfEditor.Load(current with { XY = displayed.XY, XZ = displayed.XZ, XW = displayed.XW,
                YZ = displayed.YZ, YW = displayed.YW, ZW = displayed.ZW });
        }
        finally { _updatingUi = updating; }
    }

    private void OnHopfFrameDisplayed(Fractal3DState state)
    {
        _hopfDisplayedSettings = state.Hopf.Copy();
        HopfEditor.SetColoring(state);
        HopfEditor.OnFrameDisplayed();
    }

    private void Hopf_OnChanged(object? sender, EventArgs e)
    {
        if (!_updatingUi && Kind == Fractal3DKind.Hopf)
            ScheduleRender(immediate: HopfEditor.IsPlaying);
    }
}
