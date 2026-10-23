using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

/// <summary>Две числовые оси KIFS при фиксированных симметрии и исходной форме.</summary>
public sealed class KifsAtlasWindow : Fractal3DParameterAtlasWindow
{
    private sealed record Parameter(string Name, decimal Minimum, decimal Maximum, decimal Width,
        Func<KifsSettings, double> Read, Action<KifsSettings, double> Write, bool Integer = false);

    private readonly Parameter[] _parameters;
    private readonly double[] _original;
    private readonly KifsSymmetry _symmetry;
    private readonly KifsSeed _seed;
    public decimal[] SelectedParameters => SelectedComponents;

    public KifsAtlasWindow(Fractal3DState source) : base(source)
    {
        if (source.Kind != Fractal3DKind.Kifs || source.Kifs is null ||
            !Enum.IsDefined(source.Kifs.Symmetry) || !Enum.IsDefined(source.Kifs.Seed))
            throw new ArgumentException("Нужны корректные параметры KIFS.");
        _symmetry = source.Kifs.Symmetry; _seed = source.Kifs.Seed;
        var parameters = new List<Parameter>
        {
            new("Масштаб", 1.2m, 4, .4m, s => s.Scale, (s, v) => s.Scale = v),
            new("Размер исходной формы", .1m, 2, .4m, s => s.Radius, (s, v) => s.Radius = v),
            new("Поворот X · градусы", -180, 180, 30, s => s.RotationX, (s, v) => s.RotationX = v),
            new("Поворот Y · градусы", -180, 180, 30, s => s.RotationY, (s, v) => s.RotationY = v),
            new("Поворот Z · градусы", -180, 180, 30, s => s.RotationZ, (s, v) => s.RotationZ = v),
            new("Смещение X", -2, 2, .4m, s => s.OffsetX, (s, v) => s.OffsetX = v),
            new("Смещение Y", -2, 2, .4m, s => s.OffsetY, (s, v) => s.OffsetY = v),
            new("Смещение Z", -2, 2, .4m, s => s.OffsetZ, (s, v) => s.OffsetZ = v)
        };
        if (_symmetry == KifsSymmetry.Dihedral)
            parameters.Add(new("Число лучей", 3, 16, 4, s => s.Sectors, (s, v) => s.Sectors = (int)v, true));
        _parameters = parameters.ToArray();
        _original = _parameters.Select(p => p.Read(source.Kifs)).ToArray();
        for (int i = 0; i < _original.Length; i++)
            if (!double.IsFinite(_original[i]) || _original[i] < (double)_parameters[i].Minimum ||
                _original[i] > (double)_parameters[i].Maximum ||
                _parameters[i].Integer && _original[i] != Math.Truncate(_original[i]))
                throw new ArgumentException($"Параметр «{_parameters[i].Name}» вне допустимого диапазона.");
        string seed = _seed switch { KifsSeed.Cube => "куб", KifsSeed.Octahedron => "октаэдр", _ => "сфера" };
        Initialize3DAtlas("Атлас параметров · KIFS", _parameters.Select(p => p.Name).ToArray(),
            _original.Select(value => (decimal)value).ToArray(),
            $"Симметрия: {KifsSettings.SymmetryName(_symmetry)}. Исходная форма: {seed}. " +
            "Выберите два параметра для осей; остальные фиксированы. Камера и оформление — из текущего вида. Число итераций при применении сохраняется. " +
            "Применение можно отменить в основном окне. Некоторые варианты могут быть пустыми или выходить за кадр.",
            xAxis: 0, yAxis: 4);
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

    protected override void ApplyComponents(Fractal3DState state, decimal[] point)
    {
        if (state.Kind != Fractal3DKind.Kifs || state.Kifs is null || point.Length != _parameters.Length ||
            state.Kifs.Symmetry != _symmetry || state.Kifs.Seed != _seed)
            throw new ArgumentException("Состояние не соответствует симметрии и исходной форме атласа KIFS.");
        for (int i = 0; i < point.Length; i++)
            if (point[i] < _parameters[i].Minimum || point[i] > _parameters[i].Maximum ||
                _parameters[i].Integer && point[i] != decimal.Truncate(point[i]))
                throw new ArgumentException($"Параметр «{_parameters[i].Name}» вне диапазона или требует целого значения.");
        var settings = state.Kifs.Clone();
        for (int i = 0; i < point.Length; i++)
            _parameters[i].Write(settings, point[i] == (decimal)_original[i] ? _original[i] : (double)point[i]);
        state.Kifs = settings;
    }
}
