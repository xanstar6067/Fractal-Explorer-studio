using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public sealed class PhoenixParameterAtlasWindow : ParameterAtlasWindow
{
    private PhoenixState _source;
    public PhoenixAtlasParameters SelectedParameters => PhoenixAtlasParameters.From(SelectedComponents);
    public PhoenixParameterAtlasWindow(PhoenixState source)
    {
        _source = source.Clone();
        InitializeAtlas("Атлас параметров · Phoenix 2D", ["Re(C₁)", "Im(C₁)", "Re(C₂)", "Im(C₂)"],
            PhoenixAtlasParameters.From(source).ToArray(),
            "Выберите две разные оси. Остальные компоненты фиксированы. Применение C₁/C₂ включает динамическую плоскость текущего окна, сохраняя центр и масштаб.");
    }
    protected override int PreviewIterationCount => Math.Min(_source.Iterations, PhoenixParameterAtlasRenderer.PreviewIterations);
    protected override Func<decimal[], CancellationToken, byte[]> CreateRenderSnapshot()
    {
        var source = _source.Clone();
        return (point, token) => PhoenixParameterAtlasRenderer.Render(source, PhoenixAtlasParameters.From(point), 144, 108, token);
    }
    internal void UpdateFormulaParameters(PhoenixState state)
    {
        if (_source.Variant == state.Variant && _source.PrimaryPower == state.PrimaryPower && _source.SecondaryPower == state.SecondaryPower
            && _source.InitialZReal == state.InitialZReal && _source.InitialZImaginary == state.InitialZImaginary
            && _source.InitialPreviousReal == state.InitialPreviousReal && _source.InitialPreviousImaginary == state.InitialPreviousImaginary
            && _source.Iterations == state.Iterations && _source.Threshold == state.Threshold) return;
        _source = state.Clone();
        RebuildCurrentSourceIfLoaded();
    }
}
