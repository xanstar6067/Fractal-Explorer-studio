namespace FractalExplorerWPF.Models.Fractal3DModes;

/// <summary>
/// Живое моделирование на ГП: поле считается шагами в <c>GpuVolumeSimulation</c>, кадр трассирует его
/// как объём плотности, а вместо итераций формулы у режима — собственная панель с шагами и паузой.
/// </summary>
internal abstract class GpuSimulation3DMode(Fractal3DKind kind, Fractal3DDefinition definition) : Fractal3DMode(kind, definition)
{
    public sealed override bool IsGpuSimulation => true;

    public sealed override bool IsDensityVolume => true;

    public sealed override bool UsesIterations => false;
}
