namespace FractalExplorerWPF.Models;

/// <summary>Шесть исследуемых компонент Phoenix 3D, без камеры, качества и параметров итерации.</summary>
public sealed record Phoenix3DParameters(double C1Real, double C1I, double C1J, double C2Real, double C2I, double C2J)
{
    public static Phoenix3DParameters FromState(Fractal3DState state) => new(
        state.JuliaCX, state.JuliaCY, state.JuliaCZ, state.PhoenixMemoryX, state.PhoenixMemoryY, state.PhoenixMemoryZ);

    public double[] Components => [C1Real, C1I, C1J, C2Real, C2I, C2J];
    public decimal[] AtlasPoint => Components.Select(value => (decimal)value).ToArray();

    public void ApplyTo(Fractal3DState state)
    {
        if (Components.Any(value => !double.IsFinite(value) || value < -8 || value > 8))
            throw new ArgumentException("Компоненты C₁ и C₂ должны быть от −8 до 8.");
        state.JuliaCX = C1Real; state.JuliaCY = C1I; state.JuliaCZ = C1J;
        state.PhoenixMemoryX = C2Real; state.PhoenixMemoryY = C2I; state.PhoenixMemoryZ = C2J;
    }

    public Phoenix3DParameters WithAtlasPoint(decimal[] point)
    {
        if (point.Length != 6 || point.Any(value => value < -8 || value > 8))
            throw new ArgumentException("Нужны шесть компонент C₁/C₂ от −8 до 8.");
        var values = Components;
        // Decimal нужен оболочке атласа; не округляем нетронутые double-компоненты при возврате.
        for (int i = 0; i < values.Length; i++)
            if (point[i] != (decimal)values[i]) values[i] = (double)point[i];
        return FromComponents(values);
    }

    /// <param name="target">0 — обе константы, 1 — только C₁, 2 — только C₂.</param>
    public Phoenix3DParameters Vary(Random random, double radius, int target)
    {
        if (!double.IsFinite(radius) || radius < .001 || radius > .5 || target is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(radius));
        var values = Components;
        for (int i = 0; i < values.Length; i++)
            if (target == 0 || (target == 1 && i < 3) || (target == 2 && i >= 3))
                values[i] = Math.Clamp(values[i] + (random.NextDouble() * 2 - 1) * radius, -8, 8);
        return FromComponents(values);
    }

    private static Phoenix3DParameters FromComponents(double[] values) =>
        new(values[0], values[1], values[2], values[3], values[4], values[5]);
}
