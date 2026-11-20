using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FractalExplorerWPF.Core.WaveMath;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Controls;

public partial class StandingWaveEditor : UserControl
{
    private sealed record ModeRow(Grid Panel, TextBox X, TextBox Y, TextBox Z,
        TextBox Amplitude, TextBox Phase, TextBlock Frequency, Button Remove);

    private readonly List<ModeRow> _rows = [];
    private StandingWaveSettings _applied = new();
    private bool _loading = true;
    private StandingWaveView View => (StandingWaveView)Math.Max(0, ViewBox.SelectedIndex);
    private bool HasCut => CutBox.IsChecked == true || View == StandingWaveView.Slice;

    public event EventHandler? SettingsChanged;
    public event EventHandler? PlaybackRequested;

    public StandingWaveEditor()
    {
        InitializeComponent();
        for (int i = 0; i < StandingWaveSettings.MaxModes; i++) _rows.Add(CreateRow(i));
        Load(new());
        foreach (var row in _rows)
        {
            NumericInput.Integer(this, row.X, "Число полуволн X", 1, StandingWaveMode.MaxOrder);
            NumericInput.Integer(this, row.Y, "Число полуволн Y", 1, StandingWaveMode.MaxOrder);
            NumericInput.Integer(this, row.Z, "Число полуволн Z", 1, StandingWaveMode.MaxOrder);
            NumericInput.Real(this, row.Amplitude, "Амплитуда моды", 0, 2);
            NumericInput.Real(this, row.Phase, "Фаза моды", -180, 180);
        }
        NumericInput.Real(this, LengthXBox, "Длина X", 1, 4);
        NumericInput.Real(this, LengthYBox, "Длина Y", 1, 4);
        NumericInput.Real(this, LengthZBox, "Длина Z", 1, 4);
        NumericInput.Real(this, TimeBox, "Время", 0, StandingWaveSettings.MaxTime);
        NumericInput.Real(this, SpeedBox, "Скорость воспроизведения", .01, 4);
        NumericInput.Real(this, LevelBox, "Порог амплитуды", .02, 1);
        NumericInput.Real(this, NodeWidthBox, "Толщина слоя около нуля", .02, .15);
        NumericInput.Real(this, PositionBox, "Положение сечения", .01, .99);
    }

    private ModeRow CreateRow(int index)
    {
        var panel = new Grid { Margin = new Thickness(0, 0, 0, 7) };
        for (int i = 0; i < 5; i++) panel.ColumnDefinitions.Add(new());
        panel.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        panel.RowDefinitions.Add(new());
        panel.RowDefinitions.Add(new());

        int number = index + 1;
        TextBox Field(int column, string label, string description)
        {
            var box = new TextBox { Style = (Style)Resources["WaveNumber"], Margin = new Thickness(0, 0, 4, 0) };
            AutomationProperties.SetName(box, $"Мода {number}: {description}");
            var group = new StackPanel();
            group.Children.Add(new TextBlock { Text = label, ToolTip = description });
            group.Children.Add(box);
            Grid.SetColumn(group, column);
            Grid.SetRow(group, 1);
            panel.Children.Add(group);
            return box;
        }

        var frequency = new TextBlock { Margin = new Thickness(0, 0, 0, 2) };
        Grid.SetColumnSpan(frequency, 6);
        panel.Children.Add(frequency);

        var remove = new Button { Content = "×", ToolTip = "Удалить моду", VerticalAlignment = VerticalAlignment.Bottom };
        AutomationProperties.SetName(remove, $"Удалить моду {number}");
        remove.Click += (_, _) => RemoveMode(index);
        Grid.SetColumn(remove, 5);
        Grid.SetRow(remove, 1);
        panel.Children.Add(remove);
        ModeRows.Children.Add(panel);
        return new(panel, Field(0, "nₓ", "Полуволны X"), Field(1, "nᵧ", "Полуволны Y"), Field(2, "n𝓏", "Полуволны Z"),
            Field(3, "Амп.", "Амплитуда"), Field(4, "Фаза °", "Фаза в градусах"), frequency, remove);
    }

    public void Load(StandingWaveSettings settings)
    {
        _loading = true;
        try
        {
            _applied = settings.Validated();
            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                var mode = i < _applied.Modes.Length ? _applied.Modes[i] : new StandingWaveMode(1, 2, 3);
                Write(row.X, mode.X); Write(row.Y, mode.Y); Write(row.Z, mode.Z);
                Write(row.Amplitude, mode.Amplitude); Write(row.Phase, mode.Phase);
            }
            Write(LengthXBox, _applied.LengthX); Write(LengthYBox, _applied.LengthY); Write(LengthZBox, _applied.LengthZ);
            Write(TimeBox, _applied.Time); Write(SpeedBox, _applied.PlaybackSpeed);
            Write(LevelBox, _applied.Level); Write(NodeWidthBox, _applied.NodeWidth); Write(PositionBox, _applied.CutPosition);
            LevelSlider.Value = _applied.Level; CutSlider.Value = _applied.CutPosition;
            ViewBox.SelectedIndex = (int)_applied.View;
            CutBox.IsChecked = _applied.Cutaway;
            AxisBox.SelectedIndex = (int)_applied.Axis;
            ResolutionBox.SelectedIndex = StandingWaveSettings.Resolutions.ToList().IndexOf(_applied.Resolution);
            UpdatePanels();
        }
        finally { _loading = false; }
    }

    public StandingWaveSettings Capture()
    {
        _loading = true;
        try
        {
            NumericInput.Prepare(this);
            var modes = new StandingWaveMode[_applied.Modes.Length];
            for (int i = 0; i < modes.Length; i++)
            {
                var row = _rows[i];
                var last = _applied.Modes[i];
                modes[i] = new((int)Read(row.X, last.X), (int)Read(row.Y, last.Y), (int)Read(row.Z, last.Z),
                    Read(row.Amplitude, last.Amplitude), Read(row.Phase, last.Phase));
            }
            _applied = _applied with
            {
                Modes = modes,
                LengthX = Read(LengthXBox, _applied.LengthX),
                LengthY = Read(LengthYBox, _applied.LengthY),
                LengthZ = Read(LengthZBox, _applied.LengthZ),
                Time = Read(TimeBox, _applied.Time),
                PlaybackSpeed = Read(SpeedBox, _applied.PlaybackSpeed),
                View = View,
                Level = Read(LevelBox, _applied.Level),
                NodeWidth = Read(NodeWidthBox, _applied.NodeWidth),
                Cutaway = CutBox.IsChecked == true,
                Axis = (StandingWaveAxis)Math.Max(0, AxisBox.SelectedIndex),
                CutPosition = Read(PositionBox, _applied.CutPosition),
                Resolution = StandingWaveSettings.Resolutions[Math.Max(0, ResolutionBox.SelectedIndex)]
            };
            LevelSlider.Value = _applied.Level;
            CutSlider.Value = _applied.CutPosition;
            UpdateFrequencies();
            return _applied;
        }
        finally { _loading = false; }
    }

    internal void SetPlaying(bool playing)
    {
        PlayButton.Content = playing ? "Ⅱ Пауза" : "▶ Пуск";
        TimeBox.IsReadOnly = playing;
    }

    internal void ShowTime(double time)
    {
        _loading = true;
        _applied = _applied with { Time = time };
        Write(TimeBox, time);
        _loading = false;
    }

    internal void ShowError(string message) => ErrorText.Text = message;

    private void UpdatePanels()
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            _rows[i].Panel.Visibility = Visible(i < _applied.Modes.Length);
            _rows[i].Remove.IsEnabled = _applied.Modes.Length > 1;
        }
        AddButton.IsEnabled = _applied.Modes.Length < StandingWaveSettings.MaxModes;
        LevelPanel.Visibility = Visible(View is StandingWaveView.Surface or StandingWaveView.Cloud);
        NodePanel.Visibility = Visible(View == StandingWaveView.Nodes);
        CutBox.Visibility = Visible(View != StandingWaveView.Slice);
        CutPanel.Visibility = Visible(HasCut);
        UpdateFrequencies();
    }

    private void UpdateFrequencies()
    {
        for (int i = 0; i < _applied.Modes.Length; i++)
        {
            var mode = _applied.Modes[i];
            _rows[i].Frequency.Text = $"Мода {i + 1} · f = {StandingWave.Frequency(_applied, mode):0.###}";
        }
    }

    private void OnChanged(object sender, EventArgs e)
    {
        if (_loading) return;
        UpdatePanels();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        Write(ReferenceEquals(sender, LevelSlider) ? LevelBox : PositionBox, Math.Round(e.NewValue, 3));
    }

    private void AddMode(object sender, RoutedEventArgs e)
    {
        NumericInput.CommitAll(this);
        var settings = Capture();
        if (settings.Modes.Length >= StandingWaveSettings.MaxModes) return;
        Load(settings with { Modes = [.. settings.Modes, new(1, 2, settings.Modes.Length + 1, .5)] });
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RemoveMode(int index)
    {
        NumericInput.CommitAll(this);
        var settings = Capture();
        if (settings.Modes.Length <= 1) return;
        Load(settings with { Modes = settings.Modes.Where((_, i) => i != index).ToArray() });
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void TogglePlayback(object sender, RoutedEventArgs e)
    {
        NumericInput.CommitAll(this);
        PlaybackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ResetTime(object sender, RoutedEventArgs e)
    {
        ShowTime(0);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private static Visibility Visible(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
    private static void Write(TextBox box, double value) => box.Text = value.ToString("R", CultureInfo.InvariantCulture);
    private static double Read(TextBox box, double fallback) =>
        NumericInput.IsValid(box) && PanelFields.TryReadDouble(box.Text, out double value) ? value : fallback;
}
