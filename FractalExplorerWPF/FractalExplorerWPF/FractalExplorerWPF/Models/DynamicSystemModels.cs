using System.Text.Json.Serialization;
using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Models;

public enum DynamicSystemKind
{
    Lyapunov,
    Lorenz,
    Rossler,
    LogisticMap,
    Bifurcation,
    Henon,
    Ikeda,
    Attractors2D
}

public enum Attractor2DKind
{
    Clifford,
    PeterDeJong,
    Tinkerbell,
    GumowskiMira,
    SprottQuadratic
}

public sealed class DynamicSystemState
{
    public string SaveName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string? PointOfInterestId { get; set; }
    public DynamicSystemKind Kind { get; set; }
    public double CenterX { get; set; }
    public double CenterY { get; set; }
    public double Zoom { get; set; } = 1;
    [JsonIgnore]
    public int Threads { get; set; } = Environment.ProcessorCount;
    public int SsaaFactor { get; set; } = 1;
    public Color BackgroundColor { get; set; } = Colors.Black;
    public Color FractalColor { get; set; } = Colors.White;
    public string PaletteName { get; set; } = string.Empty;

    public double AMin { get; set; } = 2.5;
    public double AMax { get; set; } = 4;
    public double BMin { get; set; } = 2.5;
    public double BMax { get; set; } = 4;
    public string Pattern { get; set; } = "AB";
    public int Iterations { get; set; } = 300;
    public int TransientIterations { get; set; } = 100;

    public double Sigma { get; set; } = 10;
    public double Rho { get; set; } = 28;
    public double Beta { get; set; } = 2.666666;
    public double Dt { get; set; } = .01;
    public int Steps { get; set; } = 120_000;
    public double StartX { get; set; } = .01;
    public double StartY { get; set; }
    public double StartZ { get; set; }
    public string ProjectionMode { get; set; } = "XY";

    public double A { get; set; } = 1.4;
    public double B { get; set; } = .3;
    public double C { get; set; } = 5.7;
    public double D { get; set; }
    public double R { get; set; } = 3.8;
    public double U { get; set; } = .918;
    public double X0 { get; set; } = .1;
    public double Y0 { get; set; }
    public int DiscardIterations { get; set; } = 500;
    public string Attractor2DMode { get; set; } = nameof(Attractor2DKind.Clifford);
    public double DensityGamma { get; set; } = .65;
    public double[] QuadraticCoefficients { get; set; } = new double[12];
    public double QuadraticSpan { get; set; } = 5;
    public string VisualizationMode { get; set; } = "Orbit";
    public double BifurcationRMin { get; set; } = 2.8;
    public double BifurcationRMax { get; set; } = 4;
    public int BifurcationSamples { get; set; } = 1600;
    public int BifurcationTransient { get; set; } = 500;
    public int BifurcationPlottedPoints { get; set; } = 240;
    public int CobwebSteps { get; set; } = 40;
    public double RMin { get; set; } = 2.8;
    public double RMax { get; set; } = 4;
    public double XMin { get; set; }
    public double XMax { get; set; } = 1;
    public int SamplesPerR { get; set; } = 240;
    public double RangeXMin { get; set; } = -2;
    public double RangeXMax { get; set; } = 2;
    public double RangeYMin { get; set; } = -2;
    public double RangeYMax { get; set; } = 2;

    public DynamicSystemState Clone(string? name = null)
    {
        var clone = (DynamicSystemState)MemberwiseClone();
        clone.QuadraticCoefficients = QuadraticCoefficients?.ToArray() ?? new double[12];
        if (name is not null) clone.SaveName = name;
        return clone;
    }

    public static DynamicSystemState CreateDefault(DynamicSystemKind kind)
    {
        var state = new DynamicSystemState { Kind = kind };
        switch (kind)
        {
            case DynamicSystemKind.Lyapunov:
                state.PaletteName = "Классическая Ляпунова";
                state.CenterX = 3.25; state.CenterY = 3.25;
                break;
            case DynamicSystemKind.Lorenz:
                state.CenterY = 25; state.X0 = .01;
                break;
            case DynamicSystemKind.Rossler:
                state.A = .2; state.B = .2; state.C = 5.7; state.Steps = 150_000; state.StartX = .1;
                break;
            case DynamicSystemKind.LogisticMap:
                state.CenterX = .5; state.CenterY = .5; state.X0 = .2; state.Iterations = 2500;
                state.PaletteName = "Орбиты: периодические полосы";
                break;
            case DynamicSystemKind.Bifurcation:
                state.CenterX = 3.4; state.CenterY = .5; state.Iterations = 1200;
                break;
            case DynamicSystemKind.Henon:
                state.Iterations = 500_000; state.X0 = .1;
                break;
            case DynamicSystemKind.Ikeda:
                state.Iterations = 1_000_000; state.X0 = .1; state.Y0 = .1;
                break;
            case DynamicSystemKind.Attractors2D:
                state.Iterations = 2_000_000;
                state.DiscardIterations = 1_000;
                state.FractalColor = Color.FromRgb(126, 224, 255);
                state.ApplyAttractor2DPreset(Attractor2DKind.Clifford);
                break;
        }
        return state;
    }

    public void ApplyAttractor2DPreset(Attractor2DKind kind)
    {
        Attractor2DMode = kind.ToString();
        Zoom = 1;

        switch (kind)
        {
            case Attractor2DKind.Clifford:
                A = -1.4; B = 1.6; C = 1; D = .7;
                X0 = .1; Y0 = .1; CenterX = 0; CenterY = 0;
                break;
            case Attractor2DKind.PeterDeJong:
                A = 1.4; B = -2.3; C = 2.4; D = -2.1;
                X0 = 0; Y0 = 0; CenterX = 0; CenterY = 0;
                break;
            case Attractor2DKind.Tinkerbell:
                A = .9; B = -.6013; C = 2; D = .5;
                X0 = -.72; Y0 = -.64; CenterX = 0; CenterY = -.3;
                break;
            case Attractor2DKind.GumowskiMira:
                A = .008; B = .05; C = -.496; D = 0;
                X0 = .1; Y0 = 0; CenterX = 0; CenterY = 0;
                break;
            case Attractor2DKind.SprottQuadratic:
                X0 = .05; Y0 = .05; CenterX = 0; CenterY = 0;
                QuadraticSpan = 5;
                break;
        }
    }
}

public sealed class DynamicPalette
{
    public string Name { get; set; } = "Новая палитра";
    public List<Color> Colors { get; set; } = [];
    public string Mode { get; set; } = "Diverging";
    public double ExponentRange { get; set; } = 2;
    public double ZeroBandWidth { get; set; } = .05;
    public bool IsBuiltIn { get; set; }
    public DynamicPalette Clone(string? name = null) => new() { Name = name ?? Name, Colors = Colors.ToList(), Mode = Mode, ExponentRange = ExponentRange, ZeroBandWidth = ZeroBandWidth };
}
