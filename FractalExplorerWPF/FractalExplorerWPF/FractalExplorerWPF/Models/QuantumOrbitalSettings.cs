namespace FractalExplorerWPF.Models;

public enum QuantumOrbitalBasis { Real, Complex }

/// <summary>Hydrogen orbitals in atomic units (a₀); display scale does not change ψ.</summary>
public sealed record QuantumOrbitalSettings
{
    public int N { get; set; } = 3;
    public int L { get; set; } = 2;
    public int M { get; set; } = 0;
    public QuantumOrbitalBasis Basis { get; set; }
    public bool Superposition { get; set; }
    public int N2 { get; set; } = 3;
    public int L2 { get; set; } = 2;
    public int M2 { get; set; } = 2;
    public double Mix { get; set; } = .5;
    public double Phase { get; set; }
    public double Extent { get; set; } = 24;
    public int Size { get; set; } = 128;
    public double Level { get; set; } = .06;
    public int CutAxis { get; set; }
    public double CutPosition { get; set; }

    public void Validate()
    {
        if (N is < 1 or > 8 || L < 0 || L >= N || Math.Abs((long)M) > L || !Enum.IsDefined(Basis))
            throw new ArgumentException("Орбиталь: 1 ≤ n ≤ 8, 0 ≤ l < n, −l ≤ m ≤ l.");
        if (Superposition && (N2 is < 1 or > 8 || L2 < 0 || L2 >= N2 || Math.Abs((long)M2) > L2))
            throw new ArgumentException("Вторая орбиталь: 1 ≤ n ≤ 8, 0 ≤ l < n, −l ≤ m ≤ l.");
        if (!double.IsFinite(Extent) || Extent is < 2 or > 256 || Size is not (64 or 96 or 128 or 192) ||
            !double.IsFinite(Level) || Level is < .000001 or > .8 || CutAxis is < 0 or > 3 ||
            !double.IsFinite(CutPosition) || CutPosition is < -1 or > 1 ||
            (Superposition && (!double.IsFinite(Mix) || Mix is < 0 or > 1 || !double.IsFinite(Phase) || Phase is < -180 or > 180)))
            throw new ArgumentException("Проверьте область, разрешение, уровень плотности и параметры смеси.");
        if (Superposition && N == N2 && L == L2 && M == M2 &&
            Math.Abs(Mix - .5) < 1e-12 && Math.Abs(Math.IEEERemainder(Phase, 360)) > 179.999999)
            throw new ArgumentException("Одинаковые состояния в противофазе дают нулевую волну. Выберите разные орбитали.");
    }

    public bool SameField(QuantumOrbitalSettings s) => N == s.N && L == s.L && M == s.M && Basis == s.Basis &&
        Size == s.Size && Extent == s.Extent && Superposition == s.Superposition &&
        (!Superposition || (N2 == s.N2 && L2 == s.L2 && M2 == s.M2 && Mix == s.Mix && Phase == s.Phase));

    public static string Label(int n, int l, int m) => $"{n}{"spdfghik"[Math.Clamp(l, 0, 7)]} · m = {m}";
}
