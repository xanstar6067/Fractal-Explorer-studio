using System.Windows.Media;
using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Core.Rendering.Laboratories;

/// <summary>
/// Как линия раскладывается на мазки круглой кисти: шаг вдоль линии в пикселях, наименьший
/// радиус кисти и длина, дальше которой линия не рисуется.
/// </summary>
internal readonly record struct LineSampling(double Step, double MinimumRadius, double MaximumLength)
{
    public static readonly LineSampling Standard = new(0.75, 0.5, 50_000);

    /// <summary>Тоньше и длиннее: Вороной, Рекаман, узлы, случайные блуждания и клейновы группы.</summary>
    public static readonly LineSampling Fine = new(0.8, 0.45, 100_000);
}

/// <summary>
/// Растр лаборатории в BGRA: перевод координат лаборатории в пиксели с учётом центра, зума и
/// поворота вида, линии, окружности и многоугольники с полупрозрачным наложением.
/// </summary>
internal sealed class LaboratoryCanvas
{
    private readonly MathematicalLaboratoryState _state;
    private readonly LineSampling _lines;

    public LaboratoryCanvas(int width, int height, MathematicalLaboratoryState state, LineSampling lines)
    {
        Width = width;
        Height = height;
        _state = state;
        _lines = lines;
        Scale = Math.Min(width, height) * 0.5;
        Pixels = new byte[checked(width * height * 4)];
    }

    public int Width { get; }
    public int Height { get; }
    public double Scale { get; }
    public byte[] Pixels { get; }

    public void Clear(Color color) => RasterDrawing.Fill(Pixels, color);

    public (double x, double y) Map(double x, double y)
    {
        double dx = (x - _state.ViewCenterX) * _state.Zoom;
        double dy = (y - _state.ViewCenterY) * _state.Zoom;
        double radians = _state.Rotation * Math.PI / 180;
        double cosine = Math.Cos(radians), sine = Math.Sin(radians);
        double rx = dx * cosine - dy * sine;
        double ry = dx * sine + dy * cosine;
        return (Width / 2d + rx * Scale, Height / 2d - ry * Scale);
    }

    public (double x, double y) Unmap(double x, double y)
    {
        double rotatedX = (x - Width / 2d) / Scale;
        double rotatedY = -(y - Height / 2d) / Scale;
        double radians = _state.Rotation * Math.PI / 180;
        double cosine = Math.Cos(radians), sine = Math.Sin(radians);
        double dx = rotatedX * cosine + rotatedY * sine;
        double dy = -rotatedX * sine + rotatedY * cosine;
        return (_state.ViewCenterX + dx / _state.Zoom, _state.ViewCenterY + dy / _state.Zoom);
    }

    public void SetPixel(int x, int y, Color color)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return;
        int offset = (y * Width + x) * 4;
        Pixels[offset] = color.B;
        Pixels[offset + 1] = color.G;
        Pixels[offset + 2] = color.R;
        Pixels[offset + 3] = 255;
    }

    public void FillScreenRect(int left, int top, int width, int height, Color color)
    {
        int right = Math.Min(Width, left + width);
        int bottom = Math.Min(Height, top + height);
        left = Math.Max(0, left); top = Math.Max(0, top);
        for (int y = top; y < bottom; y++)
        {
            int offset = (y * Width + left) * 4;
            for (int x = left; x < right; x++, offset += 4)
            {
                Pixels[offset] = color.B;
                Pixels[offset + 1] = color.G;
                Pixels[offset + 2] = color.R;
                Pixels[offset + 3] = 255;
            }
        }
    }

    public void PixelSquare(int x, int y, int size, Color color, double opacity)
    {
        int half = Math.Max(0, size / 2);
        for (int py = y - half; py <= y + half; py++)
            for (int px = x - half; px <= x + half; px++)
                Blend(px, py, color, opacity);
    }

    public void Plot(double x, double y, Color color, double opacity, double radius)
    {
        (double sx, double sy) = Map(x, y);
        Stamp(sx, sy, radius, color, opacity);
    }

    public void Line(
        double x1, double y1, double x2, double y2,
        double thickness, Color color, double opacity = 1)
    {
        (double sx1, double sy1) = Map(x1, y1);
        (double sx2, double sy2) = Map(x2, y2);
        ScreenLine(sx1, sy1, sx2, sy2, thickness, color, opacity);
    }

    public void ScreenLine(
        double x1, double y1, double x2, double y2,
        double thickness, Color color, double opacity = 1)
    {
        double dx = x2 - x1, dy = y2 - y1;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (!double.IsFinite(length) || length > _lines.MaximumLength) return;
        int steps = Math.Max(1, (int)Math.Ceiling(length / _lines.Step));
        double radius = Math.Max(_lines.MinimumRadius, thickness / 2);
        for (int step = 0; step <= steps; step++)
        {
            double amount = (double)step / steps;
            Stamp(x1 + dx * amount, y1 + dy * amount, radius, color, opacity);
        }
    }

    public void Circle(
        double x, double y, double radius, double thickness,
        Color color, bool filled, double opacity = 1)
    {
        (double sx, double sy) = Map(x, y);
        double pixelRadius = Math.Abs(radius * _state.Zoom * Scale);
        if (pixelRadius < 0.2 || pixelRadius > Math.Max(Width, Height) * 8) return;
        if (opacity >= 0.999)
        {
            RasterDrawing.DrawCircle(Pixels, Width, Height, sx, sy, pixelRadius,
                Math.Max(0.5, thickness), color, filled);
            return;
        }
        if (filled)
        {
            Stamp(sx, sy, pixelRadius, color, opacity);
            return;
        }
        int segments = Math.Clamp((int)(pixelRadius * Math.Tau / 2), 24, 720);
        double previousX = sx + pixelRadius, previousY = sy;
        for (int i = 1; i <= segments; i++)
        {
            double angle = Math.Tau * i / segments;
            double nextX = sx + pixelRadius * Math.Cos(angle);
            double nextY = sy + pixelRadius * Math.Sin(angle);
            ScreenLine(previousX, previousY, nextX, nextY, thickness, color, opacity);
            previousX = nextX; previousY = nextY;
        }
    }

    public void Polygon(
        IReadOnlyList<LaboratoryPoint> points, Color color,
        bool filled, double value)
    {
        if (points.Count < 3) return;
        var mapped = points.Select(point => Map(point.X, point.Y)).ToArray();
        if (filled) FillPolygon(mapped, color, value);
        else
            for (int i = 0; i < points.Count; i++)
                Line(points[i].X, points[i].Y,
                    points[(i + 1) % points.Count].X, points[(i + 1) % points.Count].Y,
                    value, color);
    }

    private void FillPolygon((double x, double y)[] points, Color color, double opacity)
    {
        int minimumY = Math.Max(0, (int)Math.Floor(points.Min(point => point.y)));
        int maximumY = Math.Min(Height - 1, (int)Math.Ceiling(points.Max(point => point.y)));
        var intersections = new List<double>(points.Length);
        for (int y = minimumY; y <= maximumY; y++)
        {
            intersections.Clear();
            double scan = y + 0.5;
            for (int i = 0; i < points.Length; i++)
            {
                (double x, double y) first = points[i];
                (double x, double y) second = points[(i + 1) % points.Length];
                if (first.y > second.y) (first, second) = (second, first);
                if (scan < first.y || scan >= second.y || Math.Abs(second.y - first.y) < 1e-12) continue;
                intersections.Add(first.x + (scan - first.y) * (second.x - first.x) / (second.y - first.y));
            }
            intersections.Sort();
            for (int i = 0; i + 1 < intersections.Count; i += 2)
            {
                int left = Math.Max(0, (int)Math.Ceiling(intersections[i]));
                int right = Math.Min(Width - 1, (int)Math.Floor(intersections[i + 1]));
                for (int x = left; x <= right; x++) Blend(x, y, color, opacity);
            }
        }
    }

    private void Stamp(double centerX, double centerY, double radius, Color color, double opacity)
    {
        int left = Math.Max(0, (int)Math.Floor(centerX - radius - 1));
        int right = Math.Min(Width - 1, (int)Math.Ceiling(centerX + radius + 1));
        int top = Math.Max(0, (int)Math.Floor(centerY - radius - 1));
        int bottom = Math.Min(Height - 1, (int)Math.Ceiling(centerY + radius + 1));
        for (int y = top; y <= bottom; y++)
            for (int x = left; x <= right; x++)
            {
                double dx = x + 0.5 - centerX, dy = y + 0.5 - centerY;
                double coverage = Math.Clamp(radius + 0.75 - Math.Sqrt(dx * dx + dy * dy), 0, 1) * opacity;
                if (coverage > 0) Blend(x, y, color, coverage);
            }
    }

    private void Blend(int x, int y, Color color, double opacity)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height || opacity <= 0) return;
        opacity = Math.Clamp(opacity, 0, 1);
        int offset = (y * Width + x) * 4;
        double inverse = 1 - opacity;
        Pixels[offset] = (byte)Math.Round(Pixels[offset] * inverse + color.B * opacity);
        Pixels[offset + 1] = (byte)Math.Round(Pixels[offset + 1] * inverse + color.G * opacity);
        Pixels[offset + 2] = (byte)Math.Round(Pixels[offset + 2] * inverse + color.R * opacity);
        Pixels[offset + 3] = 255;
    }
}
