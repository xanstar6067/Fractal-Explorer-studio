using System.Numerics;
using System.Text.Json.Serialization;
using System.Windows.Media;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Infrastructure.Serialization;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Models;

public enum NewtonIterationMethod
{
    Newton,
    Halley,
    Householder,
    RelaxedNewton
}

public enum NewtonRelaxedPlaneMode
{
    ZPlane,
    LambdaPlane
}

public enum NewtonRootSearchMode
{
    Automatic,
    Adaptive,
    ManualOnly
}

public enum NewtonRootDisplayMode
{
    Hidden,
    Markers,
    MarkersWithCoordinates
}

public enum NewtonDiagnosticColoringMode
{
    Disabled,
    OrbitOutcome,
    CyclesOnly,
    Residual,
    FinalValuePhase
}

public enum NewtonOrbitOutcome
{
    ConvergedToRoot,
    Cycle,
    ZeroDerivative,
    Escaped,
    NonFinite,
    IterationLimit
}

public enum NewtonPaletteExpansionMode
{
    LinearRamp,
    CyclicRamp,
    Cycle,
    RepeatFirst,
    Harmonic
}

public sealed class NewtonColorPalette
{
    public string Name { get; set; } = "Новая палитра";
    public List<Color> RootColors { get; set; } = [];
    public Color BackgroundColor { get; set; } = Colors.Black;
    public bool IsGradient { get; set; }
    public bool IsBuiltIn { get; set; }
    public NewtonPaletteExpansionMode ExpansionMode { get; set; }

    public NewtonColorPalette Clone(string name) => new()
    {
        Name = name,
        RootColors = [.. RootColors],
        BackgroundColor = BackgroundColor,
        IsGradient = IsGradient,
        ExpansionMode = ExpansionMode
    };

    public override string ToString() => Name;
}

public sealed class NewtonState
{
    public string SaveName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string FractalType { get; set; } = "NewtonPools";
    public string Formula { get; set; } = "z^3-1";
    public int MaxIterations { get; set; } = 500;

    /// <summary>
    /// Зум в расширенном диапазоне (до 1e1000). В пределах double пишется числом, поэтому
    /// сохранения с прежним double-зумом читаются без изменений.
    /// </summary>
    [JsonConverter(typeof(FloatExpJsonConverter))]
    public FloatExp Zoom { get; set; } = FloatExp.One;

    public double CenterX { get; set; }
    public double CenterY { get; set; }

    /// <summary>
    /// Центр в произвольной точности — инвариантная строка, заполняется только на глубоком зуме.
    /// У обычных сохранений null, и тогда центр задают <see cref="CenterX"/>/<see cref="CenterY"/>.
    /// </summary>
    public string? CenterXExact { get; set; }

    /// <inheritdoc cref="CenterXExact"/>
    public string? CenterYExact { get; set; }
    public NewtonIterationMethod IterationMethod { get; set; }
    public int HouseholderOrder { get; set; } = 3;
    public NewtonRelaxedPlaneMode RelaxedPlaneMode { get; set; }
    public Complex Relaxation { get; set; } = Complex.One;
    public Complex FixedInitialZ { get; set; } = new(0.5, 0.5);
    public NewtonRootSearchMode RootSearchMode { get; set; }
    public NewtonRootDisplayMode RootDisplayMode { get; set; } = NewtonRootDisplayMode.Hidden;
    public NewtonDiagnosticColoringMode DiagnosticColoringMode { get; set; }
    public double RootTolerance { get; set; } = 1e-6;
    public double RootSearchRadius { get; set; } = 8;
    public List<Complex> Roots { get; set; } = [];
    public NewtonColorPalette Palette { get; set; } = new();
}

public readonly record struct NewtonOrbitResult(
    NewtonOrbitOutcome Outcome,
    int Iterations,
    Complex FinalPoint,
    Complex FinalValue,
    double Residual,
    int RootIndex = -1,
    int CyclePeriod = 0);

public sealed record NewtonRootColorItem(int Index, Complex Root, Color Color, string? CustomLabel = null)
{
    public SolidColorBrush Brush
    {
        get
        {
            var brush = new SolidColorBrush(Color);
            brush.Freeze();
            return brush;
        }
    }

    public string Label => CustomLabel ?? $"Корень {Index + 1}: {Format(Root.Real)} {(Root.Imaginary < 0 ? '−' : '+')} {Format(Math.Abs(Root.Imaginary))}i";
    public string Hex => $"#{Color.A:X2}{Color.R:X2}{Color.G:X2}{Color.B:X2}";

    private static string Format(double value) => value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
}
