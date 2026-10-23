using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

/// <summary>Две активные числовые оси при фиксированных семействах, области и осях среза/градиента.</summary>
public sealed class PeriodicSurfaceAtlasWindow : Fractal3DParameterAtlasWindow
{
    private sealed record Parameter(string Name, decimal Minimum, decimal Maximum, decimal Width,
        Func<PeriodicSurfaceSettings, double> Read, Func<PeriodicSurfaceSettings, double, PeriodicSurfaceSettings> Write);
    private readonly Parameter[] _parameters;
    private readonly double[] _original;
    private readonly PeriodicSurfaceSettings _settings;

    public PeriodicSurfaceAtlasWindow(Fractal3DState source) : base(source)
    {
        if (source.Kind != Fractal3DKind.PeriodicSurfaces || source.PeriodicSurface is null)
            throw new ArgumentException("Нужна периодическая поверхность.");
        source.PeriodicSurface.Validate();
        _settings = source.PeriodicSurface with { };
        bool width = _settings.Region is PeriodicSurfaceRegion.Sheet or PeriodicSurfaceRegion.Both;
        var parameters = new List<Parameter>
        {
            new("Период", .5m, 4, .6m, s => s.Period, (s, v) => s with { Period = v }),
            new("Уровень F", -.9m, .9m, .4m, s => s.Level, (s, v) => s with { Level = v }),
            new("Растяжение X", .5m, 2, .4m, s => s.StretchX, (s, v) => s with { StretchX = v }),
            new("Растяжение Y", .5m, 2, .4m, s => s.StretchY, (s, v) => s with { StretchY = v }),
            new("Растяжение Z", .5m, 2, .4m, s => s.StretchZ, (s, v) => s with { StretchZ = v })
        };
        if (_settings.Family != _settings.BlendFamily)
            parameters.Add(new("Доля второго семейства", 0, 1, .4m, s => s.Blend, (s, v) => s with { Blend = v }));
        int widthIndex = parameters.Count;
        if (width)
        {
            parameters.Add(new(_settings.Region == PeriodicSurfaceRegion.Both ? "Зазор по F" : "Толщина по F",
                .02m, 1.2m, .2m, s => s.Thickness, (s, v) => s with { Thickness = v }));
            if (_settings.ThicknessAxis > 0)
                parameters.Add(new("Градиент толщины", -.9m, .9m, .4m, s => s.ThicknessGradient, (s, v) => s with { ThicknessGradient = v }));
        }
        if (_settings.CutAxis > 0)
            parameters.Add(new("Положение среза", -1, 1, .4m, s => s.CutPosition, (s, v) => s with { CutPosition = v }));
        _parameters = parameters.ToArray();
        _original = _parameters.Select(p => p.Read(_settings)).ToArray();
        static string Family(PeriodicSurfaceFamily family) => family switch
        { PeriodicSurfaceFamily.SchwarzP => "Schwarz P", PeriodicSurfaceFamily.SchwarzD => "Schwarz D", _ => "Гироид" };
        string region = _settings.Region switch
        {
            PeriodicSurfaceRegion.Surface => "поверхность без толщины", PeriodicSurfaceRegion.Positive => "положительный лабиринт",
            PeriodicSurfaceRegion.Negative => "отрицательный лабиринт", PeriodicSurfaceRegion.Both => "два лабиринта", _ => "стенки"
        };
        static string Axis(int axis) => axis == 0 ? "нет" : "XYZ"[axis - 1].ToString();
        Initialize3DAtlas("Атлас параметров · Периодические поверхности", _parameters.Select(p => p.Name).ToArray(),
            _original.Select(v => (decimal)v).ToArray(),
            $"Семейства: {Family(_settings.Family)} → {Family(_settings.BlendFamily)}. Область: {region}. " +
            $"Ось градиента: {Axis(_settings.ThicknessAxis)}; ось среза: {Axis(_settings.CutAxis)}. " +
            "На осях доступны активные числовые параметры; остальные фиксированы. Толщина и зазор измеряются по F. " +
            "Камера и оформление — из текущего вида. Для смены семейства, области или осей вернитесь в основное окно.",
            xAxis: 0, yAxis: width ? widthIndex : 1);
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

    protected override void ApplyComponents(Fractal3DState state, decimal[] point)
    {
        var s = state.PeriodicSurface;
        if (state.Kind != Fractal3DKind.PeriodicSurfaces || s is null || point.Length != _parameters.Length ||
            s.Family != _settings.Family || s.BlendFamily != _settings.BlendFamily || s.Region != _settings.Region ||
            s.ThicknessAxis != _settings.ThicknessAxis || s.CutAxis != _settings.CutAxis)
            throw new ArgumentException("Семейства, область или оси не соответствуют атласу.");
        for (int i = 0; i < point.Length; i++)
        {
            var p = _parameters[i];
            if (point[i] < p.Minimum || point[i] > p.Maximum)
                throw new ArgumentException($"Параметр «{p.Name}» вне диапазона.");
            s = p.Write(s, point[i] == (decimal)_original[i] ? _original[i] : (double)point[i]);
        }
        s.Validate();
        state.PeriodicSurface = s;
    }
}
