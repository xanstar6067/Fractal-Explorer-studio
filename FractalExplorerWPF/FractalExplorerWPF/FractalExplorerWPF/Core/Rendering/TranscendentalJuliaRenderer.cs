using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Core.Rendering;

/// <summary>Растровые орбиты и ограниченная адаптивная выборка прообразов точек Жюлиа.</summary>
public sealed class TranscendentalJuliaRenderer
{
    public const double BaseViewWidth = 12;
    private readonly TranscendentalJuliaState _state;
    private readonly TranscendentalJuliaDynamics _dynamics;
    private readonly Color[] _colors;
    private readonly Color[] _periodColors;
    private float[]? _julia;
    private int _width, _height;
    public IReadOnlyList<TranscendentalCycle> Attractors { get; private set; } = [];
    public int SeedCount { get; private set; }
    public int SampleCount { get; private set; }
    public int CompletedDepth { get; private set; }
    public int RayCount { get; private set; }
    public int RaySplits { get; private set; }
    public string Description => _state.Plane == TranscendentalJuliaPlane.Parameter
        ? (_state.Kind == TranscendentalJuliaKind.Exponential ? "Карта λ: орбита 0." : "Карта λ: обе орбиты +λ и −λ.") +
            " Цвет — период притягивающего цикла; нераспознанные исходы и численные пределы отмечены отдельно. Двойной щелчок открывает Жюлиа."
        : $"Циклов: {Attractors.Count}; затравок: {SeedCount}; выборка: {SampleCount:N0}; глубина: {CompletedDepth}; частей лучей: {RayCount}; разрывов: {RaySplits}." +
        (_state.Layer != TranscendentalJuliaLayer.Orbits && SeedCount == 0
            ? _state.LambdaReal == 0 && _state.LambdaImaginary == 0 ? " При λ = 0 отображение постоянно; обратного слоя нет." :
                _state.Drawing == TranscendentalJuliaDrawing.Points ? " Отталкивающие точки не найдены." :
                " Хвосты лучей не построены в пределах бюджета; можно сравнить с точками-прообразами." : "");

    public TranscendentalJuliaRenderer(TranscendentalJuliaState state)
    {
        state.Validate();
        _state = state.Clone();
        _dynamics = new(_state);
        _colors = NewtonPaletteManager.AdjustColors(_state.Palette, 7).ToArray();
        _periodColors = NewtonPaletteManager.AdjustColors(new NewtonColorPalette
        { RootColors = _colors.Skip(3).ToList(), ExpansionMode = NewtonPaletteExpansionMode.CyclicRamp }, _state.MaxPeriod).ToArray();
    }

    public void Prepare(int width, int height, CancellationToken token)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        token.ThrowIfCancellationRequested();
        _width = width; _height = height;
        SeedCount = SampleCount = CompletedDepth = RayCount = RaySplits = 0;
        _julia = null;
        if (_state.Plane == TranscendentalJuliaPlane.Parameter) { Attractors = []; return; }
        Attractors = _dynamics.FindAttractors(token);
        _julia = _state.Layer == TranscendentalJuliaLayer.Orbits ? null : new float[checked(width * height)];
        if (_julia is null) return;
        if (_state.Drawing != TranscendentalJuliaDrawing.Points)
        {
            int? rayBudget = _state.Drawing == TranscendentalJuliaDrawing.RaysAndPoints ? _state.PointBudget * 3 / 5 : null;
            var builder = new TranscendentalJuliaRays(_state, width, height, rayBudget);
            var rays = builder.Build(token);
            RayCount = rays.Count; RaySplits = builder.SplitCount;
            SampleCount = builder.Work; CompletedDepth = builder.CompletedDepth;
            SeedCount = rays.Count == 0 ? 0 : _state.Kind == TranscendentalJuliaKind.Sine ? 2 : 1;
            foreach (var ray in rays)
            {
                token.ThrowIfCancellationRequested();
                for (int i = 1; i < ray.Points.Length; i++) Stroke(ray.Points[i - 1], ray.Points[i]);
            }
        }
        if (_state.Drawing == TranscendentalJuliaDrawing.Rays) return;
        PreparePoints(width, height, token);
    }

    private void PreparePoints(int width, int height, CancellationToken token)
    {
        List<Complex> current = [.. _dynamics.FindRepellingSeeds(token)];
        SeedCount += current.Count;
        if (current.Count == 0) return;

        double units = BaseViewWidth / (_state.Zoom * width);
        double branchAxis = _state.Kind == TranscendentalJuliaKind.Exponential ? _state.CenterY : _state.CenterX;
        double period = _state.Kind == TranscendentalJuliaKind.Exponential ? 2 * Math.PI : Math.PI;
        long middle = (long)Math.Round(branchAxis / period);
        // Ветви около вида и начала координат: промежуточные точки за экраном тоже сохраняются.
        var branchSet = Enumerable.Range(-_state.BranchRadius, 2 * _state.BranchRadius + 1)
            .SelectMany(k => new[] { (long)k, middle + k }).ToHashSet();
        // Дальние промежуточные прообразы возвращаются в вид через log/asin.
        // Без них конечный набор k искусственно обрезает волосы по Re z (exp) или |Im z| (sin).
        double extent = _state.Kind == TranscendentalJuliaKind.Exponential
            ? _state.CenterX + BaseViewWidth / (2 * _state.Zoom)
            : Math.Abs(_state.CenterY) + BaseViewWidth * height / (2 * _state.Zoom * width);
        double lambdaMagnitude = new Complex(_state.LambdaReal, _state.LambdaImaginary).Magnitude;
        long far = (long)Math.Clamp(Math.Exp(Math.Clamp(extent + 1 + Math.Log(Math.Max(lambdaMagnitude, 1e-300)) - Math.Log(2 * Math.PI), 0, 21)), 1, 1_000_000_000);
        for (long k = 1; k <= far; k *= 2) { branchSet.Add(k); branchSet.Add(-k); }
        long[] branches = branchSet.Order().ToArray();
        int capacity = Math.Clamp(_state.PointBudget / Math.Max(1, _state.InverseDepth * branches.Length), 32, 20000);
        int visibleCapacity = Math.Max(1, capacity * 3 / 4), hiddenCapacity = Math.Max(1, capacity / 4);
        var random = new Random(_state.SamplingSeed);
        int cellsX = Math.Clamp(width, 32, 2048), cellsY = Math.Clamp(height, 32, 2048);
        for (int depth = 1; depth <= _state.InverseDepth && SampleCount < _state.PointBudget; depth++)
        {
            var visible = new List<Complex>();
            var hidden = new List<Complex>();
            var occupied = new HashSet<long>();
            int visibleSeen = 0, hiddenSeen = 0;
            foreach (Complex w in current)
                foreach (long branch in branches)
                {
                    if ((SampleCount & 255) == 0) token.ThrowIfCancellationRequested();
                    if (SampleCount >= _state.PointBudget) break;
                    SampleCount++;
                    Complex z = _dynamics.Preimage(w, branch);
                    if (!TranscendentalJuliaDynamics.IsFinite(z)) continue;
                    double px = (z.Real - _state.CenterX) / units + width / 2.0 - 0.5;
                    double py = (_state.CenterY - z.Imaginary) / units + height / 2.0 - 0.5;
                    if (px >= -1 && py >= -1 && px < width && py < height)
                    {
                        Splat(px, py);
                        long cell = (long)Math.Clamp((int)(py * cellsY / height), 0, cellsY - 1) * cellsX +
                            Math.Clamp((int)(px * cellsX / width), 0, cellsX - 1);
                        if (occupied.Add(cell)) Reservoir(visible, z, visibleCapacity, ref visibleSeen, random);
                    }
                    else Reservoir(hidden, z, hiddenCapacity, ref hiddenSeen, random);
                }
            current = [.. visible, .. hidden];
            CompletedDepth = depth;
            if (current.Count == 0) break;
        }
        token.ThrowIfCancellationRequested();
    }

    private static void Reservoir(List<Complex> points, Complex z, int capacity, ref int seen, Random random)
    {
        seen++;
        if (points.Count < capacity) points.Add(z);
        else { int index = random.Next(seen); if (index < capacity) points[index] = z; }
    }

    private void Splat(double px, double py)
    {
        // Мягкий отпечаток точки. Толщина в долях ширины изображения сохраняется при экспорте/SSAA.
        double radius = Math.Max(1, _width / 1000.0);
        int left = Math.Max(0, (int)Math.Floor(px - radius));
        int right = Math.Min(_width - 1, (int)Math.Ceiling(px + radius));
        int top = Math.Max(0, (int)Math.Floor(py - radius));
        int bottom = Math.Min(_height - 1, (int)Math.Ceiling(py + radius));
        for (int y = top; y <= bottom; y++)
            for (int x = left; x <= right; x++)
            {
                float coverage = (float)(Math.Max(0, 1 - Math.Abs(x - px) / radius) *
                    Math.Max(0, 1 - Math.Abs(y - py) / radius));
                int index = y * _width + x;
                _julia![index] = Math.Max(_julia[index], coverage);
            }
    }

    private void Stroke(Complex a, Complex b)
    {
        double scale = _width * _state.Zoom / BaseViewWidth;
        double x0 = (a.Real - _state.CenterX) * scale + _width / 2.0 - 0.5;
        double y0 = (_state.CenterY - a.Imaginary) * scale + _height / 2.0 - 0.5;
        double dx = (b.Real - a.Real) * scale, dy = (a.Imaginary - b.Imaginary) * scale;
        double from = 0, to = 1;
        bool Clip(double p, double q)
        {
            if (p == 0) return q >= 0;
            double t = q / p;
            if (p < 0) { if (t > to) return false; from = Math.Max(from, t); }
            else { if (t < from) return false; to = Math.Min(to, t); }
            return true;
        }
        if (!double.IsFinite(x0) || !double.IsFinite(y0) || !double.IsFinite(dx) || !double.IsFinite(dy) ||
            !Clip(-dx, x0 + 2) || !Clip(dx, _width + 1 - x0) || !Clip(-dy, y0 + 2) || !Clip(dy, _height + 1 - y0)) return;
        double length = Math.Sqrt(dx * dx + dy * dy) * (to - from);
        int steps = Math.Clamp((int)Math.Ceiling(length * 2), 1, 4 * (_width + _height));
        for (int i = 0; i <= steps; i++)
        { double t = from + (to - from) * i / steps; Splat(x0 + dx * t, y0 + dy * t); }
    }

    public byte[] RenderTile(MandelbrotRenderTile tile, CancellationToken token)
    {
        if (_width <= 0) throw new InvalidOperationException("Сначала подготовьте кадр.");
        var pixels = new byte[checked(tile.Width * tile.Height * 4)];
        for (int y = 0; y < tile.Height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (int x = 0; x < tile.Width; x++)
                WritePixel(pixels, (y * tile.Width + x) * 4, tile.X + x, tile.Y + y, token);
        }
        return pixels;
    }

    public void Render(byte[] pixels, int width, int height, int stride, int threads, CancellationToken token, Action<int>? progress = null)
    {
        if (stride < checked(width * 4) || pixels.Length < checked(stride * height)) throw new ArgumentException("Малый буфер кадра.");
        Prepare(width, height, token);
        int rows = 0;
        Parallel.For(0, height, new ParallelOptions { CancellationToken = token,
            MaxDegreeOfParallelism = Math.Clamp(threads, 1, Environment.ProcessorCount) }, y =>
        {
            for (int x = 0; x < width; x++) WritePixel(pixels, y * stride + x * 4, x, y, token);
            int done = Interlocked.Increment(ref rows);
            if (done % Math.Max(1, height / 100) == 0 || done == height) progress?.Invoke(done * 100 / height);
        });
    }

    private void WritePixel(byte[] pixels, int offset, int x, int y, CancellationToken token)
    {
        Color background = _state.Palette.BackgroundColor;
        if (_state.Plane == TranscendentalJuliaPlane.Parameter)
        {
            double units = BaseViewWidth / (_state.ViewZoom * _width);
            var lambda = new Complex(_state.ViewCenterX + (x + 0.5 - _width / 2.0) * units,
                _state.ViewCenterY + (_height / 2.0 - y - 0.5) * units);
            var result = TranscendentalParameterMap.Analyze(_state.Kind, lambda, _state.MaxIterations,
                _state.MaxPeriod, _state.Tolerance, token);
            Color first = ParameterColor(result.First);
            Color mapColor = result.Second is { } second ? Blend(first, ParameterColor(second), 0.5) : first;
            pixels[offset] = mapColor.B; pixels[offset + 1] = mapColor.G; pixels[offset + 2] = mapColor.R; pixels[offset + 3] = 255;
            return;
        }
        Color color = background;
        if (_state.Layer != TranscendentalJuliaLayer.Julia)
        {
            double units = BaseViewWidth / (_state.Zoom * _width);
            var z = new Complex(_state.CenterX + (x + 0.5 - _width / 2.0) * units,
                _state.CenterY + (_height / 2.0 - y - 0.5) * units);
            TranscendentalOrbit result = _dynamics.Classify(z, Attractors, token);
            color = result.Outcome switch
            {
                TranscendentalOrbitOutcome.Converged => Blend(background, _colors[3 + result.Attractor % 4],
                    _state.Palette.IsGradient ? 0.15 + 0.65 * Math.Exp(-result.Iterations / 18.0) : 0.8),
                TranscendentalOrbitOutcome.NumericalLimit => _colors[2],
                _ => _colors[1]
            };
        }
        if (_julia is not null) color = Blend(color, _colors[0], Math.Sqrt(_julia[y * _width + x]));
        pixels[offset] = color.B; pixels[offset + 1] = color.G; pixels[offset + 2] = color.R; pixels[offset + 3] = 255;
    }

    private Color ParameterColor(SingularOrbitResult orbit) => orbit.Outcome switch
    {
        TranscendentalOrbitOutcome.Converged => Blend(_state.Palette.BackgroundColor,
            _periodColors[Math.Clamp(orbit.Period - 1, 0, _periodColors.Length - 1)],
            _state.Palette.IsGradient ? 0.35 + 0.6 * Math.Exp(-orbit.Iterations / 90.0) : 0.9),
        TranscendentalOrbitOutcome.NumericalLimit => _colors[2],
        _ => _colors[1]
    };

    private static Color Blend(Color a, Color b, double t) => Color.FromRgb(
        (byte)Math.Clamp(Math.Round(a.R + (b.R - a.R) * t), 0, 255),
        (byte)Math.Clamp(Math.Round(a.G + (b.G - a.G) * t), 0, 255),
        (byte)Math.Clamp(Math.Round(a.B + (b.B - a.B) * t), 0, 255));

    public static Task<BitmapSource> RenderStateAsync(TranscendentalJuliaState state, int width, int height,
        CancellationToken token, IProgress<int>? progress = null, int ssaa = 1, int threads = 0)
    {
        var renderer = new TranscendentalJuliaRenderer(state);
        return SsaaBitmapRenderer.RenderAsync(width, height, ssaa, 4,
            (pixels, w, h, stride, report) => renderer.Render(pixels, w, h, stride,
                threads > 0 ? threads : Environment.ProcessorCount, token, report), token, progress);
    }
}
