namespace FractalExplorerWPF.Models;

/// <summary>Какой узор ищет рандомизатор Gray–Scott (2D и 3D).</summary>
public enum GrayScottPatternTarget
{
    Any,
    Spots,
    Stripes,
    Holes,
    Moving
}

/// <summary>Форма найденного узора по связным областям V выше и ниже порога.</summary>
public enum GrayScottPatternShape
{
    Spots,
    Stripes,
    Holes
}

/// <summary>
/// Сводка пробной симуляции: разброс и заполнение V, число отдельных областей выше порога
/// (пятен) и ниже него (отверстий), доля их клеток в компактных, не вытянутых областях,
/// и подвижность между двумя поздними снимками. Порог — середина между 1-м и 99-м процентилями V.
/// </summary>
public sealed record GrayScottPatternMetrics(
    double Deviation,
    double Coverage,
    double Activity,
    int Blobs,
    double CompactBlobShare,
    int Holes,
    double CompactHoleShare,
    double Threshold,
    double Floor,
    double Peak)
{
    /// <summary>Подвижность — средняя смена V за контрольный интервал в долях её разброса.</summary>
    public const double MovingActivity = .12;

    /// <summary>Поле не пустое, не однородное и не залито целиком.</summary>
    public bool IsAlive => Deviation >= .02 && Peak - Floor >= .08 && Coverage is >= .015 and <= .985;

    public bool IsMoving => Activity >= MovingActivity;

    public GrayScottPatternShape Shape =>
        Blobs >= 3 && CompactBlobShare >= .6 ? GrayScottPatternShape.Spots :
        Holes >= 3 && CompactHoleShare >= .6 ? GrayScottPatternShape.Holes :
        GrayScottPatternShape.Stripes;

    public bool Matches(GrayScottPatternTarget target) => IsAlive && target switch
    {
        GrayScottPatternTarget.Spots => Shape == GrayScottPatternShape.Spots,
        GrayScottPatternTarget.Stripes => Shape == GrayScottPatternShape.Stripes,
        GrayScottPatternTarget.Holes => Shape == GrayScottPatternShape.Holes,
        GrayScottPatternTarget.Moving => IsMoving,
        _ => true
    };

    /// <summary>Вес при выборе среди подходящих: выраженный рисунок и не крайнее заполнение.</summary>
    public double Score => Deviation * (1.2 - Math.Abs(Coverage - .45)) * (Blobs + Holes >= 3 ? 1 : .6);

    public string Describe(bool volume)
    {
        string shape = (Shape, volume) switch
        {
            (GrayScottPatternShape.Spots, false) => "пятна",
            (GrayScottPatternShape.Spots, true) => "капли",
            (GrayScottPatternShape.Holes, false) => "сетка с отверстиями",
            (GrayScottPatternShape.Holes, true) => "пористый объём",
            (_, false) => "нити и лабиринты",
            _ => "каналы и мембраны"
        };
        return $"{char.ToUpperInvariant(shape[0])}{shape[1..]} · {(IsMoving ? "в движении" : "почти устойчиво")}";
    }
}

public sealed record GrayScottSearchProgress(int Checked, int Alive);

/// <summary>Найденный вариант: готовое к запуску состояние и оценка его пробного прогона.</summary>
public sealed record GrayScottCandidate<TState>(TState State, GrayScottPatternMetrics Metrics, bool MatchesTarget, int Checked);
