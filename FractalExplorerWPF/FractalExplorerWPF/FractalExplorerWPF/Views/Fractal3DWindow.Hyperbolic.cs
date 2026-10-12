using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class Fractal3DWindow
{
    private HyperbolicHoneycombSettings CaptureHyperbolicHoneycomb() =>
        Kind==Fractal3DKind.HyperbolicHoneycomb ? HyperbolicEditor.Capture() : new();

    private void LoadHyperbolicHoneycomb(HyperbolicHoneycombSettings? s)
    {
        if(Kind==Fractal3DKind.HyperbolicHoneycomb) HyperbolicEditor.Load(s ?? new());
    }

    private void HyperbolicChanged(object sender,EventArgs e) => Parameter_OnChanged(sender,e);
}
