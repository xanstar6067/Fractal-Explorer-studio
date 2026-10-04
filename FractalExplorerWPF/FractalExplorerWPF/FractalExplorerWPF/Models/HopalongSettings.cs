namespace FractalExplorerWPF.Models;

public sealed class HopalongSettings
{
    public double A { get; set; } = 1.1;
    public double B { get; set; } = 1;
    public double C { get; set; }
    public double StartX { get; set; }
    public double StartY { get; set; }
    public double Rotation { get; set; }
    public double Span { get; set; } = 4.4;

    public HopalongSettings Clone() => (HopalongSettings)MemberwiseClone();

    public void Validate()
    {
        if (!double.IsFinite(A) || Math.Abs(A) > 10_000 ||
            !double.IsFinite(B) || Math.Abs(B) > 100 ||
            !double.IsFinite(C) || Math.Abs(C) > 10_000)
            throw new InvalidOperationException("Параметры a и c: −10000…10000; b: −100…100.");
        if (!double.IsFinite(StartX) || !double.IsFinite(StartY) ||
            Math.Abs(StartX) > 1e6 || Math.Abs(StartY) > 1e6)
            throw new InvalidOperationException("Начальные координаты должны быть от −1000000 до 1000000.");
        if (!double.IsFinite(Rotation) || Math.Abs(Rotation) > 360_000 ||
            !double.IsFinite(Span) || Span is < .0001 or > 1e8)
            throw new InvalidOperationException("Поворот: −360000…360000°; размер кадра: 0,0001…100000000.");
    }
}

public sealed record HopalongPreset(string Name, string Id, HopalongSettings Settings,
    double CenterX, double CenterY, string PaletteName)
{
    public override string ToString() => Name;
}

public static class HopalongPresets
{
    // Parameter sets from Martin Lanter's Hopalong gallery. The stored framing
    // shows the dense central region; it is not a claim that the orbit is bounded.
    public static IReadOnlyList<HopalongPreset> All { get; } =
    [
        new("Острова в хаосе", "chaos", new() { B = -.5, C = 1, Span = 57 }, .48, .62, "Hopalong — бирюза"),
        new("Орбитальная розетка", "rosette", new() { A = 2, Span = 4.9 }, .38, 1.62, "Hopalong — золото"),
        new("Лепестки Мартина", "petals", new() { A = 7.3, Span = 14.4 }, 2.84, 4.46, "Hopalong — аметист"),
        new("Кольца и вихри", "rings", new() { A = 5, C = 20, Span = 280 }, 2.55, 2.45, "Hopalong — лёд"),
        new("Тонкая гравюра", "engraving", new() { A = -11, B = .05, C = .5, Span = 21 }, -5.1, -5.9, "Hopalong — золото"),
        new("Кружевные острова", "lace", new() { Span = 4.4 }, .05, 1.05, "Hopalong — бирюза")
    ];

    public static void Apply(DynamicSystemState state, int index)
    {
        HopalongPreset preset = All[index];
        state.Hopalong = preset.Settings.Clone();
        state.CenterX = preset.CenterX; state.CenterY = preset.CenterY;
        state.Zoom = 1; state.Iterations = 2_000_000; state.DensityGamma = .55;
        state.PaletteName = preset.PaletteName;
        state.PointOfInterestId = "hopalong_" + preset.Id;
    }

    public static IReadOnlyList<DynamicSystemState> PointsOfInterest() =>
        Enumerable.Range(0, All.Count).Select(index =>
        {
            var state = new DynamicSystemState { Kind = DynamicSystemKind.Hopalong };
            Apply(state, index);
            state.SaveName = All[index].Name; state.Timestamp = DateTime.MinValue;
            return state;
        }).ToArray();
}
