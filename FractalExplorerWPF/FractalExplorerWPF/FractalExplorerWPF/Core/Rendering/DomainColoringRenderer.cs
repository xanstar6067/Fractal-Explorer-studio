using System.Numerics;
using System.Windows.Media;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Core.Rendering;

public sealed class DomainColoringRenderer
{
    public const double BaseScale = 4;
    private const double TwoPi = Math.PI * 2;
    private const double InverseLogTwo = 1.4426950408889634;
    private readonly DomainColoringState _state;
    private readonly CompiledComplexExpression _formula;

    public DomainColoringRenderer(DomainColoringState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (string.IsNullOrWhiteSpace(state.Formula))
            throw new InvalidOperationException("Введите комплексную функцию от z.");

        ExpressionNode expression = new Parser(new Tokenizer(state.Formula.Trim()).Tokenize()).Parse();
        _formula = CompiledComplexExpression.Compile(expression);
        _state = state;
        ParsedFormula = expression.PrintSimple();
    }

    public string ParsedFormula { get; }

    public byte[]? RenderTile(
        MandelbrotRenderTile tile,
        int canvasWidth,
        int canvasHeight,
        CancellationToken token)
    {
        if (tile.Width <= 0 || tile.Height <= 0 || canvasWidth <= 0 || canvasHeight <= 0)
            return null;

        byte[] pixels = new byte[checked(tile.Width * tile.Height * 4)];
        PixelTransform transform = CreateTransform(canvasWidth, canvasHeight);
        int offset = 0;
        for (int localY = 0; localY < tile.Height; localY++)
        {
            if ((localY & 7) == 0) token.ThrowIfCancellationRequested();
            int y = tile.Y + localY;
            for (int localX = 0; localX < tile.Width; localX++)
            {
                int x = tile.X + localX;
                WritePixel(pixels, offset, x, y, transform);
                offset += 4;
            }
        }
        return pixels;
    }

    public void Render(
        byte[] buffer,
        int width,
        int height,
        int stride,
        int threadCount,
        CancellationToken token,
        Action<int>? reportProgress = null)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (width <= 0 || height <= 0 || stride < width * 4 || buffer.Length < stride * height)
            throw new ArgumentOutOfRangeException(nameof(width));

        int completedRows = 0;
        var options = new ParallelOptions
        {
            CancellationToken = token,
            MaxDegreeOfParallelism = Math.Clamp(threadCount, 1, Environment.ProcessorCount)
        };
        PixelTransform transform = CreateTransform(width, height);
        Parallel.For(0, height, options, y =>
        {
            int offset = y * stride;
            for (int x = 0; x < width; x++)
            {
                WritePixel(buffer, offset, x, y, transform);
                offset += 4;
            }
            int done = Interlocked.Increment(ref completedRows);
            if (done == height || done % Math.Max(1, height / 100) == 0)
                reportProgress?.Invoke(done * 100 / height);
        });
    }

    // Масштаб и половины размеров холста не зависят от пикселя. Выносим их из
    // внутреннего цикла; сами пиксельные выражения ниже сохранены дословно
    // (тот же порядок операций), поэтому результат бит-в-бит прежний.
    private readonly record struct PixelTransform(
        double Scale, double HalfWidth, double HalfHeight, double UnitsPerPixel, int Width);

    private PixelTransform CreateTransform(int width, int height)
    {
        double zoom = Math.Clamp(_state.Zoom, 1e-12, 1e12);
        double scale = BaseScale / zoom;
        return new PixelTransform(scale, width / 2.0, height / 2.0, scale / width, width);
    }

    private void WritePixel(byte[] pixels, int offset, int x, int y, in PixelTransform transform)
    {
        double scale = transform.Scale;
        int width = transform.Width;
        double worldX = _state.CenterX + (x + 0.5 - transform.HalfWidth) * scale / width;
        double worldY = _state.CenterY + (transform.HalfHeight - y - 0.5) * scale / width;
        double unitsPerPixel = transform.UnitsPerPixel;

        Complex value = _formula.Evaluate(new Complex(worldX, worldY));
        Color color = Colorize(value);
        if (_state.ShowAxes &&
            (Math.Abs(worldX) <= unitsPerPixel * 0.65 || Math.Abs(worldY) <= unitsPerPixel * 0.65))
        {
            color = Blend(color, Colors.White, 0.55);
        }

        pixels[offset] = color.B;
        pixels[offset + 1] = color.G;
        pixels[offset + 2] = color.R;
        pixels[offset + 3] = byte.MaxValue;
    }

    private Color Colorize(Complex value)
    {
        if (!double.IsFinite(value.Real) || !double.IsFinite(value.Imaginary))
            return _state.InvalidColor;

        double magnitude = value.Magnitude;
        if (!double.IsFinite(magnitude)) return _state.InvalidColor;
        if (magnitude <= 1e-300) return Colors.Black;

        double normalizedArgument = Wrap01((Math.Atan2(value.Imaginary, value.Real) + Math.PI) / TwoPi);
        double hue = Wrap01(normalizedArgument * _state.HueCycles);
        Color baseColor = SamplePalette(_state.Palette, hue);
        baseColor = Desaturate(baseColor, _state.Saturation);
        double logarithmicMagnitude = Math.Log(magnitude) * InverseLogTwo;
        double valueLevel = 1;

        switch (_state.ColoringMode)
        {
            case DomainColoringMode.SmoothMagnitude:
                double response = 0.5 + 0.5 * Math.Tanh(logarithmicMagnitude * _state.MagnitudeExposure * 0.55);
                valueLevel = 0.22 + 0.78 * response;
                break;

            case DomainColoringMode.LogarithmicRings:
                valueLevel = RingBrightness(logarithmicMagnitude);
                break;

            case DomainColoringMode.PhaseContours:
                valueLevel = PhaseBrightness(normalizedArgument);
                break;

            case DomainColoringMode.PolarGrid:
                valueLevel = Math.Min(RingBrightness(logarithmicMagnitude),
                    PhaseBrightness(normalizedArgument));
                break;

            case DomainColoringMode.ArgumentOnly:
                valueLevel = 1;
                break;
        }

        return Scale(baseColor, Math.Clamp(valueLevel, 0, 1));
    }

    private double RingBrightness(double logarithmicMagnitude)
    {
        double wave = 0.5 + 0.5 * Math.Cos(TwoPi * logarithmicMagnitude * _state.RingDensity);
        double contour = Math.Pow(wave, 7);
        return 1 - _state.ContourStrength * contour;
    }

    private double PhaseBrightness(double normalizedArgument)
    {
        double distance = Math.Abs(Math.Sin(Math.PI * normalizedArgument * _state.PhaseSectors));
        double contour = Math.Exp(-distance * distance * 80);
        return 1 - _state.ContourStrength * contour;
    }

    private static double Wrap01(double value)
    {
        value -= Math.Floor(value);
        return value < 0 ? value + 1 : value;
    }

    // Круговая выборка: конец палитры плавно переходит в начало, так как arg f(z)
    // сам циклический (совпадает по модулю 2π), поэтому подбирать одинаковые
    // крайние цвета вручную не нужно.
    private static Color SamplePalette(DomainColoringPalette palette, double normalized)
    {
        IReadOnlyList<Color> colors = palette.Colors;
        if (colors.Count == 0) return Colors.White;
        double position = Wrap01(palette.Reverse ? 1 - normalized : normalized);
        if (colors.Count == 1) return ApplyGamma(colors[0], palette.Gamma);

        Color result;
        if (!palette.IsGradient)
        {
            int index = (int)(position * colors.Count) % colors.Count;
            result = colors[index];
        }
        else
        {
            double scaled = position * colors.Count;
            int left = (int)Math.Floor(scaled) % colors.Count;
            int right = (left + 1) % colors.Count;
            double fraction = scaled - Math.Floor(scaled);
            result = Lerp(colors[left], colors[right], fraction);
        }
        return ApplyGamma(result, palette.Gamma);
    }

    private static Color Desaturate(Color color, double saturation)
    {
        saturation = Math.Clamp(saturation, 0, 1);
        if (saturation >= 1) return color;
        double gray = 0.299 * color.R + 0.587 * color.G + 0.114 * color.B;
        return Color.FromRgb(
            ToByte((color.R * saturation + gray * (1 - saturation)) / 255),
            ToByte((color.G * saturation + gray * (1 - saturation)) / 255),
            ToByte((color.B * saturation + gray * (1 - saturation)) / 255));
    }

    private static Color Scale(Color color, double amount) => Color.FromRgb(
        ToByte(color.R / 255.0 * amount), ToByte(color.G / 255.0 * amount), ToByte(color.B / 255.0 * amount));

    private static Color Lerp(Color first, Color second, double amount) => Color.FromRgb(
        ToByte((first.R + (second.R - first.R) * amount) / 255),
        ToByte((first.G + (second.G - first.G) * amount) / 255),
        ToByte((first.B + (second.B - first.B) * amount) / 255));

    private static Color ApplyGamma(Color color, double gamma)
    {
        if (Math.Abs(gamma - 1) < 1e-9) return color;
        double inverse = 1 / gamma;
        return Color.FromRgb(
            ToByte(Math.Pow(color.R / 255.0, inverse)),
            ToByte(Math.Pow(color.G / 255.0, inverse)),
            ToByte(Math.Pow(color.B / 255.0, inverse)));
    }

    private static Color Blend(Color first, Color second, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromRgb(
            ToByte((first.R * (1 - amount) + second.R * amount) / 255),
            ToByte((first.G * (1 - amount) + second.G * amount) / 255),
            ToByte((first.B * (1 - amount) + second.B * amount) / 255));
    }

    private static byte ToByte(double value) =>
        (byte)Math.Clamp((int)Math.Round(value * 255), 0, 255);
}
