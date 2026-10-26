using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

/// <summary>Вариации шума, формы и осыпания с сохранением сетки основного ландшафта.</summary>
public sealed class TerrainAtlasWindow : Fractal3DParameterAtlasWindow
{
    private sealed record Parameter(string Name, decimal Minimum, decimal Maximum, decimal Width,
        Func<TerrainSettings, double> Read, Func<TerrainSettings, double, TerrainSettings> Write, bool Integer = false);
    private readonly Parameter[] _parameters;
    private readonly double[] _original;
    private readonly TerrainSettings _settings;
    internal const int PreviewResolution = 257;

    public TerrainAtlasWindow(Fractal3DState source) : base(PreviewSource(source))
    {
        _settings = source.Terrain with { };
        var parameters = new List<Parameter>
        {
            new("Шероховатость", .05m, .95m, .2m, s => s.Roughness, (s, v) => s with { Roughness = v }),
            new("Масштаб деталей", .25m, 16m, 1.5m, s => s.Scale, (s, v) => s with { Scale = v }),
            new("Высота", .05m, 8m, 1m, s => s.Height, (s, v) => s with { Height = v }),
            new("Размер участка", 1m, 20m, 2m, s => s.Size, (s, v) => s with { Size = v }),
            new("Уровни деталей", 1m, 12m, 4m, s => s.Octaves, (s, v) => s with { Octaves = (int)v }, Integer: true),
            new("Отношение масштабов", 1.5m, 3m, .4m, s => s.Lacunarity, (s, v) => s with { Lacunarity = v }),
            new("Начальное число", 0m, int.MaxValue, 12m, s => s.Seed, (s, v) => s with { Seed = (int)v }, Integer: true),
            new("Проходы эрозии", 0m, 64m, 16m, s => s.ThermalIterations, (s, v) => s with { ThermalIterations = (int)v }, Integer: true),
            new("Угол осыпания, °", 5m, 60m, 20m, s => s.TalusAngle, (s, v) => s with { TalusAngle = v })
        };
        if (_settings.Shape != TerrainShape.Patch)
        {
            parameters.Add(new("Сила формы", 0m, 1m, .4m, s => s.ShapeStrength, (s, v) => s with { ShapeStrength = v }));
            parameters.Add(new("Радиус формы", .2m, 1m, .3m, s => s.ShapeRadius, (s, v) => s with { ShapeRadius = v }));
        }
        _parameters = parameters.ToArray();
        _original = _parameters.Select(p => p.Read(_settings)).ToArray();
        string type = _settings.Type switch { TerrainKind.Fbm => "холмы fBm", TerrainKind.Hybrid => "гибридный рельеф", _ => "горные хребты" };
        string shape = _settings.Shape switch { TerrainShape.Island => "остров", TerrainShape.Crater => "кратер", _ => "обычный участок" };
        Initialize3DAtlas("Атлас параметров · Ландшафты", _parameters.Select(p => p.Name).ToArray(),
            _original.Select(value => (decimal)value).ToArray(),
            $"Поверхность: {type}; форма: {shape}. Выберите любые два параметра для осей; остальные фиксированы. " +
            "Угол осыпания действует при числе проходов больше нуля; радиус формы — при положительной силе формы. " +
            $"Превью: сетка {PreviewResolution}×{PreviewResolution}, выбранное число проходов эрозии, текущие камера и оформление. " +
            "Мелкие октавы отсекаются по разрешению; эрозия зависит от сетки. " +
            $"Применение сохраняет основную сетку {_settings.Resolution}×{_settings.Resolution}, тип шума и форму участка. " +
            "На более плотной сетке мелкие детали и осыпание могут отличаться; уточняйте результат в основном окне.");
    }

    private static Fractal3DState PreviewSource(Fractal3DState source)
    {
        if (source.Kind != Fractal3DKind.Terrain || source.Terrain is null)
            throw new ArgumentException("Нужен режим фрактальных ландшафтов.");
        source.Terrain.Validate();
        var preview = source.Clone();
        preview.Terrain = preview.Terrain with { Resolution = PreviewResolution };
        return preview;
    }

    protected override (decimal Minimum, decimal Maximum) ComponentBounds(int index) =>
        (_parameters[index].Minimum, _parameters[index].Maximum);
    protected override (decimal Minimum, decimal Maximum) AxisBounds(int index) => ComponentBounds(index);
    protected override bool IsIntegerComponent(int index) => _parameters[index].Integer;
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
        $"{completed} / {total} · по одному варианту · сетка {PreviewResolution}×{PreviewResolution}, выбранные проходы эрозии";

    protected override void ApplyComponents(Fractal3DState state, decimal[] point)
    {
        var settings = state.Terrain;
        if (state.Kind != Fractal3DKind.Terrain || settings is null || point.Length != _parameters.Length ||
            settings.Type != _settings.Type || settings.Shape != _settings.Shape)
            throw new ArgumentException("Тип шума или форма участка не соответствует атласу ландшафтов.");
        for (int i = 0; i < point.Length; i++)
        {
            var parameter = _parameters[i];
            if (point[i] < parameter.Minimum || point[i] > parameter.Maximum ||
                parameter.Integer && point[i] != decimal.Truncate(point[i]))
                throw new ArgumentException($"Параметр «{parameter.Name}» вне диапазона или требует целого значения.");
            settings = parameter.Write(settings, point[i] == (decimal)_original[i] ? _original[i] : (double)point[i]);
        }
        settings.Validate();
        state.Terrain = settings;
    }
}
