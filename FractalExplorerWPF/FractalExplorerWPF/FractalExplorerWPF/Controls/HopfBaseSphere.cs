using System.Numerics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FractalExplorerWPF.Models;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Pen = System.Windows.Media.Pen;

namespace FractalExplorerWPF.Controls;

/// <summary>Orthographic S² selector. The back hemisphere is explicit, so clicks are unambiguous.</summary>
public sealed class HopfBaseSphere : FrameworkElement
{
    public IReadOnlyList<HopfPoint> Points { get; private set; } = [];
    public int Selected { get; private set; } = -1;
    public bool Back { get; set; }
    public Func<int, Color>? ColorAt { get; set; }
    public event Action<int, HopfPoint?>? PointPicked;
    public HopfBaseSphere() { Height = 210; Cursor = Cursors.Hand; Focusable = true; }
    public void SetPoints(IReadOnlyList<HopfPoint> points, int selected)
    { Points = points; Selected = selected; InvalidateVisual(); }
    private (Point Center, double Radius) Circle => (new(ActualWidth / 2, ActualHeight / 2), Math.Max(1, Math.Min(ActualWidth, ActualHeight) / 2 - 10));
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        Brush line = TryFindResource("Theme.BorderBrush") as Brush ?? Brushes.Gray;
        Brush accent = TryFindResource("Theme.AccentPrimaryBrush") as Brush ?? Brushes.DeepSkyBlue;
        Brush fill = TryFindResource("Theme.BaseBackgroundBrush") as Brush ?? Brushes.Black;
        var (center,radius) = Circle;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        dc.DrawEllipse(fill,new Pen(line,1),center,radius,radius);
        dc.DrawEllipse(null,new Pen(line,1),center,radius*.5,radius);
        dc.DrawEllipse(null,new Pen(line,1),center,radius,radius*.5);
        for (int pass=0;pass<2;pass++)
            for (int i=0;i<Points.Count;i++)
            {
                Vector3 n = Points[i].OnSphere();
                bool front = Back ? n.Z <= 0 : n.Z >= 0;
                if (front != (pass==1)) continue;
                var p = new Point(center.X+n.X*radius,center.Y-n.Y*radius);
                Brush color = ColorAt is null ? accent : new SolidColorBrush(ColorAt(i));
                dc.DrawEllipse(front ? color : null,new Pen(front ? color : line,1),p,3,3);
                if(i==Selected) dc.DrawEllipse(null,new Pen(color,2),p,7,7);
            }
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e); Focus();
        var (center,radius)=Circle; Point p=e.GetPosition(this);
        double x=(p.X-center.X)/radius, y=(center.Y-p.Y)/radius, r2=x*x+y*y;
        if(r2>1) return;
        int nearest=-1; double best=100;
        for(int i=0;i<Points.Count;i++)
        {
            Vector3 n=Points[i].OnSphere();
            if(Back ? n.Z>1e-6 : n.Z< -1e-6) continue;
            double d=Math.Pow(p.X-center.X-n.X*radius,2)+Math.Pow(p.Y-center.Y+n.Y*radius,2);
            if(d<best) { best=d; nearest=i; }
        }
        bool add=(Keyboard.Modifiers&ModifierKeys.Control)!=0;
        if(!add && nearest>=0) PointPicked?.Invoke(nearest,null);
        else PointPicked?.Invoke(-1,HopfSettings.FromSphere(new((float)x,(float)y,(float)(Math.Sqrt(1-r2)*(Back?-1:1)))));
        e.Handled=true;
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if(e.Key is not (Key.Left or Key.Right) || Points.Count==0) return;
        int next=(Selected+(e.Key==Key.Left?-1:1)+Points.Count)%Points.Count;
        PointPicked?.Invoke(next,null); e.Handled=true;
    }
}
