using System.Windows.Media;
using Color = System.Windows.Media.Color;
using System.Text.Json.Serialization;

namespace FractalExplorerWPF.Models;

public enum TranscendentalJuliaKind { Exponential, Sine }
public enum TranscendentalJuliaLayer { Orbits, Julia, Combined }
public enum TranscendentalJuliaDrawing { Points, Rays, RaysAndPoints }
public enum TranscendentalJuliaPlane { Dynamic, Parameter }

public sealed class TranscendentalJuliaState
{
    public string SaveName { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public string FractalType { get; set; } = "TranscendentalJulia";
    public TranscendentalJuliaKind Kind { get; set; }
    public double LambdaReal { get; set; } = 0.3;
    public double LambdaImaginary { get; set; }
    public double CenterX { get; set; } = 2;
    public double CenterY { get; set; }
    public double Zoom { get; set; } = 1;
    public int MaxIterations { get; set; } = 300;
    public int MaxPeriod { get; set; } = 16;
    public double Tolerance { get; set; } = 1e-7;
    public TranscendentalJuliaLayer Layer { get; set; } = TranscendentalJuliaLayer.Combined;
    public int InverseDepth { get; set; } = 24;
    public int BranchRadius { get; set; } = 4;
    public int PointBudget { get; set; } = 1_000_000;
    public int SamplingSeed { get; set; } = 137;
    // Points сохраняет вид прежних JSON, где этих полей ещё нет.
    public TranscendentalJuliaDrawing Drawing { get; set; }
    public TranscendentalJuliaPlane Plane { get; set; }
    public int RayLimit { get; set; } = 600;
    public double RayAccuracy { get; set; } = 0.35;
    public double ParameterCenterX { get; set; }
    public double ParameterCenterY { get; set; }
    public double ParameterZoom { get; set; } = 3;
    [JsonIgnore] public double ViewCenterX => Plane == TranscendentalJuliaPlane.Parameter ? ParameterCenterX : CenterX;
    [JsonIgnore] public double ViewCenterY => Plane == TranscendentalJuliaPlane.Parameter ? ParameterCenterY : CenterY;
    [JsonIgnore] public double ViewZoom => Plane == TranscendentalJuliaPlane.Parameter ? ParameterZoom : Zoom;
    public NewtonColorPalette Palette { get; set; } = new()
    {
        Name = "Трансцендентные Жюлиа", BackgroundColor = Color.FromRgb(7, 10, 18),
        RootColors = [Color.FromRgb(255, 211, 112), Color.FromRgb(24, 32, 51),
            Color.FromRgb(86, 40, 63), Color.FromRgb(45, 155, 208),
            Color.FromRgb(123, 209, 163), Color.FromRgb(215, 107, 175), Color.FromRgb(191, 144, 89)],
        ExpansionMode = NewtonPaletteExpansionMode.Cycle, IsGradient = true
    };

    public TranscendentalJuliaState Clone()
    {
        var copy = (TranscendentalJuliaState)MemberwiseClone();
        copy.Palette = Palette.Clone(Palette.Name);
        return copy;
    }

    public void Validate()
    {
        if (!Enum.IsDefined(Kind) || !Enum.IsDefined(Layer) || !Enum.IsDefined(Drawing) || !Enum.IsDefined(Plane))
            throw new ArgumentException("Неизвестный режим Жюлиа.");
        if (!double.IsFinite(LambdaReal) || !double.IsFinite(LambdaImaginary) ||
            Math.Abs(LambdaReal) > 1e6 || Math.Abs(LambdaImaginary) > 1e6)
            throw new ArgumentException("Компоненты λ должны быть конечными числами в пределах ±1000000.");
        if (!double.IsFinite(CenterX) || !double.IsFinite(CenterY) || Math.Abs(CenterX) > 1e10 || Math.Abs(CenterY) > 1e10 ||
            !double.IsFinite(Zoom) || Zoom < 1e-6 || Zoom > 1e10)
            throw new ArgumentException("Центр должен быть в пределах ±1e10, масштаб — от 1e-6 до 1e10.");
        if (MaxIterations is < 1 or > 10000 || MaxPeriod is < 1 or > 32 ||
            !double.IsFinite(Tolerance) || Tolerance < 1e-12 || Tolerance > 1e-3)
            throw new ArgumentException("Итерации: 1…10000; период: 1…32; допуск: 1e-12…1e-3.");
        if (InverseDepth is < 1 or > 80 || BranchRadius is < 1 or > 24 || PointBudget is < 1000 or > 10_000_000)
            throw new ArgumentException("Глубина: 1…80; ветви: 1…24; бюджет: 1000…10000000.");
        if (Palette is null || Palette.RootColors is null) throw new ArgumentException("Палитра отсутствует.");
        if (RayLimit is < 8 or > 10000 || !double.IsFinite(RayAccuracy) || RayAccuracy < 0.05 || RayAccuracy > 2)
            throw new ArgumentException("Лучей: 8…10000; точность кривых: 0.05…2 пикселя.");
        if (!double.IsFinite(ParameterCenterX) || !double.IsFinite(ParameterCenterY) ||
            Math.Abs(ParameterCenterX) > 1e10 || Math.Abs(ParameterCenterY) > 1e10 ||
            !double.IsFinite(ParameterZoom) || ParameterZoom < 1e-6 || ParameterZoom > 1e10)
            throw new ArgumentException("Некорректный вид карты λ.");
    }
}

public static class TranscendentalJuliaCatalog
{
    public static string LaunchKey(TranscendentalJuliaKind kind) => kind == TranscendentalJuliaKind.Exponential
        ? "JuliaExponential" : "JuliaSine";
    public static string Title(TranscendentalJuliaKind kind) => kind == TranscendentalJuliaKind.Exponential
        ? "Экспоненциальное Жюлиа" : "Синусоидальное Жюлиа";
    public static string Formula(TranscendentalJuliaKind kind) => kind == TranscendentalJuliaKind.Exponential
        ? "zₙ₊₁ = λ·exp(zₙ)" : "zₙ₊₁ = λ·sin(zₙ)";
    public static bool TryParseLaunchKey(string? key, out TranscendentalJuliaKind kind)
    {
        kind = key == "JuliaSine" ? TranscendentalJuliaKind.Sine : TranscendentalJuliaKind.Exponential;
        return key is "JuliaExponential" or "JuliaSine";
    }
    public static TranscendentalJuliaState CreateDefault(TranscendentalJuliaKind kind) => new()
    {
        Kind = kind, SaveName = "Канторов букет", LambdaReal = kind == TranscendentalJuliaKind.Exponential ? 0.3 : 0.5,
        CenterX = kind == TranscendentalJuliaKind.Exponential ? 2 : 0,
        Drawing = TranscendentalJuliaDrawing.Rays
    };
    public static IReadOnlyList<TranscendentalJuliaState> Presets(TranscendentalJuliaKind kind)
    {
        var first = CreateDefault(kind);
        var second = first.Clone();
        second.SaveName = kind == TranscendentalJuliaKind.Exponential ? "Поворот параметра" : "Притягивающий цикл периода 4";
        second.LambdaReal = kind == TranscendentalJuliaKind.Exponential ? 0.25 : 0;
        second.LambdaImaginary = kind == TranscendentalJuliaKind.Exponential ? 0.15 : 1.166667;
        var third = first.Clone();
        third.SaveName = "Крупный план";
        third.Zoom = 3;
        third.CenterX = kind == TranscendentalJuliaKind.Exponential ? 1.8 : 0;
        third.CenterY = kind == TranscendentalJuliaKind.Exponential ? 0 : 2.2;
        return [first, second, third];
    }
}
