using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Core.Rendering;

public sealed class McMullenRenderer
{
    public const double BaseViewWidth = 3.6;
    private readonly McMullenState _state;
    private readonly Color[] _colors;
    private readonly McMullenDynamics _dynamics;
    private readonly int _width, _height;

    public McMullenRenderer(McMullenState state, int width, int height)
    {
        state.Validate();
        if (width < 1 || height < 1) throw new ArgumentOutOfRangeException(nameof(width));
        _state = state.Clone(); _width = width; _height = height;
        _colors = NewtonPaletteManager.AdjustColors(_state.Palette, 1024).ToArray();
        _dynamics = new(_state);
    }

    public byte[] RenderTile(MandelbrotRenderTile tile, CancellationToken token)
    {
        var pixels = new byte[checked(tile.Width * tile.Height * 4)];
        double units = BaseViewWidth / (_width * _state.ViewZoom);
        for (int y = 0; y < tile.Height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (int x = 0; x < tile.Width; x++)
            {
                Complex point = new(_state.ViewCenterX + (tile.X + x + .5 - _width / 2.0) * units,
                    _state.ViewCenterY - (tile.Y + y + .5 - _height / 2.0) * units);
                McMullenDynamics dynamics = _state.Plane == McMullenPlane.Parameter ? new(_state, point) : _dynamics;
                Complex initial = _state.Plane == McMullenPlane.Parameter ? dynamics.CriticalPoint() : point;
                var orbit = dynamics.Analyze(initial, token);
                Color color = ColorFor(orbit);
                int offset = (y * tile.Width + x) * 4;
                pixels[offset] = color.B; pixels[offset + 1] = color.G; pixels[offset + 2] = color.R; pixels[offset + 3] = 255;
            }
        }
        return pixels;
    }

    private Color ColorFor(McMullenOrbit orbit)
    {
        if (orbit.Outcome == McMullenOutcome.NumericalLimit) return Colors.Magenta;
        bool escaped = orbit.Outcome is McMullenOutcome.Escaped or McMullenOutcome.Pole;
        double value;
        switch (_state.Coloring)
        {
            case McMullenColoring.Phase: value = orbit.Phase * 12 / _state.ColorScale; break;
            case McMullenColoring.CircleTrap:
            case McMullenColoring.CrossTrap:
                value = -Math.Log10(Math.Max(1e-15, orbit.TrapDistance)) / _state.ColorScale; break;
            case McMullenColoring.Period when orbit.Outcome == McMullenOutcome.Periodic:
                value = orbit.Period * 0.618033988749895 * 12 / _state.ColorScale; break;
            default:
                if (!escaped) return _state.Palette.BackgroundColor;
                value = orbit.Smooth / _state.ColorScale;
                if (_state.Coloring == McMullenColoring.EscapeRoute && orbit.Outcome == McMullenOutcome.Pole) value += 0.5;
                break;
        }
        value += _state.ColorOffset;
        value -= Math.Floor(value);
        Color color = _colors[Math.Clamp((int)(value * _colors.Length), 0, _colors.Length - 1)];
        if (_state.Palette.IsGradient && escaped)
        {
            double light = 0.6 + 0.4 * (1 - Math.Exp(-0.2 * orbit.Iterations));
            color = Color.FromRgb((byte)(color.R * light), (byte)(color.G * light), (byte)(color.B * light));
        }
        return color;
    }

    public static Task<BitmapSource> RenderStateAsync(McMullenState state, int width, int height,
        CancellationToken token, IProgress<int>? progress = null, int ssaa = 1, int threads = 0)
    {
        // Snapshot before scheduling: UI edits must never mutate an in-flight export.
        state.Validate(); var snapshot = state.Clone();
        return SsaaBitmapRenderer.RenderAsync(width, height, ssaa, 4, (pixels, w, h, stride, report) =>
        {
            var renderer = new McMullenRenderer(snapshot, w, h);
            int completed = 0;
            Parallel.For(0, h, new ParallelOptions { CancellationToken = token,
                MaxDegreeOfParallelism = threads > 0 ? threads : Environment.ProcessorCount }, y =>
            {
                var tile = renderer.RenderTile(new(0, y, w, 1, 0, y), token);
                Buffer.BlockCopy(tile, 0, pixels, y * stride, tile.Length);
                report(Interlocked.Increment(ref completed) * 100 / h);
            });
        }, token, progress);
    }
}
