using System.Numerics;
using System.Reflection;
using System.Windows;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

// Навигация окна трёхмерного фрактала: колесо приближает к точке под курсором, не проскакивая её,
// и не застревает, когда после глубокого приближения курсор указывает на далёкую поверхность.
// Окно не показывается: разметка раскладывается вручную, зонд подменяется известной точкой.
internal static partial class Program
{
    private static void VerifyFractal3DWindowZoom()
    {
        var themeStyles = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if (!Application.Current.Resources.MergedDictionaries.Any(d => d.Source == themeStyles))
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = themeStyles });

        var window = new Fractal3DWindow(Fractal3DKind.Mandelbulb);
        try
        {
            var root = (UIElement)window.Content;
            root.Measure(new Size(1200, 800));
            root.Arrange(new Rect(0, 0, 1200, 800));
            root.UpdateLayout();
            var layer = (FrameworkElement)window.FindName("SavePreviewLayer");
            double width = layer.ActualWidth, height = layer.ActualHeight;
            Check(width > 100 && height > 100, "The canvas of the 3D window must lay out without showing the window.");

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            Type type = typeof(Fractal3DWindow);
            PropertyInfo poseProperty = type.GetProperty("Pose", flags)!;
            MethodInfo zoom = type.GetMethod("ZoomCamera", flags)!;
            MethodInfo advance = type.GetMethod("AdvanceAnimation", flags)!;
            double fieldOfView = (double)type.GetField("_fieldOfView", flags)!.GetValue(window)!;
            Fractal3DPose Pose() => (Fractal3DPose)poseProperty.GetValue(window)!;

            // Зонд «нашёл» поверхность под курсором: так окно узнаёт точку, к которой приближать.
            void Probed(Point point, Vector3? hit)
            {
                type.GetField("_hitKnown", flags)!.SetValue(window, true);
                type.GetField("_cursorHit", flags)!.SetValue(window, hit);
                type.GetField("_hitPoint", flags)!.SetValue(window, point);
                type.GetField("_hitRevision", flags)!.SetValue(window, type.GetField("_viewRevision", flags)!.GetValue(window));
            }

            void Settle()
            {
                for (int frame = 0; frame < 90; frame++) advance.Invoke(window, [1.0 / 60]);
            }

            // Приближение к точке сбоку от центра: серия быстрых щелчков, между ними — несколько
            // кадров доводки. Камера всё время перед точкой, а точка — под курсором.
            var cursor = new Point(width * 0.68, height * 0.37);
            Fractal3DPose start = Pose();
            Vector3 pivot = start.Position +
                Fractal3DCamera.PixelRay(start, fieldOfView, cursor.X, cursor.Y, width, height) * 1.3f;
            Probed(cursor, pivot);

            double previous = (pivot - start.Position).Length();
            for (int notch = 0; notch < 40; notch++)
            {
                zoom.Invoke(window, [1, cursor]);
                for (int frame = 0; frame < 4; frame++)
                {
                    advance.Invoke(window, [1.0 / 60]);
                    Fractal3DPose pose = Pose();
                    double depth = Vector3.Dot(pivot - pose.Position, pose.Forward);
                    Check(depth > 0 && (pivot - pose.Position).Length() <= previous * (1 + 1e-4),
                        $"Wheel notch {notch}: the camera must close in on the point without passing it.");
                    previous = (pivot - pose.Position).Length();
                }
            }
            Settle();
            Fractal3DPose close = Pose();
            (double pivotX, double pivotY) = Fractal3DCamera.Project(close, fieldOfView, pivot, width, height);
            Check(previous < 1.3 * 0.01 && Math.Abs(pivotX - cursor.X) < 1 && Math.Abs(pivotY - cursor.Y) < 1,
                $"Forty notches must bring the camera close to the point and keep it under the cursor " +
                $"({previous:G4} left, {pivotX:F1}; {pivotY:F1} on screen).");

            // Вплотную к одной точке курсор указывает на далёкую поверхность: приближение к ней
            // должно идти тем же шагом, а не упираться в крошечное расстояние до прежней точки.
            var farCursor = new Point(width * 0.3, height * 0.6);
            Vector3 farPivot = close.Position +
                Fractal3DCamera.PixelRay(close, fieldOfView, farCursor.X, farCursor.Y, width, height) * 3f;
            Probed(farCursor, farPivot);
            zoom.Invoke(window, [1, farCursor]);
            Settle();
            double remaining = (farPivot - Pose().Position).Length();
            Check(Math.Abs(remaining / 3 - 1 / 1.15) < 0.01,
                $"A wheel notch toward a far surface must cover the usual share of the way ({remaining:G4} of 3 left).");

            // Над фоном опора — плоскость точки наблюдения; приближение тоже работает.
            var skyCursor = new Point(width * 0.5, height * 0.5);
            Probed(skyCursor, null);
            double before = Pose().Distance;
            zoom.Invoke(window, [2, skyCursor]);
            Settle();
            Check(Pose().Distance < before * 0.8, "The wheel must zoom toward the target plane over the background.");
        }
        finally
        {
            window.Close();
        }
    }
}
