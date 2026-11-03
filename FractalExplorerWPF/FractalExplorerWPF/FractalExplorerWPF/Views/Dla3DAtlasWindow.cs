using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

/// <summary>Воспроизводимое сравнение DLA с общей стадией, камерой и исходным seed.</summary>
public sealed class Dla3DAtlasWindow : Fractal3DParameterAtlasWindow
{
    private sealed record Parameter(string Name, decimal Minimum, decimal Maximum, decimal Width,
        Func<Dla3DSettings, double> Read, Action<Dla3DSettings, double> Write, bool Integer = false);
    private readonly Parameter[] _parameters =
    [
        new("Форма затравки", 1, 4, 2, s => (int)s.SeedShape + 1, (s,v) => s.SeedShape = (Dla3DSeedShape)((int)v - 1), true),
        new("Размер стержня / кольца / диска", 2, 24, 8, s => s.SeedSize, (s,v) => s.SeedSize = (int)v, true),
        new("Вероятность прилипания", .05m, 1, .3m, s => s.Stickiness, (s,v) => s.Stickiness = v),
        new("Сила потока", 0, .65m, .2m, s => s.FlowStrength, (s,v) => s.FlowStrength = v),
        new("Азимут потока, °", -180, 180, 90, s => s.FlowYaw, (s,v) => s.FlowYaw = v),
        new("Наклон потока, °", -90, 90, 60, s => s.FlowPitch, (s,v) => s.FlowPitch = v),
        new("Случайное число", int.MinValue, int.MaxValue, 4, s => s.Seed, (s,v) => s.Seed = (int)v, true)
    ];
    private readonly double[] _original;

    public Dla3DAtlasWindow(Fractal3DState source) : base(PreviewSource(source))
    {
        _original = _parameters.Select(p => p.Read(source.Dla)).ToArray();
        Initialize3DAtlas("Атлас параметров · DLA 3D", _parameters.Select(p => p.Name).ToArray(), _original.Select(v => (decimal)v).ToArray(),
            $"Каждый вариант заново растёт от затравки до {Math.Min(source.Dla.ParticleCount, 500):N0} частиц. " +
            "Случайное число одинаково для всех ячеек, пока вы сами его не измените. Это ранняя стадия: полный кластер может выглядеть иначе. " +
            $"При применении пересчитывается исходная стадия {source.Dla.ParticleCount:N0} частиц; цель {source.Dla.TargetParticles:N0}, камера и оформление сохраняются. " +
            "Размер не влияет на точечную затравку; направление не влияет при нулевом потоке, азимут — при вертикальном потоке. " +
            "При нулевой стадии видна только затравка: для сравнения ветвей сначала вырастите несколько частиц. " +
            "Рост основного окна остаётся на паузе; продолжить можно кнопкой «Растить». Большие сетки рассчитываются последовательно.", xAxis: 2, yAxis: 3);
    }

    private static Fractal3DState PreviewSource(Fractal3DState source)
    {
        if (source.Kind != Fractal3DKind.Dla3D || source.Dla is null || source.Dla != source.Dla.Normalized())
            throw new ArgumentException("Нужен режим DLA 3D с допустимыми параметрами роста.");
        var preview = source.Clone(); preview.Dla.ParticleCount = Math.Min(source.Dla.ParticleCount, 500);
        return preview;
    }

    protected override (decimal Minimum, decimal Maximum) ComponentBounds(int index) => (_parameters[index].Minimum, _parameters[index].Maximum);
    protected override (decimal Minimum, decimal Maximum) AxisBounds(int index) => ComponentBounds(index);
    protected override bool IsIntegerComponent(int index) => _parameters[index].Integer;
    protected override decimal InitialRangeWidth(int index) => _parameters[index].Width;
    protected override string ComponentValueLabel(int index, decimal value, string format) => index == 0
        ? Dla3DSettings.ShapeName((Dla3DSeedShape)((int)value - 1)) : base.ComponentValueLabel(index, value, format);
    protected override ParameterAtlasRange OverviewRange
    {
        get { var x = ComponentBounds(XAxisIndex); var y = ComponentBounds(YAxisIndex); return new(x.Minimum, x.Maximum, y.Minimum, y.Maximum); }
    }
    protected override string PreviewStatus(int completed, int total, int iterations) =>
        $"{completed} / {total} · рост на ЦП, кадр на ГП · до 500 частиц";

    protected override void ApplyComponents(Fractal3DState state, decimal[] point)
    {
        if (state.Kind != Fractal3DKind.Dla3D || state.Dla is null || point.Length != _parameters.Length)
            throw new ArgumentException("Нужен режим DLA 3D.");
        var settings = state.Dla with { };
        for (int i = 0; i < point.Length; i++)
        {
            var p = _parameters[i];
            if (point[i] < p.Minimum || point[i] > p.Maximum || p.Integer && point[i] != decimal.Truncate(point[i]))
                throw new ArgumentException($"Параметр «{p.Name}» вне диапазона или требует целого значения.");
            p.Write(settings, point[i] == (decimal)_original[i] ? _original[i] : (double)point[i]);
        }
        state.Dla = settings;
    }
}
