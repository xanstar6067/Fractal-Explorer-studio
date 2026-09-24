using System.Globalization;
using System.Numerics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class Fractal3DWindow
{
    private Fractal3DRenderer? _juliabulbMapRenderer;
    private DispatcherTimer? _juliabulbMapTimer;
    private CancellationTokenSource? _juliabulbMapCts;
    private int _juliabulbMapVersion;

    private void ScheduleJuliabulbMapRender()
    {
        if (Kind != Fractal3DKind.Juliabulb || !IsLoaded || _isClosing) return;
        _juliabulbMapTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        _juliabulbMapTimer.Tick -= JuliabulbMapTimer_OnTick;
        _juliabulbMapTimer.Tick += JuliabulbMapTimer_OnTick;
        _juliabulbMapTimer.Stop();
        _juliabulbMapTimer.Start();
        _juliabulbMapCts?.Cancel();
    }

    private async void JuliabulbMapTimer_OnTick(object? sender, EventArgs e)
    {
        _juliabulbMapTimer?.Stop();
        if (_isClosing || Kind != Fractal3DKind.Juliabulb ||
            !TryReadDouble(PowerBox.Text, out double power) || !double.IsFinite(power) || power < -32 || power > 32 ||
            !int.TryParse(IterationsBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int iterations) ||
            iterations < 1 || iterations > 64 ||
            !TryReadDouble(BailoutBox.Text, out double bailout) || !double.IsFinite(bailout) || bailout <= 1)
            return;

        // До первого кадра Image с пустым Source имеет ActualWidth/ActualHeight == 0.
        // Размер берём у уже разложенного контейнера, иначе первый рендер никогда не стартует.
        int width = Math.Max(1, (int)Math.Round(JuliabulbMapHost.ActualWidth - 2));
        int height = Math.Max(1, (int)Math.Round(JuliabulbMapHost.ActualHeight - 2));
        if (width < 2 || height < 2) return;
        Fractal3DState state = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Mandelbulb);
        state.Power = power;
        state.Iterations = iterations;
        state.Bailout = bailout;
        state.CameraDistance = 4.5;
        state.CameraYaw = 35;
        state.CameraPitch = 18;
        state.TargetX = state.TargetY = state.TargetZ = 0;
        state.SoftShadows = false;
        state.AmbientOcclusion = false;
        state.Ssaa = 1;
        if (TryReadDouble(JuliaCXBox.Text, out double markerX) &&
            TryReadDouble(JuliaCYBox.Text, out double markerY) &&
            TryReadDouble(JuliaCZBox.Text, out double markerZ) &&
            double.IsFinite(markerX) && double.IsFinite(markerY) && double.IsFinite(markerZ))
            state.PickerMarker = new Vector4((float)markerX, (float)markerY, (float)markerZ, 0.14f);

        int version = ++_juliabulbMapVersion;
        var cts = new CancellationTokenSource();
        _juliabulbMapCts = cts;
        _juliabulbMapRenderer ??= new Fractal3DRenderer();
        try
        {
            var bitmap = await _juliabulbMapRenderer.RenderAsync(state, width, height, null, cts.Token);
            if (!cts.IsCancellationRequested && version == _juliabulbMapVersion && !_isClosing)
            {
                JuliabulbMapImage.Source = bitmap;
                UpdateJuliabulbMapMarker();
                JuliabulbMapMarkerLayer.Opacity = 0.35;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!_isClosing && version == _juliabulbMapVersion)
                JuliabulbMapHost.ToolTip = $"3D-карта недоступна: {ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(_juliabulbMapCts, cts)) _juliabulbMapCts = null;
            cts.Dispose();
        }
    }

    private void UpdateJuliabulbMapMarker()
    {
        if (Kind != Fractal3DKind.Juliabulb || !IsLoaded) return;
        if (!TryReadDouble(JuliaCXBox.Text, out double x) ||
            !TryReadDouble(JuliaCYBox.Text, out double y) ||
            !TryReadDouble(JuliaCZBox.Text, out double z) ||
            !double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z))
        {
            JuliabulbMapMarkerLayer.Children.Clear();
            return;
        }
        var state = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Mandelbulb);
        state.CameraDistance = 4.5;
        state.CameraYaw = 35;
        state.CameraPitch = 18;
        Fractal3DConstantMarker.Draw(JuliabulbMapMarkerLayer, state, new Vector3((float)x, (float)y, (float)z));
        JuliabulbMapMarkerLayer.Opacity = 1;
    }

    private void JuliabulbMapHost_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateJuliabulbMapMarker();
        ScheduleJuliabulbMapRender();
    }

    private void JuliabulbMapHost_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        JuliabulbPicker_OnClick(sender, e);
        e.Handled = true;
    }

    private void JuliabulbPicker_OnClick(object sender, RoutedEventArgs e)
    {
        if (Kind != Fractal3DKind.Juliabulb) return;
        Fractal3DState state;
        try { state = CaptureState("Выбор C"); }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Параметры", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SuspendLive(() =>
        {
            var picker = new Fractal3DConstantPickerWindow(state) { Owner = this };
            if (picker.ShowDialog() != true) return;
            _updatingUi = true;
            JuliaCXBox.Text = Format(picker.SelectedConstant.X);
            JuliaCYBox.Text = Format(picker.SelectedConstant.Y);
            JuliaCZBox.Text = Format(picker.SelectedConstant.Z);
            _updatingUi = false;
            UpdateJuliabulbMapMarker();
            ScheduleJuliabulbMapRender();
        });
    }

    private void DisposeJuliabulbMap()
    {
        _juliabulbMapTimer?.Stop();
        _juliabulbMapCts?.Cancel();
        if (_juliabulbMapRenderer is { } renderer) Task.Run(renderer.Dispose);
    }
}
