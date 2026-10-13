using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

/// <summary>
/// Режимы с живым моделированием на ГП: панель и цикл моделирования живут в редакторе режима
/// (<see cref="IFractal3DSimulationMode"/>), окну остаются общие строка времени, кнопки и экран
/// подготовки поверх холста.
/// </summary>
public partial class Fractal3DWindow : IFractal3DSimulationHost
{
    private IFractal3DSimulationMode? CreateSimulation() => Kind switch
    {
        Fractal3DKind.GrayScott3D => new GrayScott3DEditor(this).Simulation,
        Fractal3DKind.Turing3D => new Turing3DEditor(this).Simulation,
        Fractal3DKind.CahnHilliard3D => new CahnHilliard3DEditor(this).Simulation,
        Fractal3DKind.Lenia3D => new Lenia3DEditor(this).Simulation,
        Fractal3DKind.Physarum3D => new Physarum3DEditor(this).Simulation,
        Fractal3DKind.Kobayashi3D => new Kobayashi3DEditor(this).Simulation,
        Fractal3DKind.Excitable3D => new Excitable3DEditor(this).Simulation,
        Fractal3DKind.GinzburgLandau3D => new GinzburgLandau3DEditor(this).Simulation,
        Fractal3DKind.SwiftHohenberg3D => new SwiftHohenberg3DEditor(this).Simulation,
        _ => null
    };

    private void AttachSimulation()
    {
        if (_simulation is null) return;
        SimulationEditorHost.Content = _simulation.Editor;
        SimulationTransport.Visibility = Visibility.Visible;
    }

    private void SimulationPlay_OnClick(object sender, RoutedEventArgs e) => _simulation?.TogglePlay();

    private void SimulationStep_OnClick(object sender, RoutedEventArgs e) => _simulation?.StepOnce();

    private void SimulationStopPreparation_OnClick(object sender, RoutedEventArgs e) => _simulation?.StopPreparation();

    Direct3DDeviceHost IFractal3DSimulationHost.DeviceHost => _renderer.DeviceHost;
    bool IFractal3DSimulationHost.IsClosing => _isClosing;
    bool IFractal3DSimulationHost.IsSuspended => _suspended;
    PreparationOverlay IFractal3DSimulationHost.SimulationPreparation => SimulationPreparationOverlay;
    Fractal3DPose IFractal3DSimulationHost.Pose => Pose;

    void IFractal3DSimulationHost.BeginSimulationPreparation(string message) =>
        BeginSimulationPreparation(SimulationPreparationOverlay, message);

    void IFractal3DSimulationHost.ShowTransport(string time, string playText, bool playEnabled, bool stepEnabled)
    {
        SimulationTimeText.Text = time;
        SimulationPlayButton.Content = playText;
        SimulationPlayButton.IsEnabled = playEnabled;
        SimulationStepButton.IsEnabled = stepEnabled;
    }

    void IFractal3DSimulationHost.RequestDraftFrame() => RequestFrame(FrameQuality.Draft);
    void IFractal3DSimulationHost.ScheduleRender() => ScheduleRender();

    void IFractal3DSimulationHost.RestartRender()
    {
        _renderCts?.Cancel();
        ScheduleRender(immediate: true);
    }

    void IFractal3DSimulationHost.ShowStatus(string text) => StatusText.Text = text;
    void IFractal3DSimulationHost.ShowPreparationError(string message) => ShowPreparationError(message);
    void IFractal3DSimulationHost.UpdateCancelAvailability() => UpdateCancelAvailability();
    void IFractal3DSimulationHost.SetControlsEnabled(bool enabled) => ControlsHost.IsEnabled = enabled;

    void IFractal3DSimulationHost.SetColoringModeName(Fractal3DColoringMode mode, string name) =>
        ((ComboBoxItem)ColoringModeBox.Items[(int)mode]).Content = name;

    void IFractal3DSimulationHost.MoveCameraTo(Fractal3DPose pose)
    {
        StopCameraMotion();
        MoveCamera(pose);
        UpdateCameraText();
    }

    void IFractal3DSimulationHost.FitCameraTo(Vector3 center, double radius)
    {
        double aspect = Math.Max(.1, CanvasHost.ActualWidth / Math.Max(1, CanvasHost.ActualHeight));
        double halfFov = Math.Atan(Math.Tan(_fieldOfView * Math.PI / 360) * Math.Min(1, aspect));
        StopCameraMotion();
        MoveCamera(new(_orientation, radius * 1.1 / Math.Sin(halfFov), center));
        UpdateCameraText();
    }

    bool IFractal3DSimulationHost.TryPickCutPlane(MouseButtonEventArgs e, int axis, double position, out Vector3 point)
    {
        point = default;
        var cursor = e.GetPosition(SavePreviewLayer);
        double h = Math.Max(1, SavePreviewLayer.ActualHeight), w = Math.Max(1, SavePreviewLayer.ActualWidth);
        var pose = Pose;
        var direction = Vector3.Normalize(pose.Forward * (float)(1 / Math.Tan(_fieldOfView * Math.PI / 360)) +
            pose.Right * (float)((cursor.X - w / 2) / (h / 2)) + pose.Up * (float)((h / 2 - cursor.Y) / (h / 2)));
        float component = axis == 0 ? direction.X : axis == 1 ? direction.Y : direction.Z;
        float origin = axis == 0 ? pose.Position.X : axis == 1 ? pose.Position.Y : pose.Position.Z;
        if (Math.Abs(component) < 1e-6) return false;
        double distance = (position - origin) / component;
        var hit = pose.Position + direction * (float)distance;
        if (distance <= 0 || Math.Abs(hit.X) > 1 || Math.Abs(hit.Y) > 1 || Math.Abs(hit.Z) > 1) return false;
        point = new((hit.X + 1) / 2, (hit.Y + 1) / 2, (hit.Z + 1) / 2);
        return true;
    }
}
