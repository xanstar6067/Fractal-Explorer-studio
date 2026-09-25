namespace FractalExplorerWPF.Models;

/// <summary>Автономные трёхмерные потоки с наглядным ограниченным аттрактором.</summary>
public enum Attractor3DSystem { Lorenz, Rossler, Thomas, Halvorsen, Aizawa, Dadras, Chua }

public sealed class Attractor3DSettings
{
    public Attractor3DSystem System { get; set; }
    public double A { get; set; }
    public double B { get; set; }
    public double C { get; set; }
    public double D { get; set; }
    public double E { get; set; }
    public double F { get; set; }
    public double TimeStep { get; set; }
    public double StartX { get; set; }
    public double StartY { get; set; }
    public double StartZ { get; set; }

    public Attractor3DSettings Clone() => (Attractor3DSettings)MemberwiseClone();
}

public static class Attractor3DSystems
{
    public static string Name(Attractor3DSystem system) => system switch
    {
        Attractor3DSystem.Rossler => "Рёсслер · спираль и изгиб",
        Attractor3DSystem.Thomas => "Томас · три переплетения",
        Attractor3DSystem.Halvorsen => "Халворсен · тройная симметрия",
        Attractor3DSystem.Aizawa => "Айзава · круговая воронка",
        Attractor3DSystem.Dadras => "Дадрас · раскрытые крылья",
        Attractor3DSystem.Chua => "Чуа · двойная спираль",
        _ => "Лоренц · двойное крыло"
    };

    public static string Description(Attractor3DSystem system) => system switch
    {
        Attractor3DSystem.Rossler => "Плоский завиток поднимается и складывается в третьем измерении.",
        Attractor3DSystem.Thomas => "Три одинаковые синусоидальные связи дают переплетённую симметрию.",
        Attractor3DSystem.Halvorsen => "Три циклически связанные оси образуют объёмный узел.",
        Attractor3DSystem.Aizawa => "Вращение вокруг оси и нелинейная обратная связь формируют воронку.",
        Attractor3DSystem.Dadras => "Связанные нелинейные потоки раскрываются в асимметричные крылья.",
        Attractor3DSystem.Chua => "Кусочно-линейная нелинейность переключает траекторию между двумя спиралями.",
        _ => "Классическая модель конвекции с двумя хаотическими лопастями."
    };

    public static string[] ParameterNames(Attractor3DSystem system) => system switch
    {
        Attractor3DSystem.Lorenz => ["σ", "ρ", "β"],
        Attractor3DSystem.Rossler => ["a", "b", "c"],
        Attractor3DSystem.Thomas => ["b"],
        Attractor3DSystem.Halvorsen => ["a"],
        Attractor3DSystem.Aizawa => ["a", "b", "c", "d", "e", "f"],
        Attractor3DSystem.Chua => ["α", "β", "m₀", "m₁"],
        _ => ["a", "b", "c", "d", "e"]
    };

    public static Attractor3DSettings Default(Attractor3DSystem system) => system switch
    {
        Attractor3DSystem.Rossler => new() { System = system, A = .2, B = .2, C = 5.7, TimeStep = .02, StartX = 1, StartY = 1, StartZ = 1 },
        Attractor3DSystem.Thomas => new() { System = system, A = .208186, TimeStep = .025, StartX = 1, StartY = 2, StartZ = 3 },
        Attractor3DSystem.Halvorsen => new() { System = system, A = 1.27, TimeStep = .005, StartX = 1, StartY = 0, StartZ = 0 },
        Attractor3DSystem.Aizawa => new() { System = system, A = .95, B = .7, C = .6, D = 3.5, E = .25, F = .1, TimeStep = .005, StartX = .1, StartY = 0, StartZ = 0 },
        Attractor3DSystem.Dadras => new() { System = system, A = 3, B = 2.7, C = 4.7, D = 2, E = 9, TimeStep = .005, StartX = 1, StartY = 1, StartZ = 1 },
        Attractor3DSystem.Chua => new() { System = system, A = 15.6, B = 31, C = -8d / 7, D = -5d / 7, TimeStep = .005, StartX = .1, StartY = 0, StartZ = 0 },
        _ => new() { System = system, A = 10, B = 28, C = 8d / 3, TimeStep = .005, StartX = 1, StartY = 1, StartZ = 1 }
    };
}
