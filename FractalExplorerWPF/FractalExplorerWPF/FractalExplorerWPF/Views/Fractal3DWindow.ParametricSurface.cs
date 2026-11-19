using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class Fractal3DWindow
{
    private ParametricSurfaceSettings? _displayedParametricSurface;

    private void ParametricSurface_OnChanged(object sender, EventArgs e)
    {
        if (_updatingUi) return;
        ParametricEditor.ShowMeshStatus("");
        _renderCts?.Cancel();
        ScheduleRender();
    }

    private bool IsCurrentParametricFrame(ParametricSurfaceSettings? settings)
    {
        if (Kind != Fractal3DKind.ParametricSurface) return true;
        if (settings is null) return false;
        try { return ParametricEditor.Capture() == settings; }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException) { return false; }
    }

    private void OnParametricFrameDisplayed(ParametricSurfaceSettings? settings)
    {
        if (settings is null) return;
        _displayedParametricSurface = settings;
        int skipped = _renderer.ParametricSkippedSamples;
        ParametricEditor.ShowMeshStatus(skipped > 0
            ? $"Пропущено узлов: {skipped}. Проверьте область, если видны разрывы." : "");
    }
}
