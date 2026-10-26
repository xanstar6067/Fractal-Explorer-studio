using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Controls;

public partial class AbcFlowEditor : UserControl
{
    private bool _loading = true;
    private readonly Stopwatch _clock = new();
    private IReadOnlyList<AbcSectionPoint>? _sections;
    private Fractal3DState? _coloring;
    private double? _displayedPhase;
    public bool IsPlaying { get; private set; }
    public event EventHandler? SettingsChanged;
    public event EventHandler? AtlasRequested;
    private void AtlasClicked(object sender, RoutedEventArgs e) => AtlasRequested?.Invoke(this, EventArgs.Empty);
    public AbcFlowEditor()
    {
        InitializeComponent(); Load(new()); Unloaded += (_, _) => Stop();
    }
    public AbcFlowSettings Capture()
    {
        var s = new AbcFlowSettings
        {
            A = ASlider.Value, B = BSlider.Value, C = CSlider.Value,
            Group = (AbcSeedGroup)Math.Max(0, GroupBox.SelectedIndex), SeedCount = (int)CountSlider.Value,
            StartX = XSlider.Value, StartY = YSlider.Value, StartZ = ZSlider.Value, Spread = SpreadSlider.Value,
            Samples = (int)SamplesSlider.Value, TimeStep = StepSlider.Value, Thickness = ThicknessSlider.Value,
            ShowTrails = TrailsBox.IsChecked == true, ShowParticles = ParticlesBox.IsChecked == true,
            Phase = PhaseSlider.Value, PlaybackSpeed = SpeedSlider.Value,
            SectionAxis = (AbcSectionAxis)Math.Max(0, AxisBox.SelectedIndex), SectionPosition = SectionSlider.Value,
            PositiveCrossings = PositiveBox.IsChecked == true, SectionOnly = SectionOnlyBox.IsChecked == true,
            LyapunovRange = RangeSlider.Value
        };
        s.Validate(); return s;
    }
    public void Load(AbcFlowSettings s)
    {
        s.Validate(); Stop(); _displayedPhase = null; _loading = true;
        ASlider.Value = s.A; BSlider.Value = s.B; CSlider.Value = s.C;
        GroupBox.SelectedIndex = (int)s.Group; CountSlider.Value = s.SeedCount;
        XSlider.Value = s.StartX; YSlider.Value = s.StartY; ZSlider.Value = s.StartZ; SpreadSlider.Value = s.Spread;
        SamplesSlider.Value = s.Samples; StepSlider.Value = s.TimeStep; ThicknessSlider.Value = s.Thickness;
        TrailsBox.IsChecked = s.ShowTrails; ParticlesBox.IsChecked = s.ShowParticles;
        PhaseSlider.Value = s.Phase; SpeedSlider.Value = s.PlaybackSpeed;
        AxisBox.SelectedIndex = (int)s.SectionAxis; SectionSlider.Value = s.SectionPosition;
        PositiveBox.IsChecked = s.PositiveCrossings; SectionOnlyBox.IsChecked = s.SectionOnly;
        RangeSlider.Value = s.LyapunovRange;
        _sections = null; SectionPlot.SetPoints([], s, null); SectionText.Text = "Сечение появится после расчёта.";
        ErrorText.Text = ""; _loading = false; RefreshTime();
    }
    private void Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        // Never let both display options be disabled in the trajectory view.
        if (TrailsBox.IsChecked != true && ParticlesBox.IsChecked != true && SectionOnlyBox.IsChecked != true)
        { _loading = true; TrailsBox.IsChecked = true; _loading = false; }
        RefreshTime(); ErrorText.Text = ""; SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
    private void RefreshTime()
    {
        double duration = SamplesSlider.Value * AbcFlowSettings.Substeps * StepSlider.Value;
        TimeText.Text = $"t = {PhaseSlider.Value * duration:F2} / {duration:F2} · один период куба: 2π";
    }
    private void PlayClicked(object sender, RoutedEventArgs e)
    { if (IsPlaying) Stop(); else Start(); SettingsChanged?.Invoke(this, EventArgs.Empty); }
    public void Start()
    {
        _loading = true; ParticlesBox.IsChecked = true; SectionOnlyBox.IsChecked = false; _loading = false;
        IsPlaying = true; _clock.Restart(); PlayButton.Content = "Ⅱ Пауза";
    }
    public void Stop()
    { IsPlaying = false; _clock.Stop(); if (PlayButton is not null) PlayButton.Content = "▶ Движение частиц"; }
    public void StopAtDisplayedPhase()
    {
        bool playing = IsPlaying; Stop();
        if (!playing || _displayedPhase is not { } phase) return;
        bool loading = _loading; _loading = true;
        try { PhaseSlider.Value = phase; RefreshTime(); }
        finally { _loading = loading; }
    }
    public void OnFrameDisplayed(IReadOnlyList<AbcSectionPoint> points, Fractal3DState state)
    {
        _displayedPhase = state.AbcFlow.Phase;
        if (!ReferenceEquals(_sections, points) || _coloring is null || state.ColoringMode != _coloring.ColoringMode ||
            state.ColorScale != _coloring.ColorScale || state.ColorOffset != _coloring.ColorOffset ||
            state.ColorRepeat != _coloring.ColorRepeat || state.SurfaceColor != _coloring.SurfaceColor ||
            state.ResolvePalette().Gamma != _coloring.ResolvePalette().Gamma ||
            state.ResolvePalette().IsGradient != _coloring.ResolvePalette().IsGradient ||
            !state.ResolvePalette().Colors.SequenceEqual(_coloring.ResolvePalette().Colors))
        {
            _sections = points; _coloring = state;
            SectionPlot.SetPoints(points, state.AbcFlow, state);
            string axes = state.AbcFlow.SectionAxis switch { AbcSectionAxis.X => "y / z", AbcSectionAxis.Y => "x / z", _ => "x / y" };
            SectionText.Text = points.Count == 0 ? "Нет пересечений. Увеличьте время или измените плоскость." : $"{points.Count:N0} пересечений · оси {axes} · [−π, π)";
        }
        if (!IsPlaying || Visibility != Visibility.Visible) return;
        double elapsed = Math.Clamp(_clock.Elapsed.TotalSeconds, .001, .2); _clock.Restart();
        PhaseSlider.Value = (PhaseSlider.Value + elapsed * SpeedSlider.Value / state.AbcFlow.Duration) % 1;
    }
    public void ShowError(string message) { Stop(); ErrorText.Text = message; }
}
