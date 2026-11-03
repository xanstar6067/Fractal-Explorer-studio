using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

/// <summary>Независимые разряды с одинаковой ранней стадией и исходной точностью сетки.</summary>
public sealed class Lichtenberg3DAtlasWindow : Fractal3DParameterAtlasWindow
{
    private readonly double _eta;

    public Lichtenberg3DAtlasWindow(Fractal3DState source) : base(PreviewSource(source))
    {
        _eta = source.Lichtenberg.Eta;
        Initialize3DAtlas("Атлас параметров · Фигуры Лихтенберга 3D",
            ["Ветвление · η", "Электроды", "Размер сетки", "Случайное число"],
            [(decimal)_eta, (int)source.Lichtenberg.Electrodes + 1, source.Lichtenberg.Size, source.Lichtenberg.Seed],
            $"Каждый вариант заново растёт от затравки до {Math.Min(source.Lichtenberg.SegmentCount, 32)} участков либо до электрода. " +
            "η = 0 даёт равновероятный выбор связей, большое η сильнее выделяет активные концы. Электроды: сфера вокруг центра или нижняя плоскость. " +
            "Сетка 32³/48³/64³ рассчитывается без подмены меньшей. Одинаковое случайное число обеспечивает воспроизводимость при тех же параметрах. " +
            $"Применение сохраняет исходную стадию {source.Lichtenberg.SegmentCount} участков и цель {source.Lichtenberg.TargetSegments}. " +
            "При изменении параметров старое поле отбрасывается и разряд пересчитывается от затравки; при прежних параметрах снимок поля сохраняется. " +
            "Камера, оформление и порция на кадр сохраняются. Рост остаётся на паузе. При нулевой стадии видна только затравка; ранний силуэт может отличаться от полного. " +
            "Атлас использует применённые настройки, а не черновик полей основного окна. Отказ сходимости показывается в отдельной ячейке.");
    }

    private static Fractal3DState PreviewSource(Fractal3DState source)
    {
        if (source.Kind != Fractal3DKind.Lichtenberg3D || source.Lichtenberg is null) throw new ArgumentException("Нужен режим фигур Лихтенберга 3D.");
        try { source.Lichtenberg.Validate(); }
        catch (InvalidOperationException ex) { throw new ArgumentException(ex.Message, ex); }
        var preview = source.Clone();
        preview.Lichtenberg = source.Lichtenberg with { Field = null, SegmentCount = Math.Min(source.Lichtenberg.SegmentCount, 32), SessionId = 0 };
        return preview;
    }
    protected override (decimal Minimum, decimal Maximum) ComponentBounds(int index) => index switch
    {
        0 => (0, 8), 1 => (1, 2), 2 => (32, 64), 3 => (int.MinValue, int.MaxValue), _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
    protected override (decimal Minimum, decimal Maximum) AxisBounds(int index) => ComponentBounds(index);
    protected override bool IsIntegerComponent(int index) => index != 0;
    protected override int IntegerComponentStep(int index) => index == 2 ? 16 : 1;
    protected override decimal InitialRangeWidth(int index) => index switch { 0 => 2, 1 => 1, 2 => 32, _ => 4 };
    protected override string ComponentValueLabel(int index, decimal value, string format) => index switch
    {
        1 => value == 1 ? "Центр → сфера" : "Сверху → плоскость",
        2 => $"{value:0}³", _ => base.ComponentValueLabel(index, value, format)
    };
    protected override ParameterAtlasRange OverviewRange
    {
        get { var x = ComponentBounds(XAxisIndex); var y = ComponentBounds(YAxisIndex); return new(x.Minimum, x.Maximum, y.Minimum, y.Maximum); }
    }
    protected override string? UnavailableSampleError(Exception error) => error is Lichtenberg3DGrowthException ? error.Message : null;
    protected override string PreviewStatus(int completed, int total, int iterations) =>
        $"{completed} / {total} · поле и кадр на ГП · до 32 участков";
    protected override void ApplyComponents(Fractal3DState state, decimal[] point)
    {
        if (state.Kind != Fractal3DKind.Lichtenberg3D || state.Lichtenberg is null || point.Length != 4)
            throw new ArgumentException("Нужен режим фигур Лихтенберга 3D.");
        for (int i = 0; i < point.Length; i++)
        {
            var bounds = ComponentBounds(i);
            if (point[i] < bounds.Minimum || point[i] > bounds.Maximum || i != 0 && point[i] != decimal.Truncate(point[i]) ||
                i == 2 && !Lichtenberg3DSettings.IsSupportedSize((int)point[i]))
                throw new ArgumentException("Недопустимое η, электроды, сетка или случайное число.");
        }
        var current = state.Lichtenberg;
        var settings = current with { Eta = point[0] == (decimal)_eta ? _eta : (double)point[0],
            Electrodes = (LichtenbergElectrodes)((int)point[1] - 1), Size = (int)point[2], Seed = (int)point[3] };
        if (!settings.SameGrowth(current)) settings = settings with { Field = null };
        settings.Validate(); state.Lichtenberg = settings;
    }
}
