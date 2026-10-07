using System.Globalization;
using System.Windows;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class Fractal3DWindow
{
    private Lichtenberg3DSettings _lichtenbergSettings = new();
    private Lichtenberg3DField? _lichtenbergField;
    private int _lichtenbergCount, _lichtenbergRequested;
    private bool _lichtenbergRunning, _lichtenbergBoundary;
    private long _lichtenbergSession;

    private Lichtenberg3DSettings CaptureLichtenberg() => Kind != Fractal3DKind.Lichtenberg3D ? new() :
        _lichtenbergSettings with { Field = _lichtenbergField, SegmentCount = _lichtenbergCount,
            TargetSegments = (int)LichtenbergTargetSlider.Value, SessionId = _lichtenbergSession };

    private void LoadLichtenberg(Lichtenberg3DSettings? source)
    {
        if (Kind != Fractal3DKind.Lichtenberg3D) return;
        var settings = source ?? new(); settings.Validate();
        _lichtenbergSession++;
        _lichtenbergSettings = settings with { Field = null };
        _lichtenbergField = settings.Field;
        _lichtenbergCount = _lichtenbergRequested = settings.SegmentCount;
        _lichtenbergRunning = _lichtenbergBoundary = false;
        LichtenbergEtaBox.Text = Format(settings.Eta);
        LichtenbergSizeBox.SelectedIndex = settings.Size switch { 32 => 0, 64 => 2, _ => 1 };
        LichtenbergElectrodeBox.SelectedIndex = (int)settings.Electrodes;
        LichtenbergSeedBox.Text = settings.Seed.ToString(CultureInfo.InvariantCulture);
        LichtenbergTargetSlider.Value = settings.TargetSegments;
        UpdateLichtenbergLabels();
    }

    private void UpdateLichtenbergLabels()
    {
        if (LichtenbergPlayButton is null) return;
        int target = (int)LichtenbergTargetSlider.Value;
        LichtenbergCountText.Text = $"{_lichtenbergCount:N0} / {target:N0} участков · " +
            (_lichtenbergBoundary ? "достигнут электрод" : _lichtenbergRunning ? "растёт" : "пауза");
        LichtenbergGrowthProgress.Maximum = target;
        LichtenbergGrowthProgress.Value = Math.Min(target, _lichtenbergCount);
        LichtenbergPlayButton.Content = _lichtenbergRunning ? "Ⅱ Пауза" : "▶ Растить";
        LichtenbergTargetText.Text = $"Цель · {target:N0} участков";
        LichtenbergSpeedText.Text = $"Участков на кадр · {LichtenbergSpeedSlider.Value:F0}";
        UpdateCancelAvailability();
    }

    private void LichtenbergApply_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = new Lichtenberg3DSettings
            {
                Eta = ReadDouble(LichtenbergEtaBox, "η", 0, 8),
                Size = new[] { 32, 48, 64 }[Math.Clamp(LichtenbergSizeBox.SelectedIndex, 0, 2)],
                Electrodes = (LichtenbergElectrodes)Math.Clamp(LichtenbergElectrodeBox.SelectedIndex, 0, 1),
                Seed = ReadInt(LichtenbergSeedBox, "Случайное число", int.MinValue, int.MaxValue),
                SegmentCount = 0, TargetSegments = (int)LichtenbergTargetSlider.Value
            };
            settings.Validate();
            _lichtenbergSettings = settings;
            RestartLichtenberg();
        }
        catch (Exception exception) { StatusText.Text = exception.Message; }
    }

    private void RestartLichtenberg()
    {
        _lichtenbergSession++; _lichtenbergRunning = _lichtenbergBoundary = false;
        _lichtenbergCount = _lichtenbergRequested = 0; _lichtenbergField = null;
        _renderCts?.Cancel(); UpdateLichtenbergLabels(); RequestFrame(FrameQuality.Draft);
    }

    private void LichtenbergRestart_OnClick(object sender, RoutedEventArgs e) => RestartLichtenberg();

    private void LichtenbergPlay_OnClick(object sender, RoutedEventArgs e)
    {
        if (_lichtenbergRunning) { PauseLichtenberg(); return; }
        if (_lichtenbergBoundary || _lichtenbergCount >= LichtenbergTargetSlider.Value)
        { StatusText.Text = "Рост завершён. Увеличьте цель или начните заново."; return; }
        _lichtenbergRunning = true;
        _lichtenbergRequested = Math.Min((int)LichtenbergTargetSlider.Value,
            _lichtenbergCount + (int)LichtenbergSpeedSlider.Value);
        UpdateLichtenbergLabels(); RequestFrame(FrameQuality.Draft);
    }

    private void LichtenbergStep_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isRendering || _lichtenbergBoundary) return;
        _lichtenbergRunning = false;
        _lichtenbergRequested = Math.Min(Lichtenberg3DSettings.MaxSegments, _lichtenbergCount + 1);
        RequestFrame(FrameQuality.Draft);
    }

    private void PauseLichtenberg()
    {
        _lichtenbergRunning = false; _lichtenbergRequested = _lichtenbergCount;
        _renderCts?.Cancel(); UpdateLichtenbergLabels(); RequestFrame(FrameQuality.Draft);
    }

    private void LichtenbergRandom_OnClick(object sender, RoutedEventArgs e)
    {
        LichtenbergSeedBox.Text = Random.Shared.Next().ToString(CultureInfo.InvariantCulture);
        long session = _lichtenbergSession;
        LichtenbergApply_OnClick(sender, e);
        if (session != _lichtenbergSession) LichtenbergPlay_OnClick(sender, e);
    }

    private void LichtenbergFit_OnClick(object sender, RoutedEventArgs e)
    {
        if (_lichtenbergField is null) return;
        int n = _lichtenbergField.Size;
        var points = _lichtenbergField.Cells.Select(cell => new System.Numerics.Vector3(
            cell % n - n / 2, cell / n % n - n / 2, cell / (n*n) - n / 2) * (220f / n / 128)).ToArray();
        var min = points.Aggregate(System.Numerics.Vector3.Min);
        var max = points.Aggregate(System.Numerics.Vector3.Max);
        var center = (min+max)*.5f;
        double radius = Math.Max(.05, points.Max(p => System.Numerics.Vector3.Distance(p,center))+.035);
        double aspect = Math.Max(.1,CanvasHost.ActualWidth/Math.Max(1,CanvasHost.ActualHeight));
        double halfFov = Math.Atan(Math.Tan(_fieldOfView*Math.PI/360)*Math.Min(1,aspect));
        BeginTransition(new(_orientation,radius*1.15/Math.Sin(halfFov),center));
    }

    private void LichtenbergDisplay_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_updatingUi || Kind != Fractal3DKind.Lichtenberg3D) return;
        if (_lichtenbergRunning && _lichtenbergRequested > LichtenbergTargetSlider.Value) PauseLichtenberg();
        UpdateLichtenbergLabels();
    }

    private bool IsCurrentLichtenbergFrame(Fractal3DState state) => Kind != Fractal3DKind.Lichtenberg3D ||
        state.Lichtenberg.SessionId == _lichtenbergSession && state.Lichtenberg.SameGrowth(_lichtenbergSettings) &&
        state.Lichtenberg.SegmentCount == _lichtenbergRequested;

    private void OnLichtenbergFrameDisplayed(Fractal3DState state)
    {
        if (Kind != Fractal3DKind.Lichtenberg3D || _isClosing || _suspended || !IsCurrentLichtenbergFrame(state)) return;
        _lichtenbergField = _renderer.LichtenbergDisplayedField;
        if (_lichtenbergField is null) return;
        _lichtenbergCount = _lichtenbergField.Count;
        _lichtenbergBoundary = _renderer.LichtenbergBoundaryReached;
        if (_lichtenbergBoundary || _lichtenbergCount >= LichtenbergTargetSlider.Value) _lichtenbergRunning = false;
        _lichtenbergRequested = _lichtenbergCount;
        UpdateLichtenbergLabels();
        if (_lichtenbergRunning)
        {
            _lichtenbergRequested = Math.Min((int)LichtenbergTargetSlider.Value,
                _lichtenbergCount + (int)LichtenbergSpeedSlider.Value);
            RequestFrame(FrameQuality.Draft, 80);
        }
    }
}
