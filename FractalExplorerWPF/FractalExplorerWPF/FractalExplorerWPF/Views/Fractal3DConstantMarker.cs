using System.Numerics;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Views;

/// <summary>Проекция C и освещённая зелёная минисфера поверх кадра параметрического множества.</summary>
internal static class Fractal3DConstantMarker
{
    public static void Draw(Canvas layer, Fractal3DState state, Vector3 constant)
    {
        layer.Children.Clear();
        double width = layer.ActualWidth;
        double height = layer.ActualHeight;
        if (width <= 1 || height <= 1 || !float.IsFinite(constant.X) ||
            !float.IsFinite(constant.Y) || !float.IsFinite(constant.Z)) return;

        Fractal3DPose pose = Fractal3DCamera.Pose(state);
        Vector3 offset = constant - pose.Position;
        double depth = Vector3.Dot(offset, pose.Forward);
        if (depth <= 0.01) return;

        (double x, double y) = Fractal3DCamera.Project(pose, state.FieldOfView, constant, width, height);
        if (!double.IsFinite(x) || !double.IsFinite(y) || x < -30 || y < -30 || x > width + 30 || y > height + 30)
            return;

        // Радиус задан в мировых координатах, поэтому сфера меняет размер при приближении.
        (double edgeX, _) = Fractal3DCamera.Project(pose, state.FieldOfView,
            constant + pose.Right * 0.065f, width, height);
        double diameter = Math.Clamp(2 * Math.Abs(edgeX - x), 11, 28);
        var sphere = new Ellipse
        {
            Width = diameter,
            Height = diameter,
            Stroke = new SolidColorBrush(Color.FromRgb(8, 58, 18)),
            StrokeThickness = 1.2,
            Fill = new RadialGradientBrush
            {
                Center = new System.Windows.Point(0.32, 0.27),
                GradientOrigin = new System.Windows.Point(0.27, 0.21),
                RadiusX = 0.75,
                RadiusY = 0.75,
                GradientStops = new GradientStopCollection
                {
                    new(Color.FromRgb(225, 255, 174), 0),
                    new(Color.FromRgb(96, 238, 72), 0.35),
                    new(Color.FromRgb(16, 135, 35), 0.72),
                    new(Color.FromRgb(5, 54, 16), 1)
                }
            },
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Color.FromRgb(57, 240, 81),
                BlurRadius = 9,
                ShadowDepth = 0,
                Opacity = 0.75
            }
        };
        Canvas.SetLeft(sphere, x - diameter / 2);
        Canvas.SetTop(sphere, y - diameter / 2);
        layer.Children.Add(sphere);
    }
}
