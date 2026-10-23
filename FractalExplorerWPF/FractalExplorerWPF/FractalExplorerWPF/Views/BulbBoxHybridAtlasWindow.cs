using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

/// <summary>Атлас классического гибрида или одного выбранного этапа своей цепочки.</summary>
public sealed class BulbBoxHybridAtlasWindow : Fractal3DParameterAtlasWindow
{
    private sealed record Parameter(string Name, decimal Minimum, decimal Maximum, decimal Width,
        Func<Fractal3DState, double> Read, Action<Fractal3DState, double> Write, bool Integer = false);

    private readonly Parameter[] _parameters;
    private readonly double[] _original;
    private readonly bool _custom;
    private readonly BulbBoxOperation _operation;
    public int StageIndex { get; }
    public decimal[] SelectedParameters => SelectedComponents;

    public BulbBoxHybridAtlasWindow(Fractal3DState source, int stageIndex = 0) : base(source)
    {
        if (source.Kind != Fractal3DKind.BulbBoxHybrid) throw new ArgumentException("Нужен гибрид бульба и бокса.");
        _custom = source.HybridSequence is not null;
        StageIndex = stageIndex;
        string context;
        if (_custom)
        {
            source.HybridSequence!.Validate();
            if ((uint)stageIndex >= source.HybridSequence.Stages.Count) throw new ArgumentOutOfRangeException(nameof(stageIndex));
            var stage = source.HybridSequence.Stages[stageIndex];
            _operation = stage.Operation;
            _parameters = StageParameters(stage);
            context = $"Этап {stageIndex + 1}: {BulbBoxStage.Name(stage.Operation)}. Остальные этапы фиксированы. " +
                (stage.Operation is BulbBoxOperation.Bulb or BulbBoxOperation.Box
                    ? $"Инверсия: {Fractal3DCatalog.BoxInversionShapeName(stage.Inversion)}. " : "");
        }
        else
        {
            _parameters = ClassicParameters();
            context = "Классическое чередование. Порядок операций и форма инверсии фиксированы. ";
        }
        _original = _parameters.Select(p => p.Read(source)).ToArray();
        for (int i = 0; i < _original.Length; i++)
        {
            if (!double.IsFinite(_original[i]) || _original[i] < (double)_parameters[i].Minimum ||
                _original[i] > (double)_parameters[i].Maximum ||
                _parameters[i].Integer && _original[i] != Math.Truncate(_original[i]))
                throw new ArgumentException($"Параметр «{_parameters[i].Name}» вне допустимого диапазона.");
        }
        Initialize3DAtlas("Атлас параметров · Гибрид бульба и бокса", _parameters.Select(p => p.Name).ToArray(),
            _original.Select(value => (decimal)value).ToArray(),
            context + "Выберите два параметра для осей; остальные фиксированы. Камера и оформление — из текущего вида. " +
            "Целые повторы не дублируются в сетке. Некоторые варианты могут быть пустыми или выходить за кадр.",
            xAxis: _custom && _operation == BulbBoxOperation.Box ? 1 : 0,
            yAxis: _custom && _operation == BulbBoxOperation.Bulb ? 4 : _custom && _operation == BulbBoxOperation.Box ? 2 : 1);
    }

    private static Parameter[] ClassicParameters() =>
    [
        new("Степень бульба", 2, 12, 2, s => s.Power, (s, v) => s.Power = v),
        new("Масштаб бокса", -8, 8, .8m, s => s.BoxScale, (s, v) => s.BoxScale = v),
        new("Минимальный радиус", .01m, 4, .4m, s => s.BoxMinRadius, (s, v) => s.BoxMinRadius = v),
        new("Предел свёртки", .1m, 8, .4m, s => s.BoxFoldingLimit, (s, v) => s.BoxFoldingLimit = v),
        new("Подмешивание", 0, 1, .2m, s => s.HybridMix, (s, v) => s.HybridMix = v),
        new("Итераций бульба подряд", 1, 6, 2, s => s.HybridBulbSteps, (s, v) => s.HybridBulbSteps = (int)v, true),
        new("Итераций бокса подряд", 1, 6, 2, s => s.HybridBoxSteps, (s, v) => s.HybridBoxSteps = (int)v, true)
    ];

    private Parameter[] StageParameters(BulbBoxStage stage)
    {
        BulbBoxStage At(Fractal3DState s) => s.HybridSequence!.Stages[StageIndex];
        var parameters = new List<Parameter>();
        if (stage.Operation is BulbBoxOperation.Rotate or BulbBoxOperation.Translate)
        {
            decimal limit = stage.Operation == BulbBoxOperation.Rotate ? 180 : 3;
            decimal width = stage.Operation == BulbBoxOperation.Rotate ? 30 : .4m;
            string unit = stage.Operation == BulbBoxOperation.Rotate ? " · градусы" : "";
            parameters.AddRange([
                new("X" + unit, -limit, limit, width, s => At(s).X, (s, v) => At(s).X = v),
                new("Y" + unit, -limit, limit, width, s => At(s).Y, (s, v) => At(s).Y = v),
                new("Z" + unit, -limit, limit, width, s => At(s).Z, (s, v) => At(s).Z = v)
            ]);
        }
        else
        {
            parameters.AddRange([
                new("Степень бульба", 2, 12, 2, s => At(s).Power, (s, v) => At(s).Power = v),
                new("Масштаб бокса", -8, 8, .8m, s => At(s).Scale, (s, v) => At(s).Scale = v),
                new("Минимальный радиус", .01m, 4, .4m, s => At(s).MinRadius, (s, v) => At(s).MinRadius = v),
                new("Предел свёртки", .1m, 8, .4m, s => At(s).FoldLimit, (s, v) => At(s).FoldLimit = v),
                new("Подмешивание", 0, 1, .2m, s => At(s).Mix, (s, v) => At(s).Mix = v)
            ]);
            if (stage.Inversion is BoxInversionShape.Ellipsoid or BoxInversionShape.RoundedCube)
                parameters.Add(new("Вытяжение Z", .5m, 2, .4m, s => At(s).Stretch, (s, v) => At(s).Stretch = v));
            if (stage.Inversion == BoxInversionShape.RoundedCube)
                parameters.Add(new("Округлость инверсии", 2, 16, 2, s => At(s).InversionPower, (s, v) => At(s).InversionPower = v));
            // При нулевой смеси масштаб не влияет на чистый бульб: его начальные оси — степень и смесь.
        }
        parameters.Add(new("Повторов этапа", 1, 6, 2, s => At(s).Repeats, (s, v) => At(s).Repeats = (int)v, true));
        return parameters.ToArray();
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
        if (state.Kind != Fractal3DKind.BulbBoxHybrid || point.Length != _parameters.Length ||
            (state.HybridSequence is not null) != _custom)
            throw new ArgumentException("Состояние не соответствует режиму атласа гибрида.");
        if (_custom && ((uint)StageIndex >= state.HybridSequence!.Stages.Count ||
            state.HybridSequence.Stages[StageIndex].Operation != _operation))
            throw new ArgumentException("Выбранный этап цепочки изменился.");
        for (int i = 0; i < point.Length; i++)
            if (point[i] < _parameters[i].Minimum || point[i] > _parameters[i].Maximum ||
                _parameters[i].Integer && point[i] != decimal.Truncate(point[i]))
                throw new ArgumentException($"Параметр «{_parameters[i].Name}» вне диапазона или требует целого значения.");
        if (_custom) state.HybridSequence = state.HybridSequence!.Clone();
        for (int i = 0; i < point.Length; i++)
            _parameters[i].Write(state, point[i] == (decimal)_original[i] ? _original[i] : (double)point[i]);
        state.HybridSequence?.Validate();
    }
}
