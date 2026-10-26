using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class Fractal3DWindow
{
    private void HyperbolicAtlas_OnClick(object sender, EventArgs e)
    {
        if (Kind == Fractal3DKind.HyperbolicHoneycomb) OpenShapeAtlas(CreateHyperbolicAtlas);
    }

    private HyperbolicHoneycombAtlasWindow CreateHyperbolicAtlas() =>
        ConnectShapeAtlas(new HyperbolicHoneycombAtlasWindow(CaptureViewState("atlas")), state =>
            HyperbolicEditor.ApplyAtlasParameters(state.HyperbolicHoneycomb));

    private HyperbolicHoneycombSettings CaptureHyperbolicHoneycomb() =>
        Kind==Fractal3DKind.HyperbolicHoneycomb ? HyperbolicEditor.Capture() : new();

    private void LoadHyperbolicHoneycomb(HyperbolicHoneycombSettings? s)
    {
        if(Kind==Fractal3DKind.HyperbolicHoneycomb) HyperbolicEditor.Load(s ?? new());
    }

    private void HyperbolicChanged(object sender,EventArgs e) => Parameter_OnChanged(sender,e);
}
