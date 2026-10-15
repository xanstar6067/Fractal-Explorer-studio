namespace FractalExplorerWPF.Models;

public enum Newton3DRegion { Boundary, PlusBasin, RootPair }

/// <summary>Срез w = const бикомплексного Ньютона для q^n − 1, q = x + iy + jz + ijw.</summary>
public sealed record Newton3DSettings
{
    public int Size { get; init; } = 128;
    public int Degree { get; init; } = 3;
    public int MaxIterations { get; init; } = 64;
    public double Extent { get; init; } = 1.8;
    public double SliceW { get; init; } = .22;
    public double Relaxation { get; init; } = 1;
    public Newton3DRegion Region { get; init; } = Newton3DRegion.Boundary;
    public int BoundaryIterations { get; init; } = 12;
    public int PlusRoot { get; init; }
    public int MinusRoot { get; init; }
    public int CutAxis { get; init; } = 3;
    public double CutPosition { get; init; } = .2;
    public bool SliceOnly { get; init; }

    public void Validate()
    {
        if (Size is not (32 or 64 or 96 or 128 or 192))
            throw new ArgumentException("Сетка Ньютона: 32³, 64³, 96³, 128³ или 192³.");
        if (Degree is < 3 or > 8 || MaxIterations is < 16 or > 128 ||
            BoundaryIterations < 2 || BoundaryIterations >= MaxIterations)
            throw new ArgumentException("Степень: 3–8; итерации: 16–128; порог границы: от 2 до числа итераций минус 1.");
        if (!double.IsFinite(Extent) || Extent is < .2 or > 4 ||
            !double.IsFinite(SliceW) || SliceW is < -2 or > 2 ||
            !double.IsFinite(Relaxation) || Relaxation is < .2 or > 1.8)
            throw new ArgumentException("Полуразмер: 0,2–4; координата w: −2…2; релаксация: 0,2–1,8.");
        if (!Enum.IsDefined(Region) || PlusRoot < 0 || PlusRoot >= Degree || MinusRoot < 0 || MinusRoot >= Degree ||
            CutAxis is < 0 or > 3 || !double.IsFinite(CutPosition) || CutPosition is < -1 or > 1 ||
            SliceOnly && CutAxis == 0)
            throw new ArgumentException("Некорректный бассейн или сечение Ньютона.");
    }

    // Срез экрана, камера и палитра используют готовую текстуру; выбор области меняет маску.
    public bool SameField(Newton3DSettings other) =>
        this with { CutAxis = 0, CutPosition = 0, SliceOnly = false } ==
        other with { CutAxis = 0, CutPosition = 0, SliceOnly = false };
}
