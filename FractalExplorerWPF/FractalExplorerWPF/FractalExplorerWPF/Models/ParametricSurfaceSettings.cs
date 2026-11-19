using FractalExplorerWPF.Core.NewtonMath;
using System.Text.Json.Serialization;

namespace FractalExplorerWPF.Models;

public enum ParametricSurfaceFamily { Superformula, Equations }

public sealed record SuperformulaProfile(double M = 0, double N1 = 2, double N2 = 2, double N3 = 2,
    double A = 1, double B = 1)
{
    public const double MaxSymmetry = 32, MinExponent = .1, MaxExponent = 16, MinScale = .25, MaxScale = 4;

    internal void Validate(string name)
    {
        ParametricSurfaceSettings.Check(M, 0, MaxSymmetry, $"{name}: m");
        ParametricSurfaceSettings.Check(N1, MinExponent, MaxExponent, $"{name}: n₁");
        ParametricSurfaceSettings.Check(N2, MinExponent, MaxExponent, $"{name}: n₂");
        ParametricSurfaceSettings.Check(N3, MinExponent, MaxExponent, $"{name}: n₃");
        ParametricSurfaceSettings.Check(A, MinScale, MaxScale, $"{name}: a");
        ParametricSurfaceSettings.Check(B, MinScale, MaxScale, $"{name}: b");
    }

    internal double Radius(double angle)
    {
        double t = M * angle * .25;
        double sum = Math.Pow(Math.Abs(Math.Cos(t)) / A, N2) + Math.Pow(Math.Abs(Math.Sin(t)) / B, N3);
        return Math.Pow(sum, -1 / N1);
    }
}

public sealed record ParametricDomain(double UMin = -Math.PI, double UMax = Math.PI,
    double VMin = -Math.PI / 2, double VMax = Math.PI / 2)
{
    public const double Limit = 100;

    internal void Validate()
    {
        ParametricSurfaceSettings.Check(UMin, -Limit, Limit, "u от");
        ParametricSurfaceSettings.Check(UMax, -Limit, Limit, "u до");
        ParametricSurfaceSettings.Check(VMin, -Limit, Limit, "v от");
        ParametricSurfaceSettings.Check(VMax, -Limit, Limit, "v до");
        if (UMax - UMin < 1e-5 || VMax - VMin < 1e-5)
            throw new ArgumentException("Для каждой оси верхняя граница должна быть больше нижней хотя бы на 0.00001.");
    }
}

public sealed record ParametricEquations(
    string X = "(a+b*cos(v))*cos(u)", string Y = "b*sin(v)", string Z = "(a+b*cos(v))*sin(u)")
{
    internal CompiledRealSpaceExpression[] Compile() => [CompileAxis(X, "x"), CompileAxis(Y, "y"), CompileAxis(Z, "z")];

    internal static CompiledRealSpaceExpression CompileAxis(string? expression, string axis)
    {
        try { return CompiledRealSpaceExpression.ParseParametric(expression); }
        catch (ArgumentException error) { throw new ArgumentException($"{axis}(u,v): {error.Message}", error); }
    }
}

public sealed record ParametricSurfaceSettings
{
    public const int MinSegments = 16, MaxSegments = 192;
    public const int MinGridLines = 2, MaxGridLines = 48;
    public const double CoefficientLimit = 10;
    public const double CoordinateLimit = 1e8;

    public ParametricSurfaceFamily Family { get; init; }
    public SuperformulaProfile Longitude { get; init; } = new(8, .32, 1.5, 1.5);
    public SuperformulaProfile Latitude { get; init; } = new(4, 1.2, 2.4, 2.4, 1, .65);
    public bool LinkProfiles { get; init; }
    public ParametricEquations Equations { get; init; } = new();
    public ParametricDomain Domain { get; init; } = new(0, 2 * Math.PI, 0, 2 * Math.PI);
    public double A { get; init; } = 1;
    public double B { get; init; } = .36;
    public double C { get; init; } = .15;
    public int USegments { get; init; } = 112;
    public int VSegments { get; init; } = 72;
    public bool Fit { get; init; } = true;
    public bool ShowGrid { get; init; }
    public int GridLines { get; init; } = 12;

    [JsonIgnore]
    internal ParametricDomain ActiveDomain => Family == ParametricSurfaceFamily.Superformula ? new() : Domain;

    internal static string Name(ParametricSurfaceFamily family) =>
        family == ParametricSurfaceFamily.Superformula ? "Суперформула" : "Уравнения x/y/z";

    internal void Validate()
    {
        if (!Enum.IsDefined(Family)) throw new ArgumentException("Неизвестный способ построения поверхности.");
        Check(USegments, MinSegments, MaxSegments, "Разбиение u");
        Check(VSegments, MinSegments, MaxSegments, "Разбиение v");
        if (ShowGrid) Check(GridLines, MinGridLines, MaxGridLines, "Деления сетки");
        if (Family == ParametricSurfaceFamily.Superformula)
        {
            (Longitude ?? throw new ArgumentException("Отсутствует профиль долготы.")).Validate("Долгота");
            if (!LinkProfiles)
                (Latitude ?? throw new ArgumentException("Отсутствует профиль широты.")).Validate("Широта");
            return;
        }

        (Domain ?? throw new ArgumentException("Отсутствует область параметров.")).Validate();
        if (Equations is null) throw new ArgumentException("Отсутствуют уравнения поверхности.");
        Check(A, -CoefficientLimit, CoefficientLimit, "a");
        Check(B, -CoefficientLimit, CoefficientLimit, "b");
        Check(C, -CoefficientLimit, CoefficientLimit, "c");
    }

    internal static void Check(double value, double min, double max, string name)
    {
        if (!double.IsFinite(value) || value < min || value > max)
            throw new ArgumentException($"Параметр «{name}» должен быть в диапазоне {min:G}…{max:G}.");
    }

    // Changing a draft in the inactive family, the grid overlay or the camera leaves the mesh intact.
    internal ParametricSurfaceSettings GeometryKey() => Family == ParametricSurfaceFamily.Superformula
        ? this with { Equations = new(), Domain = new(), A = 0, B = 0, C = 0, ShowGrid = false, GridLines = 0,
            Latitude = LinkProfiles ? Longitude : Latitude, LinkProfiles = false }
        : this with { Longitude = new(), Latitude = new(), LinkProfiles = false, ShowGrid = false, GridLines = 0 };
}
