using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FractalExplorerWPF.Core.NewtonMath;
using Point = System.Windows.Point;

namespace FractalExplorerWPF.Controls;

/// <summary>
/// Вид двумерного окна с глубоким зумом: центр, зум, колесо, перетаскивание и сдвиг грубого
/// предпросмотра относительно отрисованного кадра.
///
/// Центр ведётся в двух представлениях. Ниже <see cref="DeepZoomThreshold"/> источник истины —
/// обычный центр <typeparamref name="T"/> (decimal или double — тип, в котором его хранит
/// сохранение окна). Начиная с порога, где соседние пиксели в нём уже не различимы, центр
/// ведётся в <see cref="BigFloat"/> с адаптивной точностью, а обычный остаётся ближайшим
/// приближением для показа и старого формата сохранений. Зум и все сдвиги — в
/// <see cref="FloatExp"/>: на глубине ширина вида и сдвиг пана уже вне диапазона double.
/// </summary>
public abstract class DeepZoomViewport<T> where T : struct
{
    /// <summary>
    /// Запас сверх log2 зума в разрядности центра: субпиксельная точность и накопление сотен
    /// сдвигов пана без повторного рендера.
    /// </summary>
    private const int CenterPrecisionGuardBits = 96;

    private BigFloat _exactX, _exactY;
    private FrameworkElement? _previewCanvas;
    private ScaleTransform? _previewScale;
    private TranslateTransform? _previewTranslation;
    private Point _lastPanPoint;

    /// <param name="baseViewWidth">Ширина вида в мировых единицах при зуме 1.</param>
    /// <param name="minZoom">Нижняя граница зума.</param>
    /// <param name="maxZoom">Верхняя граница зума.</param>
    /// <param name="deepZoomThreshold">
    /// Зум, начиная с которого центр ведётся в <see cref="BigFloat"/>; совпадает с порогом
    /// глубокого движка рендера, иначе тому неоткуда взять положение области с нужным числом знаков.
    /// </param>
    /// <param name="wheelStep">Множитель зума на щелчок колеса без модификаторов.</param>
    protected DeepZoomViewport(double baseViewWidth, FloatExp minZoom, FloatExp maxZoom, double deepZoomThreshold,
        double wheelStep = 1.2)
    {
        BaseViewWidth = baseViewWidth;
        MinZoom = minZoom;
        MaxZoom = maxZoom;
        DeepZoomThreshold = deepZoomThreshold;
        WheelStep = wheelStep;
    }

    public double BaseViewWidth { get; }
    public FloatExp MinZoom { get; }
    public FloatExp MaxZoom { get; }
    public double DeepZoomThreshold { get; }
    public double WheelStep { get; }

    /// <summary>Центр; в глубоком режиме — ближайшее к точному центру значение.</summary>
    public T CenterX { get; private set; }

    /// <inheritdoc cref="CenterX"/>
    public T CenterY { get; private set; }

    public FloatExp Zoom { get; private set; } = FloatExp.One;

    /// <summary>Центр ведётся в <see cref="BigFloat"/>: зум не ниже <see cref="DeepZoomThreshold"/>.</summary>
    public bool IsDeep { get; private set; }

    /// <summary>Ширина вида в мировых единицах.</summary>
    public FloatExp ViewWidth => BaseViewWidth / Zoom;

    /// <summary>Центр в произвольной точности — и в глубоком режиме, и ниже порога.</summary>
    public BigFloat ExactCenterX => IsDeep ? _exactX : ToExact(CenterX);

    /// <inheritdoc cref="ExactCenterX"/>
    public BigFloat ExactCenterY => IsDeep ? _exactY : ToExact(CenterY);

    /// <summary>Идёт перетаскивание вида мышью.</summary>
    public bool IsPanning { get; private set; }

    /// <summary>Есть отрисованный кадр, относительно которого сдвигается грубый предпросмотр.</summary>
    public bool HasRenderedFrame { get; private set; }

    public BigFloat RenderedCenterX { get; private set; }
    public BigFloat RenderedCenterY { get; private set; }
    public FloatExp RenderedZoom { get; private set; } = FloatExp.One;

    protected abstract BigFloat ToExact(T value);
    protected abstract T FromExact(BigFloat value);
    protected abstract T Add(T value, FloatExp shift);

    /// <summary>
    /// Разрядность арифметики центра на UI-потоке: столько бит, сколько нужно на разрешение
    /// пикселей (log2 зума), плюс запас. Ниже ~1e87 — <see cref="BigFloat.MinimumPrecisionBits"/>.
    /// </summary>
    public static int CenterPrecisionBits(FloatExp zoom)
    {
        double zoomBits = zoom.Sign > 0 && zoom.IsFinite ? zoom.Log2() : 0;
        if (!double.IsFinite(zoomBits) || zoomBits < 0) zoomBits = 0;
        int needed = (int)Math.Ceiling(zoomBits) + CenterPrecisionGuardBits;
        return Math.Max(BigFloat.MinimumPrecisionBits, (needed + 63) / 64 * 64);
    }

    /// <summary>
    /// Область точности для арифметики над точным центром при зуме <paramref name="zoom"/>.
    /// <see cref="BigFloat"/> округляет каждую операцию до рабочей точности потока, поэтому без
    /// неё сдвиг центра на сверхглубоком зуме терялся бы целиком.
    /// </summary>
    public static BigFloat.PrecisionScope PrecisionScope(FloatExp zoom) => new(CenterPrecisionBits(zoom));

    /// <summary>Область точности для арифметики над центром при текущем зуме.</summary>
    public BigFloat.PrecisionScope PrecisionScope() => PrecisionScope(Zoom);

    /// <summary>
    /// Новый зум в пределах окна; центр переходит в нужное представление. Бесконечность
    /// упирается в потолок, неположительное значение и NaN не меняют вид.
    /// </summary>
    public void SetZoom(FloatExp zoom)
    {
        if (zoom.Sign <= 0) return;
        Zoom = FloatExp.Clamp(zoom, MinZoom, MaxZoom);
        SyncDeepState();
    }

    /// <summary>
    /// Центр и зум из сохранения. Строки точного центра есть только у глубоких сохранений;
    /// когда они есть и зум за порогом, обычный центр пересобирается из них. Испорченная строка
    /// не мешает открыть сохранение: остаётся обычный центр из того же файла.
    /// </summary>
    public void Restore(T centerX, T centerY, string? exactX, string? exactY, FloatExp zoom)
    {
        Zoom = zoom.Sign > 0 && zoom.IsFinite ? FloatExp.Clamp(zoom, MinZoom, MaxZoom) : FloatExp.One;
        CenterX = centerX;
        CenterY = centerY;
        IsDeep = false;
        if (exactX is { Length: > 0 } && exactY is { Length: > 0 })
        {
            try
            {
                // Разбор округляет до рабочей точности потока — на глубине её поднимаем заранее.
                using var precision = PrecisionScope();
                BigFloat parsedX = BigFloat.Parse(exactX);
                BigFloat parsedY = BigFloat.Parse(exactY);
                _exactX = parsedX;
                _exactY = parsedY;
                IsDeep = Zoom >= DeepZoomThreshold;
                if (IsDeep)
                {
                    CenterX = FromExact(_exactX);
                    CenterY = FromExact(_exactY);
                }
            }
            catch (FormatException)
            {
                IsDeep = false;
            }
        }
        if (!IsDeep) SyncDeepState();
    }

    /// <summary>
    /// Исходный вид. Точный центр гасится напрямую: <see cref="SyncDeepState"/> при спуске с
    /// глубины восстановил бы обычный центр из прежнего точного поверх нового.
    /// </summary>
    public void Reset(T centerX, T centerY, FloatExp zoom)
    {
        CenterX = centerX;
        CenterY = centerY;
        Zoom = FloatExp.Clamp(zoom, MinZoom, MaxZoom);
        _exactX = BigFloat.Zero;
        _exactY = BigFloat.Zero;
        IsDeep = false;
        SyncDeepState();
    }

    /// <summary>
    /// Центр для сохранения: обычный (в глубоком режиме — ближайший к точному) и строки точного
    /// центра, только когда он ведётся.
    /// </summary>
    public (T X, T Y, string? ExactX, string? ExactY) CaptureCenter() => IsDeep
        ? (FromExact(_exactX), FromExact(_exactY), _exactX.ToInvariantString(), _exactY.ToInvariantString())
        : (CenterX, CenterY, null, null);

    /// <summary>
    /// Прибавляет к центру сдвиг в мировых координатах: в глубоком режиме — к точному центру
    /// (мантисса сдвига переносится целиком, обычный центр обновляется следом), иначе — к обычному.
    /// </summary>
    public void ApplyShift(FloatExp shiftX, FloatExp shiftY)
    {
        if (IsDeep)
        {
            using var precision = PrecisionScope();
            _exactX += shiftX.ToBigFloat();
            _exactY += shiftY.ToBigFloat();
            CenterX = FromExact(_exactX);
            CenterY = FromExact(_exactY);
        }
        else
        {
            CenterX = Add(CenterX, shiftX);
            CenterY = Add(CenterY, shiftY);
        }
    }

    /// <summary>
    /// Множитель зума на щелчок колеса: без модификаторов <see cref="WheelStep"/>, с Ctrl — ×10
    /// (до 1e1000 это тысяча щелчков), с Shift — точные ×1.05. Прокрутка назад — обратный множитель.
    /// </summary>
    public double WheelFactor(int delta)
    {
        double step = (Keyboard.Modifiers & ModifierKeys.Control) != 0 ? 10.0
            : (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 1.05
            : WheelStep;
        return delta > 0 ? step : 1 / step;
    }

    /// <summary>
    /// Меняет зум в <paramref name="factor"/> раз так, что мировая точка под
    /// <paramref name="point"/> остаётся на месте. Масштаб по обеим осям — один, по ширине
    /// холста <paramref name="width"/>.
    /// </summary>
    public void ZoomAt(Point point, double width, double height, double factor)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        double fractionX = point.X / width - 0.5;
        double fractionY = (height / 2 - point.Y) / width;

        FloatExp previousZoom = Zoom;
        Zoom = FloatExp.Clamp(Zoom * factor, MinZoom, MaxZoom);

        // Точка под курсором стоит на месте: сдвиг центра — её доля от разности ширин вида.
        FloatExp viewWidthDelta = BaseViewWidth / previousZoom - BaseViewWidth / Zoom;
        SyncDeepState();
        ApplyShift(fractionX * viewWidthDelta, fractionY * viewWidthDelta);
    }

    /// <summary>Мировая точка под экранной точкой холста.</summary>
    public (T X, T Y) ScreenToWorld(Point point, double width, double height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        FloatExp viewWidth = ViewWidth;
        FloatExp offsetX = (point.X - width / 2) / width * viewWidth;
        FloatExp offsetY = (height / 2 - point.Y) / width * viewWidth;
        if (!IsDeep) return (Add(CenterX, offsetX), Add(CenterY, offsetY));
        using var precision = PrecisionScope();
        return (FromExact(_exactX + offsetX.ToBigFloat()), FromExact(_exactY + offsetY.ToBigFloat()));
    }

    public void BeginPan(Point point)
    {
        IsPanning = true;
        _lastPanPoint = point;
    }

    /// <summary>Сдвигает вид вслед за мышью; false — перетаскивания нет.</summary>
    public bool PanTo(Point point, double width)
    {
        if (!IsPanning) return false;
        width = Math.Max(1, width);
        FloatExp viewWidth = ViewWidth;
        ApplyShift((_lastPanPoint.X - point.X) / width * viewWidth, (point.Y - _lastPanPoint.Y) / width * viewWidth);
        _lastPanPoint = point;
        return true;
    }

    /// <summary>Заканчивает перетаскивание; false — его и не было.</summary>
    public bool EndPan()
    {
        if (!IsPanning) return false;
        IsPanning = false;
        return true;
    }

    /// <summary>Отрисованный кадр соответствует текущему виду (запечённый снимок холста).</summary>
    public void MarkRendered() => MarkRendered(ExactCenterX, ExactCenterY, Zoom);

    /// <summary>
    /// Отрисованный кадр посчитан по сохранённому центру и зуму — тому состоянию, которым
    /// запускался рендер: за время рендера вид могли уже сдвинуть.
    /// </summary>
    public void MarkRendered(T centerX, T centerY, string? exactX, string? exactY, FloatExp zoom)
    {
        using var precision = PrecisionScope(zoom);
        MarkRendered(
            exactX is { Length: > 0 } ? BigFloat.Parse(exactX) : ToExact(centerX),
            exactY is { Length: > 0 } ? BigFloat.Parse(exactY) : ToExact(centerY),
            zoom);
    }

    public void MarkRendered(BigFloat centerX, BigFloat centerY, FloatExp zoom)
    {
        RenderedCenterX = centerX;
        RenderedCenterY = centerY;
        RenderedZoom = zoom;
        HasRenderedFrame = true;
    }

    /// <summary>
    /// Сдвиг отрисованного кадра относительно текущего вида в долях ширины вида, по осям
    /// экрана (Y вниз). Разность центров берётся в BigFloat: на глубине она тонет в любом
    /// обычном типе.
    /// </summary>
    public (FloatExp X, FloatExp Y) RenderedOffset()
    {
        using var precision = PrecisionScope();
        FloatExp viewWidth = ViewWidth;
        return (FloatExp.FromBigFloat(RenderedCenterX - ExactCenterX) / viewWidth,
            FloatExp.FromBigFloat(ExactCenterY - RenderedCenterY) / viewWidth);
    }

    /// <summary>
    /// Масштаб и сдвиг (в DIP, от центра холста) отрисованного кадра при текущем виде.
    /// false — кадра ещё нет или величины вне double.
    /// </summary>
    public bool TryGetPreviewTransform(double width, out double scale, out double translationX, out double translationY)
    {
        scale = translationX = translationY = 0;
        if (!HasRenderedFrame || RenderedZoom.Sign <= 0 || Zoom.Sign <= 0 || width <= 0) return false;
        scale = (Zoom / RenderedZoom).ToDouble();
        (FloatExp offsetX, FloatExp offsetY) = RenderedOffset();
        translationX = (offsetX * width).ToDouble();
        translationY = (offsetY * width).ToDouble();
        return double.IsFinite(scale) && double.IsFinite(translationX) && double.IsFinite(translationY);
    }

    /// <summary>
    /// Привязывает к изображению стабильного предпросмотра масштаб и сдвиг от его центра;
    /// <see cref="UpdatePreview"/> затем двигает их вслед за видом холста <paramref name="canvas"/>.
    /// </summary>
    public void AttachPreview(UIElement preview, FrameworkElement canvas)
    {
        _previewCanvas = canvas;
        _previewScale = new ScaleTransform(1, 1);
        _previewTranslation = new TranslateTransform();
        var transform = new TransformGroup();
        transform.Children.Add(_previewScale);
        transform.Children.Add(_previewTranslation);
        preview.RenderTransformOrigin = new Point(0.5, 0.5);
        preview.RenderTransform = transform;
    }

    /// <summary>Сдвигает привязанный предпросмотр под текущий вид; до привязки ничего не делает.</summary>
    public void UpdatePreview()
    {
        if (_previewCanvas is null || _previewScale is null || _previewTranslation is null ||
            !TryGetPreviewTransform(_previewCanvas.ActualWidth, out double scale, out double translationX,
                out double translationY)) return;
        _previewScale.ScaleX = scale;
        _previewScale.ScaleY = scale;
        _previewTranslation.X = translationX;
        _previewTranslation.Y = translationY;
    }

    /// <summary>
    /// Заводит или гасит ведение точного центра по текущему зуму: вверх через порог центр
    /// переносится из обычного, вниз обычный снова становится источником истины.
    /// </summary>
    private void SyncDeepState()
    {
        bool shouldEngage = Zoom >= DeepZoomThreshold;
        if (shouldEngage && !IsDeep)
        {
            using var precision = PrecisionScope();
            _exactX = ToExact(CenterX);
            _exactY = ToExact(CenterY);
            IsDeep = true;
        }
        else if (!shouldEngage && IsDeep)
        {
            CenterX = FromExact(_exactX);
            CenterY = FromExact(_exactY);
            IsDeep = false;
        }
    }
}

/// <summary>Вид с обычным центром в decimal (Мандельброт, Nova, Phoenix, Коллатц).</summary>
public sealed class DecimalDeepZoomViewport(double baseViewWidth, FloatExp minZoom, FloatExp maxZoom,
    double deepZoomThreshold, double wheelStep = 1.2)
    : DeepZoomViewport<decimal>(baseViewWidth, minZoom, maxZoom, deepZoomThreshold, wheelStep)
{
    protected override BigFloat ToExact(decimal value) => BigFloat.FromDecimal(value);
    protected override decimal FromExact(BigFloat value) => value.ToDecimalClamped();
    protected override decimal Add(decimal value, FloatExp shift) => value + (decimal)shift.ToDouble();
}

/// <summary>Вид с обычным центром в double (бассейны Ньютона).</summary>
public sealed class DoubleDeepZoomViewport(double baseViewWidth, FloatExp minZoom, FloatExp maxZoom,
    double deepZoomThreshold, double wheelStep = 1.2)
    : DeepZoomViewport<double>(baseViewWidth, minZoom, maxZoom, deepZoomThreshold, wheelStep)
{
    protected override BigFloat ToExact(double value) => BigFloat.FromDouble(value);
    protected override double FromExact(BigFloat value) => value.ToDouble();
    protected override double Add(double value, FloatExp shift) => value + shift.ToDouble();
}
