namespace FractalExplorerWPF.Models;

public sealed record FoldedFormulaDefinition(MandelbrotVariant Parameter, MandelbrotVariant Julia,
    string Name, int Degree, string Kind, decimal JuliaReal, decimal JuliaImaginary, decimal AlternativeReal, decimal AlternativeImaginary)
{
    public string FormulaDescription => $"{Name}: " + (Degree == 3 ? "кубический полином" : Degree == 4 ? "полином четвёртой степени" : "полином пятой степени") +
        (Kind.Contains("BurningShip") ? "; горящий корабль" : Kind.Contains("Buffalo") ? "; Буффало" : Kind.Contains("Celtic") ? "; кельтский вариант" : Kind.Contains("Mandelbar") ? "; Трикорн, сопряжение" : "") +
        (Kind.Contains("Partial") ? "; частичное отражение" : "") + (Kind.Contains("Heart") ? "; квази сердце" : "") +
        ". Затем добавляется C. Глубокий зум, палитры и сохранения.";
}

public static class FoldedFormulaCatalog
{
    public static IReadOnlyList<FoldedFormulaDefinition> All { get; } =
    [
        new(MandelbrotVariant.CubicBurningShip, MandelbrotVariant.JuliaCubicBurningShip, "Cubic Burning Ship", 3, "BurningShip", -0.425m, -0.5525m, 0.5525m, 0.425m),
        new(MandelbrotVariant.CubicBuffalo, MandelbrotVariant.JuliaCubicBuffalo, "Cubic Buffalo", 3, "Buffalo", -0.2975m, -1.275m, -1.275m, -0.2975m),
        new(MandelbrotVariant.CubicCeltic, MandelbrotVariant.JuliaCubicCeltic, "Cubic Celtic", 3, "Celtic", -0.595m, 0.6375m, -0.595m, -0.6375m),
        new(MandelbrotVariant.CubicMandelbar, MandelbrotVariant.JuliaCubicMandelbar, "Cubic Mandelbar", 3, "Mandelbar", -0.5593m, -0.7191m, -0.25m, -0.35m),
        new(MandelbrotVariant.QuarticBurningShip, MandelbrotVariant.JuliaQuarticBurningShip, "Quartic Burning Ship", 4, "BurningShip", -0.7225m, -0.425m, 0.5525m, -0.2975m),
        new(MandelbrotVariant.QuarticBuffalo, MandelbrotVariant.JuliaQuarticBuffalo, "Quartic Buffalo", 4, "Buffalo", -0.595m, -1.105m, -1.02m, -0.595m),
        new(MandelbrotVariant.QuarticCeltic, MandelbrotVariant.JuliaQuarticCeltic, "Quartic Celtic", 4, "Celtic", -0.935m, -0.1275m, -0.935m, -0.1275m),
        new(MandelbrotVariant.QuarticMandelbar, MandelbrotVariant.JuliaQuarticMandelbar, "Quartic Mandelbar", 4, "Mandelbar", -0.255m, -0.5525m, -0.255m, 0.5525m),
        new(MandelbrotVariant.QuinticBurningShip, MandelbrotVariant.JuliaQuinticBurningShip, "Quintic Burning Ship", 5, "BurningShip", 0.6375m, -0.2975m, -0.2975m, 0.6375m),
        new(MandelbrotVariant.QuinticBuffalo, MandelbrotVariant.JuliaQuinticBuffalo, "Quintic Buffalo", 5, "Buffalo", -0.425m, -0.9775m, -0.9775m, -0.425m),
        new(MandelbrotVariant.QuinticCeltic, MandelbrotVariant.JuliaQuinticCeltic, "Quintic Celtic", 5, "Celtic", -0.255m, -0.595m, -0.2975m, 0.6375m),
        new(MandelbrotVariant.QuinticMandelbar, MandelbrotVariant.JuliaQuinticMandelbar, "Quintic Mandelbar", 5, "Mandelbar", -0.5525m, -0.3825m, 0.5525m, -0.3825m),
        new(MandelbrotVariant.CubicPartialBurningShipReal, MandelbrotVariant.JuliaCubicPartialBurningShipReal, "Cubic Partial Burning Ship Real", 3, "PartialBurningShipReal", 0.5525m, -0.2975m, 0.5525m, 0.2975m),
        new(MandelbrotVariant.CubicPartialBurningShipImag, MandelbrotVariant.JuliaCubicPartialBurningShipImag, "Cubic Partial Burning Ship Imag", 3, "PartialBurningShipImag", -0.4675m, 0.0425m, 0.4675m, 0.0425m),
        new(MandelbrotVariant.CubicQuasiPerpendicular, MandelbrotVariant.JuliaCubicQuasiPerpendicular, "Cubic Quasi Perpendicular", 3, "QuasiPerpendicular", 0.68m, 0.8925m, 0.68m, -0.8925m),
        new(MandelbrotVariant.CubicCelticQuasiPerpendicular, MandelbrotVariant.JuliaCubicCelticQuasiPerpendicular, "Cubic Celtic Quasi Perpendicular", 3, "CelticQuasiPerpendicular", -0.8075m, 0.85m, 0.0425m, 0.85m),
        new(MandelbrotVariant.CubicQuasiPerpendicularBurningShip, MandelbrotVariant.JuliaCubicQuasiPerpendicularBurningShip, "Cubic Quasi Perpendicular Burning Ship", 3, "QuasiPerpendicularBurningShip", -0.51m, 0.2975m, -0.9775m, -0.2125m),
        new(MandelbrotVariant.CubicQuasiPerpendicularBuffalo, MandelbrotVariant.JuliaCubicQuasiPerpendicularBuffalo, "Cubic Quasi Perpendicular Buffalo", 3, "QuasiPerpendicularBuffalo", -0.255m, 0.68m, -0.5525m, 0.2975m),
        new(MandelbrotVariant.QuarticPartialBurningShipImag, MandelbrotVariant.JuliaQuarticPartialBurningShipImag, "Quartic Partial Burning Ship Imag", 4, "PartialBurningShipImag", -0.255m, -0.5525m, 0.5525m, -0.2975m),
        new(MandelbrotVariant.QuarticPartialBurningShipReal, MandelbrotVariant.JuliaQuarticPartialBurningShipReal, "Quartic Partial Burning Ship Real", 4, "PartialBurningShipReal", 0.6375m, -0.2975m, 0.6375m, 0.2975m),
        new(MandelbrotVariant.QuarticPartialBurningShipRealMandelbar, MandelbrotVariant.JuliaQuarticPartialBurningShipRealMandelbar, "Quartic Partial Burning Ship Real Mandelbar", 4, "PartialBurningShipRealMandelbar", -0.4395m, -0.4794m, -0.25m, -0.35m),
        new(MandelbrotVariant.QuarticCelticPartialBurningShipImag, MandelbrotVariant.JuliaQuarticCelticPartialBurningShipImag, "Quartic Celtic Partial Burning Ship Imag", 4, "CelticPartialBurningShipImag", -0.935m, -0.085m, -0.255m, 0.5525m),
        new(MandelbrotVariant.QuarticCelticPartialBurningShipReal, MandelbrotVariant.JuliaQuarticCelticPartialBurningShipReal, "Quartic Celtic Partial Burning Ship Real", 4, "CelticPartialBurningShipReal", -0.935m, 0.085m, -0.6375m, -0.935m),
        new(MandelbrotVariant.QuarticCelticPartialBurningShipRealMandelbar, MandelbrotVariant.JuliaQuarticCelticPartialBurningShipRealMandelbar, "Quartic Celtic Partial Burning Ship Real Mandelbar", 4, "CelticPartialBurningShipRealMandelbar", 0.5525m, -0.3825m, 0.5525m, -0.3825m),
        new(MandelbrotVariant.QuarticBuffaloPartialImag, MandelbrotVariant.JuliaQuarticBuffaloPartialImag, "Quartic Buffalo Partial Imag", 4, "BuffaloPartialImag", 0.595m, -0.765m, -0.17m, -0.9775m),
        new(MandelbrotVariant.QuarticCelticMandelbar, MandelbrotVariant.JuliaQuarticCelticMandelbar, "Quartic Celtic Mandelbar", 4, "CelticMandelbar", -0.935m, 0.085m, -0.85m, -0.17m),
        new(MandelbrotVariant.QuarticFalseQuasiPerpendicular, MandelbrotVariant.JuliaQuarticFalseQuasiPerpendicular, "Quartic False Quasi Perpendicular", 4, "FalseQuasiPerpendicular", 0.6375m, 0.6375m, 0.6375m, -0.6375m),
        new(MandelbrotVariant.QuarticFalseQuasiHeart, MandelbrotVariant.JuliaQuarticFalseQuasiHeart, "Quartic False Quasi Heart", 4, "FalseQuasiHeart", -0.3825m, -0.8925m, -0.3825m, 0.8925m),
        new(MandelbrotVariant.QuarticCelticFalseQuasiPerpendicular, MandelbrotVariant.JuliaQuarticCelticFalseQuasiPerpendicular, "Quartic Celtic False Quasi Perpendicular", 4, "CelticFalseQuasiPerpendicular", -0.8075m, -0.255m, -0.8075m, -0.255m),
        new(MandelbrotVariant.QuarticCelticFalseQuasiHeart, MandelbrotVariant.JuliaQuarticCelticFalseQuasiHeart, "Quartic Celtic False Quasi Heart", 4, "CelticFalseQuasiHeart", -0.765m, 0.6375m, -0.765m, -0.6375m),
        new(MandelbrotVariant.QuarticImagQuasi, MandelbrotVariant.JuliaQuarticImagQuasi, "Quartic Imag Quasi", 4, "ImagQuasi", 0.6375m, -0.6375m, -0.255m, 0.6375m),
        new(MandelbrotVariant.QuarticRealQuasiPerpendicular, MandelbrotVariant.JuliaQuarticRealQuasiPerpendicular, "Quartic Real Quasi Perpendicular", 4, "RealQuasiPerpendicular", 0.34m, -0.8075m, 0.34m, 0.8075m),
        new(MandelbrotVariant.QuarticRealQuasiHeart, MandelbrotVariant.JuliaQuarticRealQuasiHeart, "Quartic Real Quasi Heart", 4, "RealQuasiHeart", -0.6375m, -0.085m, -0.255m, -0.425m),
        new(MandelbrotVariant.QuarticCelticImagQuasi, MandelbrotVariant.JuliaQuarticCelticImagQuasi, "Quartic Celtic Imag Quasi", 4, "CelticImagQuasi", -0.765m, 0.6375m, -0.8075m, -0.255m),
        new(MandelbrotVariant.QuarticCelticRealQuasiPerpendicular, MandelbrotVariant.JuliaQuarticCelticRealQuasiPerpendicular, "Quartic Celtic Real Quasi Perpendicular", 4, "CelticRealQuasiPerpendicular", -0.425m, 0.9775m, -0.425m, -0.9775m),
        new(MandelbrotVariant.QuarticCelticRealQuasiHeart, MandelbrotVariant.JuliaQuarticCelticRealQuasiHeart, "Quartic Celtic Real Quasi Heart", 4, "CelticRealQuasiHeart", -0.7225m, 0.34m, -0.7225m, -0.34m),
        new(MandelbrotVariant.QuinticPartialBurningShipReal, MandelbrotVariant.JuliaQuinticPartialBurningShipReal, "Quintic Partial Burning Ship Real", 5, "PartialBurningShipReal", -0.3825m, -0.5525m, -0.3825m, 0.5525m),
        new(MandelbrotVariant.QuinticPartialBurningShipRealMandelbar, MandelbrotVariant.JuliaQuinticPartialBurningShipRealMandelbar, "Quintic Partial Burning Ship Real Mandelbar", 5, "PartialBurningShipRealMandelbar", 0.51m, -0.3825m, 0.51m, 0.3825m),
        new(MandelbrotVariant.QuinticCelticMandelbar, MandelbrotVariant.JuliaQuinticCelticMandelbar, "Quintic Celtic Mandelbar", 5, "CelticMandelbar", -0.51m, -0.3825m, -0.51m, 0.3825m),
        new(MandelbrotVariant.QuinticQuasiBurningShip, MandelbrotVariant.JuliaQuinticQuasiBurningShip, "Quintic Quasi Burning Ship", 5, "QuasiBurningShip", -0.3825m, 0.51m, -0.51m, 0.765m),
        new(MandelbrotVariant.QuinticQuasiPerpendicular, MandelbrotVariant.JuliaQuinticQuasiPerpendicular, "Quintic Quasi Perpendicular", 5, "QuasiPerpendicular", -0.3825m, -0.51m, -0.3825m, 0.51m),
        new(MandelbrotVariant.QuinticQuasiHeart, MandelbrotVariant.JuliaQuinticQuasiHeart, "Quintic Quasi Heart", 5, "QuasiHeart", 0.68m, -0.425m, 0.68m, 0.425m),
        new(MandelbrotVariant.QuinticQuasiPerpendicularBurningShip, MandelbrotVariant.JuliaQuinticQuasiPerpendicularBurningShip, "Quintic Quasi Perpendicular Burning Ship", 5, "QuasiPerpendicularBurningShip", -0.425m, -0.68m, -0.8075m, -0.595m),
        new(MandelbrotVariant.QuinticQuasiPerpendicularBuffalo, MandelbrotVariant.JuliaQuinticQuasiPerpendicularBuffalo, "Quintic Quasi Perpendicular Buffalo", 5, "QuasiPerpendicularBuffalo", 0.085m, 0.8925m, -0.8925m, -0.085m),
        new(MandelbrotVariant.QuinticCelticQuasiPerpendicular, MandelbrotVariant.JuliaQuinticCelticQuasiPerpendicular, "Quintic Celtic Quasi Perpendicular", 5, "CelticQuasiPerpendicular", -0.2125m, 1.02m, 0.51m, -0.425m),
        new(MandelbrotVariant.QuinticCelticQuasiHeart, MandelbrotVariant.JuliaQuinticCelticQuasiHeart, "Quintic Celtic Quasi Heart", 5, "CelticQuasiHeart", -0.765m, -0.2975m, -0.51m, -0.3825m),
    ];
    private static readonly Dictionary<MandelbrotVariant, FoldedFormulaDefinition> ByVariant =
        All.SelectMany(d => new[] { KeyValuePair.Create(d.Parameter, d), KeyValuePair.Create(d.Julia, d) }).ToDictionary();
    public static bool IsProgram(MandelbrotVariant v) => ByVariant.ContainsKey(v) || IsHybrid(v);
    public static bool IsHybrid(MandelbrotVariant v) => v is MandelbrotVariant.Hybrid or MandelbrotVariant.JuliaHybrid;
    public static bool IsJulia(MandelbrotVariant v) => v == MandelbrotVariant.JuliaHybrid ||
        ByVariant.TryGetValue(v, out var d) && v == d.Julia;
    public static MandelbrotVariant ParameterVariant(MandelbrotVariant v) => v == MandelbrotVariant.JuliaHybrid
        ? MandelbrotVariant.Hybrid : ByVariant.TryGetValue(v, out var d) ? d.Parameter : v;
    public static FoldedFormulaDefinition? Find(MandelbrotVariant v) => ByVariant.GetValueOrDefault(v);
    public static MandelbrotVariantDefinition Definition(MandelbrotVariant v)
    {
        bool julia = IsJulia(v);
        if (IsHybrid(v)) return new(v, julia ? "Конструктор гибридов (Жюлиа)" : "Конструктор гибридов Мандельброта",
            v.ToString(), julia ? 0 : -0.35m, 0, 0.75, HasJuliaConstant: julia,
            DefaultJuliaReal: -0.4m, DefaultJuliaImaginary: -0.2m);
        var d = Find(v) ?? throw new ArgumentOutOfRangeException(nameof(v));
        return new(v, d.Name + (julia ? " (Жюлиа)" : ""), v.ToString(), 0, 0, 0.75,
            DefaultPower: d.Degree, HasJuliaConstant: julia,
            DefaultJuliaReal: d.JuliaReal, DefaultJuliaImaginary: d.JuliaImaginary);
    }
}

public sealed class HybridFormulaStep
{
    public MandelbrotVariant Formula { get; set; } = MandelbrotVariant.Mandelbrot;
    public int Power { get; set; } = 2;
    public int Repeats { get; set; } = 1;
    public HybridFormulaStep Clone() => new() { Formula = Formula, Power = Power, Repeats = Repeats };
}

public sealed class HybridFormulaSettings
{
    public List<HybridFormulaStep> Steps { get; set; } =
    [new() { Repeats = 2 }, new() { Formula = MandelbrotVariant.BurningShip }];
    public HybridFormulaSettings Clone() => new() { Steps = Steps.Select(s => s.Clone()).ToList() };
    public void Validate()
    {
        if (Steps is null || Steps.Count is < 1 or > 16)
            throw new ArgumentException("В последовательности должно быть от 1 до 16 строк.");
        int length = 0;
        foreach (var step in Steps)
        {
            if (step is null || !HybridFormulaChoices.Contains(step.Formula) || step.Repeats is < 1 or > 32 || step.Power is < 2 or > 5)
                throw new ArgumentException("Выберите формулу, степень 2–5 и от 1 до 32 повторов.");
            length += step.Repeats;
        }
        if (length > 256) throw new ArgumentException("Цикл не должен превышать 256 шагов.");
    }
    public static IReadOnlyList<MandelbrotVariant> HybridFormulaChoices { get; } =
        new[] { MandelbrotVariant.Mandelbrot, MandelbrotVariant.BurningShip, MandelbrotVariant.Tricorn,
            MandelbrotVariant.Buffalo, MandelbrotVariant.Celtic, MandelbrotVariant.PerpendicularMandelbrot,
            MandelbrotVariant.PerpendicularBurningShip, MandelbrotVariant.PerpendicularCeltic,
            MandelbrotVariant.PerpendicularBuffalo, MandelbrotVariant.CelticMandelbar,
            MandelbrotVariant.CubicQuasiBurningShip, MandelbrotVariant.CubicFlyingSquirrel }
        .Concat(FoldedFormulaCatalog.All.Select(d => d.Parameter)).ToArray();
}
