using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Point = System.Windows.Point;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Pen = System.Windows.Media.Pen;

namespace FractalExplorerWPF.Controls;

/// <summary>Top view XZ direction dial: click or drag the arrow, keyboard arrows turn it.</summary>
public sealed class DirectionCompass : FrameworkElement
{
    public static readonly DependencyProperty AngleProperty = DependencyProperty.Register(nameof(Angle), typeof(double),
        typeof(DirectionCompass), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender |
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    public double Angle { get => (double)GetValue(AngleProperty); set => SetValue(AngleProperty, value); }
    private static readonly DependencyProperty TextBrushProperty = BrushProperty("TextBrush", Brushes.Gray);
    private static readonly DependencyProperty LineBrushProperty = BrushProperty("LineBrush", Brushes.Gray);
    private static readonly DependencyProperty AccentBrushProperty = BrushProperty("AccentBrush", Brushes.DeepSkyBlue);
    private static readonly DependencyProperty BackgroundBrushProperty = BrushProperty("BackgroundBrush", Brushes.Transparent);
    private static DependencyProperty BrushProperty(string name, Brush fallback) => DependencyProperty.Register(name,
        typeof(Brush), typeof(DirectionCompass), new FrameworkPropertyMetadata(fallback, FrameworkPropertyMetadataOptions.AffectsRender));
    public DirectionCompass()
    {
        SetResourceReference(TextBrushProperty, "Theme.PrimaryTextBrush");
        SetResourceReference(LineBrushProperty, "Theme.BorderBrush");
        SetResourceReference(AccentBrushProperty, "Theme.AccentPrimaryBrush");
        SetResourceReference(BackgroundBrushProperty, "Theme.PanelBackgroundBrush");
        Focusable = true;
        Cursor = Cursors.Hand;
        ToolTip = "Вид сверху (XZ). Направление движения частиц. Нажмите или перетащите стрелку; ←/→ — поворот.";
        System.Windows.Automation.AutomationProperties.SetName(this, "Направление потока в плоскости XZ");
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        Brush text = (Brush)GetValue(TextBrushProperty);
        Brush line = (Brush)GetValue(LineBrushProperty);
        Brush accent = (Brush)GetValue(AccentBrushProperty);
        Brush background = (Brush)GetValue(BackgroundBrushProperty);
        Point center = new(ActualWidth / 2, ActualHeight / 2);
        double r = Math.Max(1, Math.Min(ActualWidth, ActualHeight) / 2 - 22);
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        dc.DrawEllipse(background, new Pen(line, IsKeyboardFocused ? 2 : 1), center, r, r);
        dc.DrawLine(new Pen(line, 1), new(center.X - r, center.Y), new(center.X + r, center.Y));
        dc.DrawLine(new Pen(line, 1), new(center.X, center.Y - r), new(center.X, center.Y + r));
        double angle = Angle * Math.PI / 180;
        Vector direction = new(Math.Sin(angle), -Math.Cos(angle));
        Vector side = new(-direction.Y, direction.X);
        Point tip = center + direction * r * .85;
        dc.DrawLine(new Pen(accent, 4), center, tip);
        dc.DrawLine(new Pen(accent, 4), tip, tip - direction * 12 + side * 7);
        dc.DrawLine(new Pen(accent, 4), tip, tip - direction * 12 - side * 7);
        Label("+Z", new(center.X - 9, center.Y - r - 19));
        Label("−Z", new(center.X - 9, center.Y + r + 3));
        Label("−X", new(center.X - r - 22, center.Y - 8));
        Label("+X", new(center.X + r + 3, center.Y - 8));
        void Label(string s, Point p) => dc.DrawText(new FormattedText(s, System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, text, VisualTreeHelper.GetDpi(this).PixelsPerDip), p);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    { base.OnMouseLeftButtonDown(e); Focus(); CaptureMouse(); SetFromPoint(e.GetPosition(this)); e.Handled = true; }
    protected override void OnMouseMove(MouseEventArgs e)
    { base.OnMouseMove(e); if (IsMouseCaptured) SetFromPoint(e.GetPosition(this)); }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    { base.OnMouseLeftButtonUp(e); ReleaseMouseCapture(); e.Handled = true; }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is not (Key.Left or Key.Right)) return;
        double value = Angle + (e.Key == Key.Left ? -5 : 5);
        SetCurrentValue(AngleProperty, value > 180 ? value - 360 : value < -180 ? value + 360 : value);
        e.Handled = true;
    }
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    { base.OnGotKeyboardFocus(e); InvalidateVisual(); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    { base.OnLostKeyboardFocus(e); InvalidateVisual(); }
    private void SetFromPoint(Point p)
    {
        double x = p.X - ActualWidth / 2, z = ActualHeight / 2 - p.Y;
        if (x * x + z * z > 9) SetCurrentValue(AngleProperty, Math.Round(Math.Atan2(x, z) * 180 / Math.PI));
    }
}
