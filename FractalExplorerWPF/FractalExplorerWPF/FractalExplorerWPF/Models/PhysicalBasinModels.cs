using System.Numerics;

namespace FractalExplorerWPF.Models;

public enum LogisticPlaneMode { InitialValues, Parameter }
public enum PhysicalCaptureMode { Absorb, Settle }

/// <summary>Неподвижный центр. Единицы всех физических параметров условные.</summary>
public sealed class BasinForceCenter
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Strength { get; set; } = 1;
    public double CaptureRadius { get; set; } = 0.18;
    public BasinForceCenter Clone() => (BasinForceCenter)MemberwiseClone();
    public override string ToString() => $"({X:G4}; {Y:G4}) · сила {Strength:G4} · радиус {CaptureRadius:G4}";
}

public sealed class PhysicalBasinSettings
{
    public List<BasinForceCenter> Centers { get; set; } = Triangle();
    public double Damping { get; set; } = 0.3;
    public double RestoringForce { get; set; } = 0.2;
    public double Height { get; set; } = 0.25;
    public double TimeStep { get; set; } = 0.03;
    public double MaxTime { get; set; } = 80;
    public double SettleSpeed { get; set; } = 0.04;
    public double SettleTime { get; set; } = 0.5;
    public double EscapeDistance { get; set; } = 100;
    public Complex InitialVelocity { get; set; }
    public PhysicalCaptureMode CaptureMode { get; set; }

    public PhysicalBasinSettings Clone()
    {
        var copy = (PhysicalBasinSettings)MemberwiseClone();
        copy.Centers = Centers.Select(c => c.Clone()).ToList();
        return copy;
    }

    public static List<BasinForceCenter> Triangle() => RegularPolygon(3);
    public static List<BasinForceCenter> RegularPolygon(int count) => Enumerable.Range(0, count)
        .Select(i => new BasinForceCenter
        {
            X = Math.Cos(Math.PI / 2 + 2 * Math.PI * i / count),
            Y = Math.Sin(Math.PI / 2 + 2 * Math.PI * i / count)
        }).ToList();
}

public static partial class BasinExplorerCatalog
{
    private const string PhysicalHint = "Колесо: масштаб. Левая кнопка: перемещение. Правая: траектория. В режиме редактирования щелчок добавляет центр, перетаскивание перемещает. F11: полный экран.";

    private static IReadOnlyList<BasinExplorerState> LogisticPresets() =>
    [
        Logistic("λ = 3.2 · цикл периода 2", new Complex(3.2, 0)),
        Logistic("λ = 3.5 · цикл периода 4", new Complex(3.5, 0)),
        Logistic("λ = 3.55 · цикл периода 8", new Complex(3.55, 0)),
        Logistic("λ = 2 + 0.6i · комплексные бассейны", new Complex(2, 0.6)),
        Logistic("Плоскость λ · карта периодов", new Complex(3.2, 0), true),
        Logistic("λ = 2 · неподвижная точка", new Complex(2, 0))
    ];

    private static BasinExplorerState Logistic(string name, Complex lambda, bool parameter = false) => new()
    {
        SaveName = name, Kind = BasinExplorerKind.ComplexLogistic, Formula = "c*z*(1-z)",
        ParameterC = lambda, LogisticPlane = parameter ? LogisticPlaneMode.Parameter : LogisticPlaneMode.InitialValues,
        CenterX = parameter ? 1 : 0.5, Zoom = parameter ? 0.55 : 2,
        MaxIterations = 500, MaxPeriod = 16, ShadingScale = 25,
        ColoringMode = parameter ? BasinColoringMode.Period : BasinColoringMode.ConvergenceSpeed,
        MarkerMode = BasinMarkerMode.Hidden, Palette = GrayscalePalette()
    };

    private static IReadOnlyList<BasinExplorerState> PhysicalPresets(BasinExplorerKind kind)
    {
        bool magnetic = kind == BasinExplorerKind.MagneticPendulum;
        var basic = new BasinExplorerState
        {
            Kind = kind, SaveName = magnetic ? "Три магнита · классический маятник" : "Три центра · поглощение",
            MaxIterations = 16000, Zoom = 0.55, ShadingScale = 12,
            MarkerMode = BasinMarkerMode.Hidden, Palette = GrayscalePalette(),
            Physics = new PhysicalBasinSettings
            {
                CaptureMode = magnetic ? PhysicalCaptureMode.Settle : PhysicalCaptureMode.Absorb,
                Damping = magnetic ? 0.3 : 0.08,
                RestoringForce = magnetic ? 0.2 : 0,
                Height = magnetic ? 0.25 : 0.12
            }
        };
        var square = basic.Clone();
        square.SaveName = magnetic ? "Четыре магнита · квадрат" : "Четыре центра · квадрат";
        square.Physics.Centers = PhysicalBasinSettings.RegularPolygon(4);
        var unequal = basic.Clone();
        unequal.SaveName = "Неравные силы · 1 : 1.5 : 0.7";
        unequal.Physics.Centers[1].Strength = 1.5;
        unequal.Physics.Centers[2].Strength = 0.7;
        var moving = basic.Clone();
        moving.SaveName = magnetic ? "Маятник · начальная скорость вправо" : "Центры · боковой пролёт";
        moving.Physics.InitialVelocity = new Complex(0.8, 0);
        if (!magnetic) return [basic, square, unequal, moving];
        var chaotic = basic.Clone();
        chaotic.SaveName = "Слабое трение · хаотические границы (дольше)";
        chaotic.Physics.Damping = 0.12;
        chaotic.Physics.MaxTime = 200;
        chaotic.MaxIterations = 40000;
        chaotic.ShadingScale = 45;
        return [basic, chaotic, square, unequal, moving];
    }
}
