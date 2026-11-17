namespace FractalExplorerWPF.Models;

public enum Polytope4DKind { Simplex, Tesseract, Orthoplex, Cell24, Cell120, Cell600 }
public enum Polytope4DView { Projection, Section, SectionWithProjection }
public enum Polytope4DAnimation { XY, XZ, XW, YZ, YW, ZW, Section }

/// <summary>All bodies have circumradius one. Rotations precede the section w = Offset.</summary>
public sealed record Polytope4DSettings
{
    public Polytope4DKind Body { get; init; } = Polytope4DKind.Tesseract;
    public Polytope4DView View { get; init; } = Polytope4DView.Projection;
    public bool Perspective { get; init; } = true;
    public double EyeDistance { get; init; } = 2.5;
    public double Offset { get; init; }
    public bool Faces { get; init; } = true;
    public bool Edges { get; init; } = true;
    public bool Vertices { get; init; }
    public double Thickness { get; init; } = .012;
    public double XY { get; init; }
    public double XZ { get; init; }
    public double XW { get; init; } = 22;
    public double YZ { get; init; }
    public double YW { get; init; } = 15;
    public double ZW { get; init; }
    public Polytope4DAnimation Animation { get; init; } = Polytope4DAnimation.XW;
    public double Speed { get; init; } = 18;

    public void Validate()
    {
        if (!Enum.IsDefined(Body) || !Enum.IsDefined(View) || !Enum.IsDefined(Animation) ||
            !Finite(EyeDistance, 1.25, 6) || !Finite(Offset, -1.05, 1.05) ||
            !Finite(Thickness, .003, .04) || !Finite(Speed, -60, 60) ||
            new[] { XY, XZ, XW, YZ, YW, ZW }.Any(a => !Finite(a, -180, 180)))
            throw new ArgumentException("Проверьте параметры многогранника: углы ±180°, срез ±1,05, перспектива 1,25–6.");
        if (View != Polytope4DView.Projection && !Faces && !Edges && !Vertices)
            throw new ArgumentException("Включите грани, рёбра или вершины сечения.");
    }

    public static string Name(Polytope4DKind kind) => kind switch
    {
        Polytope4DKind.Simplex => "5-ячейник · симплекс",
        Polytope4DKind.Tesseract => "8-ячейник · тессеракт",
        Polytope4DKind.Orthoplex => "16-ячейник · ортоплекс",
        Polytope4DKind.Cell24 => "24-ячейник",
        Polytope4DKind.Cell120 => "120-ячейник",
        _ => "600-ячейник"
    };

    public static string ViewName(Polytope4DView view) => view switch
    {
        Polytope4DView.Projection => "Проекция каркаса",
        Polytope4DView.Section => "Сечение w = t",
        _ => "Сечение и каркас"
    };

    private static bool Finite(double x, double lo, double hi) => double.IsFinite(x) && x >= lo && x <= hi;
}
