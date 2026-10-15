namespace FractalExplorerWPF.Views;

public partial class Fractal3DWindow
{
    private bool IsCurrentNewtonFrame(Models.Fractal3DState state)
    {
        if (Kind != Models.Fractal3DKind.Newton3D) return true;
        try { return NewtonEditor.Capture() == state.Newton; }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException) { return false; }
    }
}
