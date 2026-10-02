using System.Windows;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class Fractal3DWindow
{
    private int _dlaCount, _dlaRequestedCount;
    private double _dlaDisplayedRadius;
    private bool _dlaRunning;

    private Dla3DSettings CaptureDla() => Kind != Fractal3DKind.Dla3D ? new() : new Dla3DSettings
    {
        Seed = ReadInt(DlaRandomSeedBox, "Случайное число", int.MinValue, int.MaxValue),
        SeedShape = (Dla3DSeedShape)Math.Clamp(DlaSeedShapeBox.SelectedIndex, 0, 3),
        SeedSize = (int)Math.Round(DlaSeedSizeSlider.Value),
        Stickiness = DlaStickSlider.Value / 100,
        FlowStrength = DlaFlowSlider.Value / 100,
        FlowYaw = DlaYawSlider.Value,
        FlowPitch = DlaPitchSlider.Value,
        ParticleCount = _dlaCount,
        TargetParticles = (int)Math.Round(DlaTargetSlider.Value)
    }.Normalized();

    private void LoadDla(Dla3DSettings? source)
    {
        Dla3DSettings s = (source ?? new()).Normalized();
        _dlaRunning = false;
        _dlaCount = _dlaRequestedCount = s.ParticleCount;
        _dlaDisplayedRadius = 0;
        DlaRandomSeedBox.Text = s.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture);
        DlaSeedShapeBox.SelectedIndex = (int)s.SeedShape;
        DlaSeedSizeSlider.Value = s.SeedSize;
        DlaStickSlider.Value = s.Stickiness * 100;
        DlaFlowSlider.Value = s.FlowStrength * 100;
        DlaYawSlider.Value = s.FlowYaw;
        DlaPitchSlider.Value = s.FlowPitch;
        DlaTargetSlider.Value = s.TargetParticles;
        UpdateDlaLabels();
    }

    private void UpdateDlaLabels()
    {
        if (DlaPitchSlider is null || DlaPlayButton is null) return;
        UpdateCancelAvailability();
        int target = (int)Math.Round(DlaTargetSlider.Value);
        DlaCountText.Text = $"{_dlaCount:N0} / {target:N0} частиц · " + (_dlaRunning ? "растёт" : _dlaCount >= target ? "готово" : "пауза");
        DlaGrowthProgress.Maximum = target;
        DlaGrowthProgress.Value = Math.Min(target, _dlaCount);
        DlaPlayButton.Content = _dlaRunning ? "Ⅱ Пауза" : _dlaCount >= target ? "▶ Наблюдать рост" : "▶ Растить";
        DlaSeedSizePanel.Visibility = Collapse(DlaSeedShapeBox.SelectedIndex != 0);
        DlaSeedSizeText.Text = $"Размер затравки · {DlaSeedSizeSlider.Value:F0}";
        DlaStickText.Text = $"Прилипание · {DlaStickSlider.Value:F0} %";
        DlaTargetText.Text = $"Цель · {target:N0} частиц";
        DlaFlowText.Text = DlaFlowSlider.Value == 0 ? "Без потока · свободная диффузия" : $"Сила потока · {DlaFlowSlider.Value:F0} %";
        DlaDirectionPanel.IsEnabled = DlaFlowSlider.Value > 0;
        DlaDirectionPanel.Opacity = DlaFlowSlider.Value > 0 ? 1 : .5;
        double pitch = DlaPitchSlider.Value;
        DlaCompass.IsEnabled = Math.Abs(pitch) < 89.5;
        DlaCompass.Opacity = DlaCompass.IsEnabled ? 1 : .4;
        DlaPitchText.Text = Math.Abs(pitch) < 1 ? "Поток горизонтально" :
            $"{(pitch < 0 ? "↓ Вниз" : "↑ Вверх")} · наклон {Math.Abs(pitch):F0}°";
        string[] speeds = ["Неспешно", "Спокойно", "Обычный", "Быстро", "Очень быстро"];
        DlaSpeedText.Text = "Темп показа · " + speeds[Math.Clamp((int)DlaSpeedSlider.Value - 1, 0, 4)];
    }

    private int DlaBatchSize => new[] { 25, 75, 150, 400, 1000 }[Math.Clamp((int)DlaSpeedSlider.Value - 1, 0, 4)];

    private void DlaPlay_OnClick(object sender, RoutedEventArgs e)
    {
        if (_dlaRunning) { StopDlaGrowth(); return; }
        try { _ = CaptureDla(); }
        catch (Exception exception) { StatusText.Text = exception.Message; return; }
        if (_dlaCount >= DlaTargetSlider.Value || _renderer.DlaBoundaryReached)
            _dlaCount = _dlaRequestedCount = 0;
        _dlaRunning = true;
        _dlaRequestedCount = Math.Min((int)DlaTargetSlider.Value, _dlaCount + DlaBatchSize);
        UpdateDlaLabels();
        RequestFrame(FrameQuality.Draft);
    }

    private void StopDlaGrowth()
    {
        _dlaRunning = false;
        _dlaRequestedCount = _dlaCount;
        _renderCts?.Cancel();
        UpdateDlaLabels();
        RequestFrame(FrameQuality.Draft);
    }

    private void DlaRestart_OnClick(object sender, RoutedEventArgs e)
    {
        _dlaRunning = false;
        _dlaCount = _dlaRequestedCount = 0;
        _renderCts?.Cancel();
        UpdateDlaLabels();
        RequestFrame(FrameQuality.Draft);
    }

    private void DlaFit_OnClick(object sender, RoutedEventArgs e)
    {
        // Use the last displayed bounds even when the renderer is busy growing a new batch.
        if (_dlaDisplayedRadius <= 0 && _isRendering) return;
        double radius = Math.Max(.03, _dlaDisplayedRadius + .02);
        double aspect = Math.Max(.1, CanvasHost.ActualWidth / Math.Max(1, CanvasHost.ActualHeight));
        double halfFov = Math.Atan(Math.Tan(_fieldOfView * Math.PI / 360) * Math.Min(1, aspect));
        BeginTransition(new(_orientation, radius * 1.15 / Math.Sin(halfFov), System.Numerics.Vector3.Zero));
    }

    private void DlaRandom_OnClick(object sender, RoutedEventArgs e)
    {
        DlaRandomSeedBox.Text = Random.Shared.Next().ToString(System.Globalization.CultureInfo.InvariantCulture);
        DlaPlay_OnClick(sender, e);
    }

    private void DlaGrowth_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_updatingUi || Kind != Fractal3DKind.Dla3D) return;
        _dlaRunning = false;
        _dlaCount = _dlaRequestedCount = 0;
        _renderCts?.Cancel();
        UpdateDlaLabels();
        ScheduleRender();
    }

    private void DlaDisplay_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_updatingUi || Kind != Fractal3DKind.Dla3D) return;
        if (_dlaRunning && _dlaCount >= DlaTargetSlider.Value) StopDlaGrowth();
        else if (_dlaRunning && _dlaRequestedCount > DlaTargetSlider.Value)
        {
            _dlaRequestedCount = (int)DlaTargetSlider.Value;
            _renderCts?.Cancel();
            RequestFrame(FrameQuality.Draft);
        }
        UpdateDlaLabels();
    }

    // Only displayed, complete frames advance the visible count. Saves/export always capture it.
    private void OnDlaFrameDisplayed(Fractal3DState state)
    {
        if (Kind != Fractal3DKind.Dla3D || _isClosing || _suspended) return;
        Dla3DSettings current;
        try { current = CaptureDla(); }
        catch (InvalidOperationException) { _dlaRunning = false; return; }
        if (!state.Dla.SameGrowth(current) || state.Dla.ParticleCount != _dlaRequestedCount) return;
        _dlaCount = _renderer.DlaParticleCount;
        _dlaDisplayedRadius = _renderer.DlaRadius;
        if (_renderer.DlaBoundaryReached)
        {
            _dlaRunning = false;
            _dlaRequestedCount = _dlaCount;
            UpdateDlaLabels();
            DlaCountText.Text = $"{_dlaCount:N0} частиц · достигнута граница объёма";
            return;
        }
        if (_dlaCount >= current.TargetParticles) _dlaRunning = false;
        UpdateDlaLabels();
        if (_dlaRunning)
        {
            _dlaRequestedCount = Math.Min(current.TargetParticles, _dlaCount + DlaBatchSize);
            RequestFrame(FrameQuality.Draft, 100);
        }
    }
}
