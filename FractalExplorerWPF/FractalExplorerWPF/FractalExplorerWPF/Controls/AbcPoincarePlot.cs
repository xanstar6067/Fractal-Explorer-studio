using System.Globalization;
using System.Windows;
using System.Windows.Media;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;

namespace FractalExplorerWPF.Controls;

/// <summary>Positive oriented intersections of trajectories with a periodic coordinate plane.</summary>
public sealed class AbcPoincarePlot : FrameworkElement
{
    private IReadOnlyList<AbcSectionPoint> _points = [];
    private AbcFlowSettings _settings = new();
    private Fractal3DState? _state;
    public AbcPoincarePlot() { Height = 250; MinWidth = 200; }
    public void SetPoints(IReadOnlyList<AbcSectionPoint> points, AbcFlowSettings settings, Fractal3DState? state)
    { _points = points; _settings = settings; _state = state; InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        Brush foreground = TryFindResource("Theme.SecondaryTextBrush") as Brush ?? Brushes.Gray;
        Brush background = TryFindResource("Theme.PanelBackgroundBrush") as Brush ?? Brushes.Transparent;
        double side = Math.Max(1, Math.Min(ActualWidth - 32, ActualHeight - 24));
        var rect = new Rect((ActualWidth - side) / 2, 3, side, side);
        var pen = new Pen(foreground, .6);
        dc.DrawRectangle(background, pen, rect);
        dc.DrawLine(pen, new(rect.Left, rect.Top + side / 2), new(rect.Right, rect.Top + side / 2));
        dc.DrawLine(pen, new(rect.Left + side / 2, rect.Top), new(rect.Left + side / 2, rect.Bottom));
        var palette = _state?.ResolvePalette() ?? Fractal3DPalettes.Get("Неон");
        var brushes = new SolidColorBrush[64];
        for (int i = 0; i < brushes.Length; i++)
        {
            if (palette.Colors.Count == 0) { brushes[i] = new(Colors.White); continue; }
            double t = i / 63.0;
            int index = (int)(t * (palette.Colors.Count - 1)), next = Math.Min(index + 1, palette.Colors.Count - 1);
            Color a = palette.Colors[index], b = palette.Colors[next];
            double f = palette.IsGradient ? t * (palette.Colors.Count - 1) - index : 0;
            brushes[i] = new(Color.FromRgb((byte)(a.R + f * (b.R - a.R)), (byte)(a.G + f * (b.G - a.G)), (byte)(a.B + f * (b.B - a.B))));
            brushes[i].Freeze();
        }
        dc.PushClip(new RectangleGeometry(rect));
        foreach (var point in _points)
        {
            double value = _state?.ColoringMode switch
            {
                Fractal3DColoringMode.OrbitTrap => Math.Clamp(.5 + .5 * point.Lyapunov / _settings.LyapunovRange, 0, 1),
                Fractal3DColoringMode.CrossTrap => point.Speed,
                Fractal3DColoringMode.Escape => point.Time / _settings.Duration,
                _ => point.Seed / (double)Math.Max(_settings.SeedCount - 1, 1)
            };
            value = value * (_state?.ColorScale ?? 1) + (_state?.ColorOffset ?? 0);
            value = _state?.ColorRepeat switch
            {
                Fractal3DColorRepeat.Cycle => value - Math.Floor(value),
                Fractal3DColorRepeat.Mirror => 1 - Math.Abs(2 * (value - Math.Floor(value)) - 1),
                _ => Math.Clamp(value, 0, 1)
            };
            if (_state?.ColorRepeat == Fractal3DColorRepeat.Mirror) value = value * value * (3 - 2 * value);
            value = Math.Pow(value, Math.Clamp(palette.Gamma, .05, 8));
            Brush brush = _state?.ColoringMode == Fractal3DColoringMode.Material
                ? new SolidColorBrush(_state.SurfaceColor) : brushes[(int)(value * 63)];
            dc.DrawEllipse(brush, null,
                new(rect.Left + (point.Position.X / Math.Tau + .5) * side,
                    rect.Top + (.5 - point.Position.Y / Math.Tau) * side), 1.6, 1.6);
        }
        dc.Pop();
        var text = new FormattedText("−π                     0                     π", CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(text, new(rect.Left, rect.Bottom + 3));
    }
}
