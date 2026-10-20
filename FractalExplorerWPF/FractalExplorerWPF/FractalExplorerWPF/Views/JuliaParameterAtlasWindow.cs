using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public sealed class JuliaParameterAtlasWindow : ParameterAtlasWindow
{
    private readonly MandelbrotState _source;
    public decimal SelectedReal => SelectedComponents[0];
    public decimal SelectedImaginary => SelectedComponents[1];
    public event EventHandler? ConstantApplied { add => ParametersApplied += value; remove => ParametersApplied -= value; }

    public JuliaParameterAtlasWindow(MandelbrotState source)
    {
        if (!MandelbrotVariantDefinition.IsJulia(source.Variant)) throw new ArgumentException("Требуется вариант Жюлиа.", nameof(source));
        _source = source.Clone();
        InitializeAtlas($"Атлас параметров · {MandelbrotVariantDefinition.For(source.Variant).DisplayName}",
            ["Re(C)", "Im(C)"], [source.JuliaCReal, source.JuliaCImaginary],
            "Общий обзор формы и текущая палитра. Применение меняет только C, сохраняя вид основного окна.");
    }

    protected override int PreviewIterationCount => Math.Min(_source.Iterations, JuliaParameterAtlasRenderer.PreviewIterations);
    protected override ParameterAtlasRange OverviewRange => ParameterAtlasRange.JuliaOverview(_source.Variant);
    protected override Func<decimal[], CancellationToken, byte[]> CreateRenderSnapshot()
    {
        var source = _source.Clone();
        return (point, token) => JuliaParameterAtlasRenderer.Render(source, point[0], point[1], 144, 108, token);
    }
    internal void UpdateFormulaParameters(decimal power, bool useInversion, HybridFormulaSettings hybrid)
    {
        if (_source.Power == power && _source.UseInversion == useInversion && _source.Hybrid.Steps.Select(s => (s.Formula, s.Power, s.Repeats))
            .SequenceEqual(hybrid.Steps.Select(s => (s.Formula, s.Power, s.Repeats)))) return;
        _source.Power = power; _source.UseInversion = useInversion; _source.Hybrid = hybrid.Clone();
        RebuildCurrentSourceIfLoaded();
    }
}
