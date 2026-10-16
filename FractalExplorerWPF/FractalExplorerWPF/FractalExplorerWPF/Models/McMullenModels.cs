using System.Text.Json.Serialization;
using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Models;

public enum McMullenPlane { Julia, Parameter }
public enum McMullenColoring { SmoothEscape, EscapeRoute, Phase, CircleTrap, CrossTrap, Period }

public sealed class McMullenState
{
    public string SaveName { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public string FractalType { get; set; } = "McMullen";
    public int P { get; set; } = 3;
    public int Q { get; set; } = 3;
    public double LambdaReal { get; set; } = 0.0001;
    public double LambdaImaginary { get; set; }
    public McMullenPlane Plane { get; set; }
    public double CenterX { get; set; }
    public double CenterY { get; set; }
    public double Zoom { get; set; } = 1;
    public double ParameterCenterX { get; set; }
    public double ParameterCenterY { get; set; }
    public double ParameterZoom { get; set; } = 8;
    public int MaxIterations { get; set; } = 300;
    public double EscapeRadius { get; set; } = 4;
    public McMullenColoring Coloring { get; set; } = McMullenColoring.EscapeRoute;
    public double ColorScale { get; set; } = 12;
    public double ColorOffset { get; set; }
    public double TrapRadius { get; set; } = 0.5;
    public int MaxPeriod { get; set; } = 24;
    public double Tolerance { get; set; } = 1e-8;
    public bool ShowCriticalPoints { get; set; }
    public NewtonColorPalette Palette { get; set; } = new()
    {
        Name = "Макмаллен", BackgroundColor = Color.FromRgb(7, 10, 20),
        RootColors = [Color.FromRgb(20, 35, 83), Color.FromRgb(32, 150, 191),
            Color.FromRgb(155, 234, 213), Color.FromRgb(255, 217, 132),
            Color.FromRgb(223, 103, 87), Color.FromRgb(99, 53, 129)],
        ExpansionMode = NewtonPaletteExpansionMode.CyclicRamp, IsGradient = true
    };
    [JsonIgnore] public double ViewCenterX => Plane == McMullenPlane.Parameter ? ParameterCenterX : CenterX;
    [JsonIgnore] public double ViewCenterY => Plane == McMullenPlane.Parameter ? ParameterCenterY : CenterY;
    [JsonIgnore] public double ViewZoom => Plane == McMullenPlane.Parameter ? ParameterZoom : Zoom;

    public McMullenState Clone()
    {
        var copy = (McMullenState)MemberwiseClone();
        copy.Palette = Palette.Clone(Palette.Name);
        return copy;
    }

    public void Validate()
    {
        if (P is < 2 or > 12 || Q is < 1 or > 12)
            throw new ArgumentException("Степень p: 2…12; порядок полюса q: 1…12.");
        if (!Enum.IsDefined(Plane) || !Enum.IsDefined(Coloring)) throw new ArgumentException("Неизвестный режим Макмаллена.");
        if (!FiniteIn(LambdaReal, -1e6, 1e6) || !FiniteIn(LambdaImaginary, -1e6, 1e6))
            throw new ArgumentException("Компоненты λ должны быть конечными и лежать в пределах ±1000000.");
        if (!FiniteIn(CenterX, -1e6, 1e6) || !FiniteIn(CenterY, -1e6, 1e6) || !FiniteIn(Zoom, 1e-4, 1e10) ||
            !FiniteIn(ParameterCenterX, -1e6, 1e6) || !FiniteIn(ParameterCenterY, -1e6, 1e6) || !FiniteIn(ParameterZoom, 1e-4, 1e10))
            throw new ArgumentException("Центр: ±1000000; приближение: 1e-4…1e10.");
        if (MaxIterations is < 1 or > 10000 || !FiniteIn(EscapeRadius, 2, 1e6) || MaxPeriod is < 1 or > 64 ||
            !FiniteIn(Tolerance, 1e-12, 1e-3)) throw new ArgumentException("Некорректные параметры итераций или сходимости.");
        if (!FiniteIn(ColorScale, 0.1, 1000) || !FiniteIn(ColorOffset, 0, 1) || !FiniteIn(TrapRadius, 0, 100))
            throw new ArgumentException("Масштаб цвета: 0.1…1000; сдвиг: 0…1; радиус ловушки: 0…100.");
        if (Palette?.RootColors is not { Count: > 0 }) throw new ArgumentException("Палитра отсутствует или пуста.");
    }
    private static bool FiniteIn(double x, double min, double max) => double.IsFinite(x) && x >= min && x <= max;
}

public static class McMullenPresets
{
    public static IReadOnlyList<McMullenState> All =>
    [
        Make("Канторовы кольца · 3/3", 3, 3, 0.0001, 0),
        Make("Переходы · 3/3", 3, 3, 0.02, 0),
        Make("Комплексный параметр · 3/2", 3, 2, -0.04, 0.025),
        Make("Четыре лепестка · 2/2", 2, 2, -0.1, 0),
        Make("Двойной полюс · 4/2", 4, 2, 0.001, 0),
        Make("Простой полюс · 3/1", 3, 1, -0.05, 0.01),
        Make("Карта λ · 3/3", 3, 3, 0.0001, 0, McMullenPlane.Parameter),
        Make("Карта λ · 2/2", 2, 2, -0.1, 0, McMullenPlane.Parameter)
    ];
    private static McMullenState Make(string name, int p, int q, double real, double imaginary,
        McMullenPlane plane = McMullenPlane.Julia) => new()
        { SaveName = name, P = p, Q = q, LambdaReal = real, LambdaImaginary = imaginary, Plane = plane };
}
