using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

/// <summary>Проекции одной воспроизводимой выборки посещений четырёхмерного пространства (z, c).</summary>
public sealed class Buddhabrot4DAtlasWindow : Fractal3DParameterAtlasWindow
{
    private sealed record Parameter(string Name, decimal Minimum, decimal Maximum, decimal Width,
        Func<Buddhabrot4DSettings, double> Read, Action<Buddhabrot4DSettings, double> Write, bool Integer = false);
    private readonly Parameter[] _parameters;
    private readonly double[] _original;
    private readonly bool _cloud;

    public Buddhabrot4DAtlasWindow(Fractal3DState source) : base(PreviewSource(source))
    {
        _cloud = IsCloud(source);
        var parameters = new List<Parameter>
        {
            new("Проекция после поворота", 1, 4, 2, s => (int)s.Projection + 1, (s,v) => s.Projection = (Buddhabrot4DProjection)((int)v - 1), true),
            new("Re z ↔ Im z, °", -180, 180, 90, s => s.ZrZi, (s,v) => s.ZrZi = v),
            new("Re z ↔ Re c, °", -180, 180, 90, s => s.ZrCr, (s,v) => s.ZrCr = v),
            new("Re z ↔ Im c, °", -180, 180, 90, s => s.ZrCi, (s,v) => s.ZrCi = v),
            new("Im z ↔ Re c, °", -180, 180, 90, s => s.ZiCr, (s,v) => s.ZiCr = v),
            new("Im z ↔ Im c, °", -180, 180, 90, s => s.ZiCi, (s,v) => s.ZiCi = v),
            new("Re c ↔ Im c, °", -180, 180, 90, s => s.CrCi, (s,v) => s.CrCi = v),
            new("Минимум итераций до выхода", 1, 9999, 20, s => s.MinIterations, (s,v) => s.MinIterations = (int)v, true),
            new("Максимум итераций", 2, 10000, 400, s => s.MaxIterations, (s,v) => s.MaxIterations = (int)v, true),
            new("Предел R · время выхода", 1, 10000, 100, s => s.RedLimit, (s,v) => s.RedLimit = (int)v, true),
            new("Предел G · время выхода", 1, 10000, 200, s => s.GreenLimit, (s,v) => s.GreenLimit = (int)v, true),
            new("Предел B · время выхода", 1, 10000, 400, s => s.BlueLimit, (s,v) => s.BlueLimit = (int)v, true),
            new("Начальное случайное число", int.MinValue, int.MaxValue, 4, s => s.Seed, (s,v) => s.Seed = (int)v, true),
            new("Экспозиция", .05m, 20, 1, s => s.Exposure, (s,v) => s.Exposure = v),
            new("Гамма", .5m, 4, .6m, s => s.Gamma, (s,v) => s.Gamma = v),
            new("Насыщенность", 0, 2, .6m, s => s.Saturation, (s,v) => s.Saturation = v)
        };
        if (_cloud) parameters.Add(new("Плотность облака", .05m, 10, .4m, s => s.Density, (s,v) => s.Density = v));
        _parameters = parameters.ToArray(); _original = _parameters.Select(p => p.Read(source.Buddhabrot)).ToArray();
        Initialize3DAtlas("Атлас параметров · Буддаброт 4D", _parameters.Select(p => p.Name).ToArray(), _original.Select(v => (decimal)v).ToArray(),
            "Четыре проекции и шесть поворотов меняют вид облака (Re z, Im z, Re c, Im c). Это проекции после поворота, а не срезы. " +
            "При неизменных seed и пределах орбит используется одна выборка. Минимум/максимум отбирают орбиты по первому выходу; пределы R/G/B независимы, их можно переставлять. " +
            "Минимум должен быть меньше максимума. Канал получает посещения орбит, вышедших не позднее своего предела. " +
            $"Превью используют {Math.Min(source.Buddhabrot.SampleCount, 10_000):N0} затравок; полное число {source.Buddhabrot.SampleCount:N0} сохраняется. " +
            "Редкие орбиты могут не попасть в миниатюру; отказ выборки не доказывает отсутствия формы в полном расчёте. " +
            "Камера, стиль и качество сохраняются. Каждая проекция вписывается отдельно. Начальные оси смешивают z с c; фиксированные поля прокручиваются.", xAxis: 2, yAxis: 5);
    }

    private static bool IsCloud(Fractal3DState s) => s.ShadingStyle is Fractal3DShadingStyle.Glow or Fractal3DShadingStyle.Density;
    private static Fractal3DState PreviewSource(Fractal3DState source)
    {
        if (source.Kind != Fractal3DKind.Buddhabrot4D || source.Buddhabrot is null) throw new ArgumentException("Нужен режим Буддаброта 4D.");
        source.Buddhabrot.Validate();
        var preview = source.Clone(); preview.Buddhabrot.SampleCount = Math.Min(source.Buddhabrot.SampleCount, 10_000); return preview;
    }
    protected override (decimal Minimum, decimal Maximum) ComponentBounds(int index) => (_parameters[index].Minimum, _parameters[index].Maximum);
    protected override (decimal Minimum, decimal Maximum) AxisBounds(int index) => ComponentBounds(index);
    protected override bool IsIntegerComponent(int index) => _parameters[index].Integer;
    protected override decimal InitialRangeWidth(int index) => _parameters[index].Width;
    protected override string ComponentValueLabel(int index, decimal value, string format) => index == 0
        ? Buddhabrot4DSettings.ProjectionName((Buddhabrot4DProjection)((int)value - 1)) : base.ComponentValueLabel(index, value, format);
    protected override ParameterAtlasRange OverviewRange
    {
        get { var x = ComponentBounds(XAxisIndex); var y = ComponentBounds(YAxisIndex); return new(x.Minimum, x.Maximum, y.Minimum, y.Maximum); }
    }
    protected override string? UnavailableSample(decimal[] point) => point[7] >= point[8]
        ? "Минимум итераций должен быть меньше максимума."
        : Math.Max(point[9], Math.Max(point[10], point[11])) < Math.Max(point[7], 2)
            ? "Все каналы исключают выбранные орбиты. Увеличьте пределы R/G/B." : null;
    protected override string? UnavailableSampleError(Exception error) => error is Buddhabrot4DSampleException ? error.Message : null;
    protected override string PreviewStatus(int completed, int total, int iterations) =>
        $"{completed} / {total} · орбиты на ЦП, кадр на ГП · до 10 000 затравок";
    protected override void ApplyComponents(Fractal3DState state, decimal[] point)
    {
        if (state.Kind != Fractal3DKind.Buddhabrot4D || state.Buddhabrot is null || point.Length != _parameters.Length || IsCloud(state) != _cloud)
            throw new ArgumentException("Режим или стиль не соответствует атласу.");
        var settings = state.Buddhabrot with { };
        for (int i = 0; i < point.Length; i++)
        {
            var p = _parameters[i];
            if (point[i] < p.Minimum || point[i] > p.Maximum || p.Integer && point[i] != decimal.Truncate(point[i]))
                throw new ArgumentException($"Параметр «{p.Name}» вне диапазона или требует целого значения.");
            p.Write(settings, point[i] == (decimal)_original[i] ? _original[i] : (double)point[i]);
        }
        if (UnavailableSample(point) is { } reason) throw new ArgumentException(reason);
        settings.Validate(); state.Buddhabrot = settings;
    }
}
