using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Controls;

public partial class LSystem3DEditor : UserControl
{
    private bool _loading = true;
    private LSystem3DSettings _grammar = new();
    private readonly Stack<LSystem3DSettings> _undo = new();
    private TextBox? _grammarTarget;
    private CancellationTokenSource? _validation;
    private int _revision;
    private readonly Stopwatch _playClock = new();
    private double _lastFrameTime;
    private bool _advancingGrowth;
    private readonly Stack<LSystem3DSettings> _randomUndo = new();
    public bool IsPlaying { get; private set; }
    public event EventHandler? SettingsChanged;

    public LSystem3DEditor()
    {
        InitializeComponent();
        Randomizer.IsSpatial = true;
        Randomizer.CaptureSpatial = Capture;
        Randomizer.Generated += (_, result) =>
        {
            _randomUndo.Push(Capture());
            Load(result.Spatial!, keepRandomHistory: true);
            PresetHint.Text = result.Description;
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        };
        Randomizer.UndoRequested += (_, _) =>
        {
            if (!_randomUndo.TryPop(out var previous)) return;
            Load(previous, keepRandomHistory: true);
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        };
        Load(new());
        Unloaded += (_, _) => { Stop(); _validation?.Cancel(); };
    }
    public LSystem3DSettings Capture() => _grammar with
    {
        Generations = (int)GenerationSlider.Value,
        Yaw = YawSlider.Value, Pitch = PitchSlider.Value, Roll = RollSlider.Value,
        Radius = RadiusSlider.Value / 200, BranchTaper = TaperSlider.Value / 100,
        StepDecay = StepSlider.Value / 100, Growth = GrowthSlider.Value / 100,
        ColorSource = (LSystem3DColorSource)Math.Max(0, SourceBox.SelectedIndex)
    };
    public void Load(LSystem3DSettings settings, bool keepRandomHistory = false)
    {
        Randomizer.CancelWork();
        if (!keepRandomHistory) _randomUndo.Clear();
        Randomizer.SetUndoAvailable(_randomUndo.Count > 0);
        Stop(); _validation?.Cancel(); _revision++;
        _loading = true;
        _grammar = settings with { };
        AxiomBox.Text = settings.Axiom; RulesBox.Text = settings.RulesText; DrawBox.Text = settings.DrawSymbols;
        GenerationSlider.Value = settings.Generations;
        YawSlider.Value = settings.Yaw; PitchSlider.Value = settings.Pitch; RollSlider.Value = settings.Roll;
        RadiusSlider.Value = settings.Radius * 200; TaperSlider.Value = settings.BranchTaper * 100;
        StepSlider.Value = settings.StepDecay * 100; GrowthSlider.Value = settings.Growth * 100;
        SourceBox.SelectedIndex = (int)settings.ColorSource;
        _undo.Clear(); UndoButton.IsEnabled = false;
        RuleStatus.Text = "Правила применены. Изменения формы видны сразу.";
        RuleStatus.SetResourceReference(TextBlock.ForegroundProperty, "Theme.SecondaryTextBrush");
        ApplyButton.IsEnabled = true;
        PresetHint.Text = LSystem3DPresets.All.FirstOrDefault(p => p.Settings.GeometryKey() == settings.GeometryKey())?.Description ?? "";
        _loading = false;
        UpdateGrowth();
    }
    public void ShowError(string message)
    {
        Stop(); RuleStatus.Text = message;
        RuleStatus.SetResourceReference(TextBlock.ForegroundProperty, "Theme.DangerBrush");
        SceneText.Text = "Измените параметры или выберите готовый вид.";
    }

    public void OnFrameDisplayed(int segments, int symbols)
    {
        SceneText.Text = $"{segments:N0} сегментов · {symbols:N0} символов";
        if (!IsPlaying) return;
        double now = _playClock.Elapsed.TotalSeconds;
        double duration = 30 * Math.Pow(.562341, SpeedSlider.Value - 1);
        double next = Math.Min(100, GrowthSlider.Value + (now - _lastFrameTime) / duration * 100);
        _lastFrameTime = now;
        if (next >= 100) Stop();
        _advancingGrowth = true;
        try { GrowthSlider.Value = next; }
        finally { _advancingGrowth = false; }
    }

    private async void Apply_OnClick(object sender, RoutedEventArgs e)
    {
        _validation?.Cancel();
        using var cts = new CancellationTokenSource();
        _validation = cts;
        int revision = _revision;
        var candidate = Capture() with { Axiom = AxiomBox.Text, RulesText = RulesBox.Text, DrawSymbols = DrawBox.Text };
        ApplyButton.IsEnabled = false; RuleStatus.Text = "Проверяем правила и размер формы…";
        RuleStatus.SetResourceReference(TextBlock.ForegroundProperty, "Theme.SecondaryTextBrush");
        try
        {
            var geometry = await Task.Run(() => LSystem3DGeometry.Build(candidate, cts.Token), cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            if (revision != _revision) return;
            _undo.Push(_grammar);
            _grammar = candidate;
            UndoButton.IsEnabled = true;
            RuleStatus.Text = $"Применено · {geometry.Segments.Length:N0} сегментов.";
            PresetHint.Text = "";
            Stop();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ShowError(ex.Message); }
        finally
        {
            if (ReferenceEquals(_validation, cts)) { _validation = null; ApplyButton.IsEnabled = true; }
        }
    }
    private void Undo_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_undo.TryPop(out var previous)) return;
        _revision++; _validation?.Cancel();
        _loading = true;
        _grammar = previous; AxiomBox.Text = previous.Axiom; RulesBox.Text = previous.RulesText; DrawBox.Text = previous.DrawSymbols;
        _loading = false;
        UndoButton.IsEnabled = _undo.Count > 0;
        RuleStatus.Text = "Предыдущие правила восстановлены.";
        RuleStatus.SetResourceReference(TextBlock.ForegroundProperty, "Theme.SecondaryTextBrush");
        Stop(); SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
    private void Grammar_OnFocus(object sender, KeyboardFocusChangedEventArgs e) => _grammarTarget = (TextBox)sender;
    private void Grammar_OnChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        Randomizer.CancelWork();
        _revision++; _validation?.Cancel();
        RuleStatus.Text = "Есть изменения правил · нажмите «Применить».";
        RuleStatus.SetResourceReference(TextBlock.ForegroundProperty, "Theme.SecondaryTextBrush");
    }
    private void Insert_OnClick(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        string value = (button.Tag ?? button.Content).ToString() ?? "";
        TextBox target = _grammarTarget ?? RulesBox;
        int caret = target.SelectionStart;
        target.SelectedText = value;
        target.CaretIndex = caret + (value == "[]" ? 1 : value.Length);
        target.Focus();
    }
    private void Shape_OnChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        Randomizer.CancelWork();
        Stop(); _revision++; _validation?.Cancel();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
    private void Display_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (ReferenceEquals(sender, GrowthSlider) && IsPlaying && !_advancingGrowth) Stop();
        UpdateGrowth();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
    private void Play_OnClick(object sender, RoutedEventArgs e)
    {
        if (IsPlaying) { Stop(); SettingsChanged?.Invoke(this, EventArgs.Empty); return; }
        if (GrowthSlider.Value >= 100) GrowthSlider.Value = 0;
        IsPlaying = true; PlayButton.Content = "Ⅱ Пауза";
        _playClock.Restart(); _lastFrameTime = 0;
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
    private void Restart_OnClick(object sender, RoutedEventArgs e)
    { Stop(); GrowthSlider.Value = 0; }
    private void Stop() { IsPlaying = false; _playClock.Stop(); if (PlayButton is not null) PlayButton.Content = "▶ Построить"; }
    public void CancelWork() { Stop(); _validation?.Cancel(); Randomizer.CancelWork(); }
    private void UpdateGrowth() => GrowthText.Text = $"Построено · {GrowthSlider.Value:0.#} %";
}
