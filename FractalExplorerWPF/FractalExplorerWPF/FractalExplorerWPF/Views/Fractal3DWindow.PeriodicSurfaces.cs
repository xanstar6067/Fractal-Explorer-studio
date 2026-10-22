using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class Fractal3DWindow
{
    private PeriodicSurfaceSettings CapturePeriodicSurface() => Kind == Fractal3DKind.PeriodicSurfaces
        ? PeriodicEditor.Capture() : new();

    private void LoadPeriodicSurface(PeriodicSurfaceSettings? settings)
    {
        if (Kind == Fractal3DKind.PeriodicSurfaces) PeriodicEditor.Load(settings);
    }

    private void Periodic_OnChanged(object sender, EventArgs e)
    {
        if (!_updatingUi && Kind == Fractal3DKind.PeriodicSurfaces) Parameter_OnChanged(sender, e);
    }

    private void PeriodicColorLabyrinths_OnRequested(object sender, EventArgs e)
    {
        if (!_updatingUi && Kind == Fractal3DKind.PeriodicSurfaces)
            ColoringModeBox.SelectedIndex = (int)Fractal3DColoringMode.OrbitTrap;
    }
}
