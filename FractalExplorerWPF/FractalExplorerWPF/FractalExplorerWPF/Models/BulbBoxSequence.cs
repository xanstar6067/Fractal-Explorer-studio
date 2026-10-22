using System.Text.Json.Serialization;

namespace FractalExplorerWPF.Models;

public enum BulbBoxOperation { Bulb, Box, Rotate, Translate }

/// <summary>A finite cyclic program. Null in Fractal3DState keeps the original hybrid formula.</summary>
public sealed class BulbBoxSequence
{
    public const int MaxStages = 8;
    public List<BulbBoxStage> Stages { get; set; } = [];
    [JsonIgnore] public int OperationCount => Stages.Sum(s => s.Repeats);
    public BulbBoxSequence Clone() => new() { Stages = Stages.Select(s => s with { }).ToList() };

    public void Validate()
    {
        if (Stages is null || Stages.Count is < 1 or > MaxStages || Stages.Any(s => s is null))
            throw new ArgumentException("Цепочка должна содержать от 1 до 8 этапов.");
        foreach (var stage in Stages) stage.Validate();
        if (!Stages.Any(s => s.Operation is BulbBoxOperation.Bulb or BulbBoxOperation.Box))
            throw new ArgumentException("Добавьте хотя бы один бульб или бокс: поворот и сдвиг сами по себе не задают фрактал.");
    }

    public static BulbBoxSequence FromClassic(Fractal3DState state)
    {
        BulbBoxStage Stage(BulbBoxOperation operation, int repeats) => new()
        {
            Operation = operation, Repeats = repeats, Power = state.Power,
            Scale = state.BoxScale, MinRadius = state.BoxMinRadius, FoldLimit = state.BoxFoldingLimit,
            Mix = state.HybridMix, Inversion = state.BoxInversionShape,
            Stretch = state.BoxInversionStretch, InversionPower = state.BoxInversionPower
        };
        var bulb = Stage(BulbBoxOperation.Bulb, state.HybridBulbSteps);
        var box = Stage(BulbBoxOperation.Box, state.HybridBoxSteps);
        return new() { Stages = state.HybridOrder == Hybrid3DOrder.BoxFirst ? [box, bulb] : [bulb, box] };
    }

    public override string ToString() => string.Join(" → ", Stages.Select(s => s.Describe()));
}

public sealed record BulbBoxStage
{
    public BulbBoxOperation Operation { get; set; }
    public int Repeats { get; set; } = 1;
    public double Power { get; set; } = 3;
    public double Scale { get; set; } = -1.8;
    public double MinRadius { get; set; } = .5;
    public double FoldLimit { get; set; } = 1;
    public double Mix { get; set; }
    public BoxInversionShape Inversion { get; set; }
    public double Stretch { get; set; } = 1;
    public double InversionPower { get; set; } = 4;
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }

    public static string Name(BulbBoxOperation operation) => operation switch
    {
        BulbBoxOperation.Bulb => "Бульб", BulbBoxOperation.Box => "Бокс",
        BulbBoxOperation.Rotate => "Поворот", _ => "Сдвиг"
    };

    public string Describe()
    {
        string parameters;
        if (Operation is BulbBoxOperation.Rotate or BulbBoxOperation.Translate)
            parameters = $"{X:G5}; {Y:G5}; {Z:G5}" + (Operation == BulbBoxOperation.Rotate ? "°" : "");
        else
        {
            bool bulb = Operation == BulbBoxOperation.Bulb;
            string box = $"масштаб {Scale:G5}, радиус {MinRadius:G5}, свёртка {FoldLimit:G5}, " +
                $"{Fractal3DCatalog.BoxInversionShapeName(Inversion)}" +
                (Inversion is BoxInversionShape.Ellipsoid or BoxInversionShape.RoundedCube ? $", Z×{Stretch:G5}" : "") +
                (Inversion == BoxInversionShape.RoundedCube ? $", p={InversionPower:G5}" : "");
            parameters = bulb ? $"n={Power:G5}" : box;
            if (Mix > 0) parameters += $", смесь {Mix:G5}, " + (bulb ? box : $"n={Power:G5}");
        }
        return $"{Name(Operation)} ({parameters})" + (Repeats > 1 ? $" ×{Repeats}" : "");
    }

    public void Validate()
    {
        if (!Enum.IsDefined(Operation) || Repeats is < 1 or > 6)
            throw new ArgumentException("Неизвестная операция или число повторов вне диапазона 1–6.");
        static bool In(double value, double min, double max) => double.IsFinite(value) && value >= min && value <= max;
        if (Operation is BulbBoxOperation.Bulb or BulbBoxOperation.Box)
        {
            if (!In(Power, 2, 12) || !In(Scale, -8, 8) || !In(MinRadius, .01, 4) ||
                !In(FoldLimit, .1, 8) || !In(Mix, 0, 1) || !Enum.IsDefined(Inversion) ||
                !In(Stretch, .5, 2) || !In(InversionPower, 2, 16))
                throw new ArgumentException("Проверьте степень, параметры бокса и смешивание этапа.");
        }
        else
        {
            double limit = Operation == BulbBoxOperation.Rotate ? 180 : 3;
            if (!In(X, -limit, limit) || !In(Y, -limit, limit) || !In(Z, -limit, limit))
                throw new ArgumentException($"Компоненты операции «{Name(Operation)}» должны быть в пределах ±{limit}.");
        }
    }
}
