using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

/// <summary>Числовые вариации поля Ляпунова с переносом диапазонов без изменения их ширины.</summary>
public sealed class Lyapunov3DAtlasWindow : Fractal3DParameterAtlasWindow
{
    private sealed record Parameter(string Name, decimal Minimum, decimal Maximum, decimal Width,
        Func<Lyapunov3DSettings, double> Read, Func<Lyapunov3DSettings, double, Lyapunov3DSettings> Write);
    private readonly Parameter[] _parameters;
    private readonly double[] _original;
    private readonly Lyapunov3DSettings _settings;
    internal const int PreviewSize = 32, PreviewTransient = 256, PreviewSamples = 512;

    public Lyapunov3DAtlasWindow(Fractal3DState source) : base(PreviewSource(source))
    {
        _settings = source.Lyapunov with { };
        var parameters = new List<Parameter>();
        AddCenter('A', _settings.MinA, _settings.MaxA, s => (s.MinA + s.MaxA) / 2,
            (s, min, max) => s with { MinA = min, MaxA = max });
        AddCenter('B', _settings.MinB, _settings.MaxB, s => (s.MinB + s.MaxB) / 2,
            (s, min, max) => s with { MinB = min, MaxB = max });
        AddCenter('C', _settings.MinC, _settings.MaxC, s => (s.MinC + s.MaxC) / 2,
            (s, min, max) => s with { MinC = min, MaxC = max });
        parameters.Add(new("Начальное x", .000001m, .999999m, .2m, s => s.InitialX, (s, v) => s with { InitialX = v }));
        if (!_settings.SliceOnly)
        {
            parameters.Add(new("Уровень λ", -2, 1, .2m, s => s.Level, (s, v) => s with { Level = v }));
            if (_settings.Region == Lyapunov3DRegion.Boundary)
                parameters.Add(new("Полуширина полосы λ", .005m, .5m, .06m, s => s.BandWidth, (s, v) => s with { BandWidth = v }));
        }
        if (_settings.CutAxis > 0)
            parameters.Add(new("Положение среза · доля диапазона", -1, 1, .4m,
                s => s.CutPosition, (s, v) => s with { CutPosition = v }));
        _parameters = parameters.ToArray();
        _original = _parameters.Select(p => p.Read(_settings)).ToArray();
        string region = _settings.SliceOnly ? "цветная плоскость" : _settings.Region switch
        { Lyapunov3DRegion.Chaotic => "хаос", Lyapunov3DRegion.Boundary => "граничная полоса", _ => "устойчивость" };
        Initialize3DAtlas("Атлас параметров · Ляпунов 3D", _parameters.Select(p => p.Name).ToArray(),
            _original.Select(v => (decimal)v).ToArray(),
            $"Последовательность: {_settings.Sequence}; вид: {region}. " +
            "Центр сдвигает весь диапазон A/B/C с сохранением ширины; отсутствующие в последовательности буквы и " +
            "неподвижные полные диапазоны 0…4 не предлагаются на осях. Остальные параметры фиксированы. " +
            $"Превью: {PreviewSize}³, прогрев до {PreviewTransient}, измерение до {PreviewSamples}. " +
            "Применение сохраняет основную сетку, прогрев, измерение, последовательность, вид, ось среза, камеру и оформление. " +
            "Тонкие границы и влияние начального x проверяйте в основном окне с его точностью.");

        void AddCenter(char letter, double min, double max, Func<Lyapunov3DSettings, double> read,
            Func<Lyapunov3DSettings, double, double, Lyapunov3DSettings> write)
        {
            decimal half = (decimal)(max - min) / 2;
            if (!_settings.Sequence.Contains(letter) || half >= 2) return;
            double originalCenter = (min + max) / 2;
            parameters.Add(new($"Центр диапазона {letter}", half, 4 - half, .2m, read, (s, center) =>
            {
                if (center == originalCenter) return write(s, min, max);
                double lower = Math.Clamp(center - (max - min) / 2, 0, 4);
                double upper = Math.Clamp(center + (max - min) / 2, 0, 4);
                // При минимальной ширине округление double не должно сделать сдвинутый интервал невалидным.
                if (upper - lower < .00001)
                {
                    if (lower + .00001 < 4) upper = Math.BitIncrement(lower + .00001);
                    else lower = Math.BitDecrement(upper - .00001);
                }
                return write(s, lower, upper);
            }));
        }
    }

    private static Fractal3DState PreviewSource(Fractal3DState source)
    {
        if (source.Kind != Fractal3DKind.Lyapunov3D || source.Lyapunov is null)
            throw new ArgumentException("Нужен режим Ляпунов 3D.");
        source.Lyapunov.Validate();
        var preview = source.Clone();
        preview.Lyapunov = preview.Lyapunov with { Size = PreviewSize,
            Transient = Math.Min(preview.Lyapunov.Transient, PreviewTransient),
            Samples = Math.Min(preview.Lyapunov.Samples, PreviewSamples) };
        return preview;
    }

    protected override (decimal Minimum, decimal Maximum) ComponentBounds(int index) =>
        (_parameters[index].Minimum, _parameters[index].Maximum);
    protected override (decimal Minimum, decimal Maximum) AxisBounds(int index) => ComponentBounds(index);
    protected override decimal InitialRangeWidth(int index) => _parameters[index].Width;
    protected override ParameterAtlasRange OverviewRange
    {
        get
        {
            var x = ComponentBounds(XAxisIndex); var y = ComponentBounds(YAxisIndex);
            return new(x.Minimum, x.Maximum, y.Minimum, y.Maximum);
        }
    }
    protected override string PreviewStatus(int completed, int total, int iterations) =>
        $"{completed} / {total} · ГП, по одному варианту · {PreviewSize}³ · прогрев ≤ {PreviewTransient}, измерение ≤ {PreviewSamples}";

    protected override void ApplyComponents(Fractal3DState state, decimal[] point)
    {
        var s = state.Lyapunov;
        if (state.Kind != Fractal3DKind.Lyapunov3D || s is null || point.Length != _parameters.Length ||
            s.Sequence != _settings.Sequence || s.Region != _settings.Region || s.CutAxis != _settings.CutAxis || s.SliceOnly != _settings.SliceOnly ||
            s.MaxA - s.MinA != _settings.MaxA - _settings.MinA || s.MaxB - s.MinB != _settings.MaxB - _settings.MinB ||
            s.MaxC - s.MinC != _settings.MaxC - _settings.MinC)
            throw new ArgumentException("Последовательность, вид, срез или ширина диапазонов не соответствует атласу Ляпунова.");
        for (int i = 0; i < point.Length; i++)
        {
            var p = _parameters[i];
            if (point[i] < p.Minimum || point[i] > p.Maximum)
                throw new ArgumentException($"Параметр «{p.Name}» вне диапазона.");
            s = p.Write(s, point[i] == (decimal)_original[i] ? _original[i] : (double)point[i]);
        }
        s.Validate();
        state.Lyapunov = s;
    }
}
