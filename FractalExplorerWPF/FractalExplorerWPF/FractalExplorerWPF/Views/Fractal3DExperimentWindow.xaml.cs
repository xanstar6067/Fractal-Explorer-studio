using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Core.Rendering3D;

namespace FractalExplorerWPF.Views;

/// <summary>
/// Черновое окно для проверки веса и работоспособности Vortice.Windows (D3D11):
/// раз в кадр рендерит Mandelbulb на GPU и копирует результат в WriteableBitmap через CPU-readback.
/// Интерфейс намеренно не проработан — цель эксперимента только в оценке добавленной зависимости.
/// </summary>
public partial class Fractal3DExperimentWindow : Window
{
    private readonly RaymarchGpuRenderer _renderer = new();
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private WriteableBitmap? _bitmap;

    public Fractal3DExperimentWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => CompositionTarget.Rendering += OnRendering;
        Closed += (_, _) =>
        {
            CompositionTarget.Rendering -= OnRendering;
            _renderer.Dispose();
        };
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var width = Math.Max(64, (int)ActualWidth);
        var height = Math.Max(64, (int)ActualHeight);

        if (_bitmap == null || _bitmap.PixelWidth != width || _bitmap.PixelHeight != height)
        {
            _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            OutputImage.Source = _bitmap;
        }

        var pixels = _renderer.RenderFrame(width, height, (float)_stopwatch.Elapsed.TotalSeconds);

        _bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
    }
}
