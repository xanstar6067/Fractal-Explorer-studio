namespace FractalExplorerWPF.Models;

public enum Percolation3DMedium { Independent, Correlated }
public enum Percolation3DView { Clusters, Largest, Spanning, Route }
public enum Percolation3DAxis { X, Y, Z }
public enum Percolation3DCut { None, Cutaway, Layer }

public sealed record Percolation3DSettings
{
    public const int MinSize = 12;
    public const int MaxSize = 96;
    public const double CriticalProbability = 0.3116077;

    public int Size { get; init; } = 48;
    public int Seed { get; init; } = 42;
    public double Probability { get; init; } = 0.336;
    public Percolation3DMedium Medium { get; init; }
    public Percolation3DAxis Axis { get; init; } = Percolation3DAxis.Y;
    public Percolation3DView View { get; init; } = Percolation3DView.Route;
    public bool ShowPath { get; init; } = true;
    public Percolation3DCut Cut { get; init; }
    public Percolation3DAxis CutAxis { get; init; } = Percolation3DAxis.Z;
    public double CutPosition { get; init; } = 0.55;

    internal bool SameField(Percolation3DSettings other) =>
        (Size, Seed, Probability, Medium, Axis) == (other.Size, other.Seed, other.Probability, other.Medium, other.Axis);

    internal int SliceIndex => Math.Min(Size - 1, (int)(CutPosition * Size));

    internal void Validate()
    {
        if (Size is < MinSize or > MaxSize)
            throw new ArgumentOutOfRangeException(nameof(Size), $"Сторона решётки должна быть от {MinSize} до {MaxSize}.");
        if (!double.IsFinite(Probability) || Probability is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(Probability), "Порог должен быть от 0 до 1.");
        if (!double.IsFinite(CutPosition) || CutPosition is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(CutPosition), "Положение среза должно быть от 0 до 1.");
        if (!Enum.IsDefined(Medium) || !Enum.IsDefined(Axis) || !Enum.IsDefined(View) ||
            !Enum.IsDefined(Cut) || !Enum.IsDefined(CutAxis))
            throw new ArgumentException("Неизвестный вариант среды, оси или вида перколяции.");
    }
}
