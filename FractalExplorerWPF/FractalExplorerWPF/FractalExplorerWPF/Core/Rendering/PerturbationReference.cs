using System.Globalization;
using System.Numerics;
using FractalExplorerWPF.Core.NewtonMath;

namespace FractalExplorerWPF.Core.Rendering;

/// <summary>
/// Общее для пертурбационных движков двумерных окон (семейство Мандельброта, Nova, Феникс,
/// бассейны Ньютона): порог включения, план разрядности опорной орбиты, разбор центра кадра и
/// кэш опорной орбиты. Сами орбиты и ядра у движков свои — формулы и данные на шаг у них
/// разные, а одинаковые правила вокруг орбиты собраны здесь, чтобы не расходиться.
/// </summary>
internal static class PerturbationReference
{
    /// <summary>
    /// Зум, выше которого кадр считает пертурбационный движок: около 1.5e9 шаг между пикселями
    /// перестаёт надёжно отличаться от нуля в double-координатах порядка единицы. Окна с
    /// глубоким зумом переводят на этом же пороге и центр вида в произвольную точность.
    /// </summary>
    public const double ZoomThreshold = 1.5e9;

    /// <summary>
    /// Критерий Pauldelbrot для ребазирования: |z|² ниже этой доли от |Zref|² — опорная точка
    /// для пикселя ненадёжна. 1e-6 — общепринятое значение.
    /// </summary>
    public const double GlitchToleranceSquared = 1e-6;

    /// <summary>
    /// Опорная орбита идёт до радиуса, заведомо большего, чем радиус выхода пикселя: так она
    /// даёт полезные точки как можно дольше, а хвост закрывает ребазирование. Запас и от
    /// переполнения старшей степени: |Z| ≤ 1e9 ⇒ |Z|¹² ≤ 1e108 ≪ 1.8e308.
    /// </summary>
    public const double EscapeSquared = 1e18;

    /// <summary>
    /// Короче этого опорная орбита вырождена (центр вылетел почти сразу): пертурбации не на
    /// что опереться, и кадр отдаётся плоской ступени. Рано, но не мгновенно вышедшая орбита
    /// обслуживается ребазированием и вырожденной не считается.
    /// </summary>
    public const int MinimumOrbitLength = 4;

    /// <summary>Двоичный логарифм зума; для нуля, отрицательного, меньшего единицы и нечислового — 0.</summary>
    public static double ZoomBits(FloatExp zoom)
    {
        double bits = zoom.Sign > 0 && zoom.IsFinite ? zoom.Log2() : 0;
        return double.IsFinite(bits) && bits > 0 ? bits : 0;
    }

    /// <inheritdoc cref="ZoomBits(FloatExp)"/>
    public static double ZoomBits(double zoom)
    {
        double bits = zoom > 0 && double.IsFinite(zoom) ? Math.Log2(zoom) : 0;
        return double.IsFinite(bits) && bits > 0 ? bits : 0;
    }

    /// <summary>
    /// Разрядность мантиссы опорной орбиты: биты на разрешение соседних пикселей
    /// (<paramref name="zoomBits"/> ≈ log2 зума), удвоенный запас по длине орбиты на накопление
    /// округлений и 48 бит на субпиксельную точность и общий люфт; вверх до кратного 64. Ниже
    /// ~1e93 формула не поднимается выше <see cref="BigFloat.MinimumPrecisionBits"/>.
    /// </summary>
    public static int PlanBits(double zoomBits, int iterations)
    {
        int iterationBits = 32 - BitOperations.LeadingZeroCount((uint)Math.Max(iterations, 2));
        int needed = (int)Math.Ceiling(zoomBits) + 2 * iterationBits + 48;
        return Math.Max(BigFloat.MinimumPrecisionBits, (needed + 63) / 64 * 64);
    }

    /// <summary>
    /// Координата центра для разбора в <see cref="BigFloat"/>: точная строка состояния, когда она
    /// есть (глубокий зум ведёт центр в произвольной точности), иначе — decimal-поле.
    /// </summary>
    public static string CenterText(string? exact, decimal value) =>
        exact is { Length: > 0 } ? exact : value.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc cref="CenterText(string?, decimal)"/>
    public static string CenterText(string? exact, double value) =>
        exact is { Length: > 0 } ? exact : value.ToString("R", CultureInfo.InvariantCulture);
}

/// <summary>
/// Кэш одной опорной орбиты движка: кадр, его тайлы и превью с тем же видом берут уже
/// посчитанную орбиту. Ключ — строка из всего, что участвует в построении орбиты; пропущенное
/// поле однажды стоило семейству Мандельброта чёрного кадра (кэш отдавал орбиту другой
/// степени), поэтому перечисление в ключе лучше держать избыточным.
/// </summary>
internal sealed class ReferenceOrbitCache<TOrbit> where TOrbit : class
{
    private readonly object _lock = new();
    private string? _key;
    private TOrbit? _orbit;

    /// <summary>
    /// Орбита по ключу; при промахе считает её под блокировкой, чтобы параллельные тайлы не
    /// строили одну и ту же орбиту наперегонки. Исключение (в том числе отмена) из
    /// <paramref name="compute"/> уходит вызывающему, и недосчитанная орбита в кэш не попадает.
    /// </summary>
    public TOrbit GetOrCompute(string key, Func<TOrbit> compute)
    {
        lock (_lock)
        {
            if (_key == key && _orbit is not null) return _orbit;

            TOrbit orbit = compute();
            _key = key;
            _orbit = orbit;
            return orbit;
        }
    }
}
