using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Point = System.Windows.Point;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Pen = System.Windows.Media.Pen;

namespace FractalExplorerWPF.Controls;

/// <summary>Local turn angle: the faint arrow is before the command, the colored arrow after it.</summary>
public sealed class TurtleTurnDial : FrameworkElement
{
    public static readonly DependencyProperty AngleProperty = DependencyProperty.Register(nameof(Angle), typeof(double),
        typeof(TurtleTurnDial), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender |
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    public double Angle { get => (double)GetValue(AngleProperty); set => SetValue(AngleProperty, value); }
    public TurtleTurnDial()
    {
        Focusable = true; Cursor = Cursors.Hand;
        ToolTip = "Серая стрелка — до поворота, цветная — после. Перетащите стрелку; ←/→ меняют угол на 1°, Shift — на 10°.";
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var accent = TryFindResource("Theme.AccentPrimaryBrush") as Brush ?? Brushes.DeepSkyBlue;
        var line = TryFindResource("Theme.BorderBrush") as Brush ?? Brushes.Gray;
        var text = TryFindResource("Theme.PrimaryTextBrush") as Brush ?? Brushes.White;
        Point c = new(ActualWidth / 2, ActualHeight / 2 - 6);
        double r = Math.Max(1, Math.Min(ActualWidth, ActualHeight) / 2 - 15);
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        dc.DrawEllipse(null, new Pen(line, IsKeyboardFocused ? 2 : 1), c, r, r);
        dc.DrawLine(new Pen(line, 2), c, c + new Vector(0, -r));
        double rad = Angle * Math.PI / 180;
        Vector v = new(Math.Sin(rad), -Math.Cos(rad)), side = new(-v.Y, v.X);
        Point tip = c + v * r;
        dc.DrawLine(new Pen(accent, 3), c, tip);
        dc.DrawLine(new Pen(accent, 3), tip, tip - v * 8 + side * 4);
        dc.DrawLine(new Pen(accent, 3), tip, tip - v * 8 - side * 4);
        var label = new FormattedText(Angle.ToString("0.#", CultureInfo.CurrentCulture) + "°",
            CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, text,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(label, new(c.X - label.Width / 2, c.Y + r + 3));
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    { base.OnMouseLeftButtonDown(e); Focus(); CaptureMouse(); SetAngle(e.GetPosition(this)); e.Handled = true; }
    protected override void OnMouseMove(MouseEventArgs e)
    { base.OnMouseMove(e); if (IsMouseCaptured) SetAngle(e.GetPosition(this)); }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    { base.OnMouseLeftButtonUp(e); ReleaseMouseCapture(); e.Handled = true; }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is not (Key.Left or Key.Right)) return;
        double step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 10 : 1;
        SetCurrentValue(AngleProperty, Math.Clamp(Angle + (e.Key == Key.Left ? -step : step), -180, 180));
        e.Handled = true;
    }
    private void SetAngle(Point p)
    {
        double x = p.X - ActualWidth / 2, y = ActualHeight / 2 - 6 - p.Y;
        if (x * x + y * y > 9) SetCurrentValue(AngleProperty, Math.Round(Math.Atan2(x, y) * 180 / Math.PI));
    }
}
