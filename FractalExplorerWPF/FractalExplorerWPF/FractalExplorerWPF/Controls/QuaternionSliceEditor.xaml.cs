using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FractalExplorerWPF.Models;
using static FractalExplorerWPF.Controls.PanelFields;

namespace FractalExplorerWPF.Controls;

public partial class QuaternionSliceEditor : UserControl
{
    private bool _loading = true;
    private int _direction = 1;
    private QuaternionJuliaSettings _appliedSweep = new();
    private readonly Stopwatch _clock = new();
    public bool IsPlaying { get; private set; }
    public event EventHandler? SettingsChanged;
    public event EventHandler? AtlasRequested;
    public event Action<double>? SliceRequested;

    public QuaternionSliceEditor()
    {
        InitializeComponent();
        Load(null);
        NumericInput.Real(this, XWBox, "Наклон XW", -180, 180);
        NumericInput.Real(this, YWBox, "Наклон YW", -180, 180);
        NumericInput.Real(this, ZWBox, "Наклон ZW", -180, 180);
        NumericInput.Real(this, MinimumBox, "Начало прохода w", -4, 4);
        NumericInput.Real(this, MaximumBox, "Конец прохода w", -4, 4);
        NumericInput.OrderedPair(this, MinimumBox, MaximumBox);
        NumericInput.Real(this, SpeedBox, "Скорость прохода", .01, 4);
        Unloaded += (_, _) => Stop();
    }

    public QuaternionJuliaSettings? Capture()
    {
        NumericInput.Prepare(this);
        double minimum = ReadDouble(MinimumBox, "Начало прохода w", -4, 4);
        double maximum = ReadDouble(MaximumBox, "Конец прохода w", -4, 4);
        double speed = ReadDouble(SpeedBox, "Скорость прохода", .01, 4);
        // Черновик неактивного прохода не должен блокировать неподвижный срез.
        // Пара полей сохраняет объяснение NumericInput, модель — последние применимые границы.
        if (minimum < maximum)
            _appliedSweep = _appliedSweep with { SweepMinimum = minimum, SweepMaximum = maximum, SweepSpeed = speed };
        var settings = new QuaternionJuliaSettings
        {
            XW = ReadDouble(XWBox, "Наклон XW", -180, 180), YW = ReadDouble(YWBox, "Наклон YW", -180, 180),
            ZW = ReadDouble(ZWBox, "Наклон ZW", -180, 180),
            SweepMinimum = _appliedSweep.SweepMinimum,
            SweepMaximum = _appliedSweep.SweepMaximum,
            SweepSpeed = _appliedSweep.SweepSpeed
        };
        settings.Validate();
        return settings == new QuaternionJuliaSettings() ? null : settings;
    }

    public void Load(QuaternionJuliaSettings? settings)
    {
        var value = settings ?? new(); value.Validate(); Stop();
        _appliedSweep = value;
        _loading = true;
        try
        {
            Set(XWBox, value.XW); Set(YWBox, value.YW); Set(ZWBox, value.ZW);
            Set(MinimumBox, value.SweepMinimum); Set(MaximumBox, value.SweepMaximum); Set(SpeedBox, value.SweepSpeed);
            ErrorText.Text = "";
        }
        finally { _loading = false; }
    }

    private static void Set(TextBox box, double value) => box.Text = value.ToString("R", CultureInfo.InvariantCulture);
    private void Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        Stop(); ErrorText.Text = ""; SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
    private void ResetClicked(object sender, RoutedEventArgs e)
    {
        NumericInput.CommitAll(this);
        var settings = Capture() ?? new();
        Load(settings with { XW = 0, YW = 0, ZW = 0 });
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
    private void PlayClicked(object sender, RoutedEventArgs e)
    {
        if (IsPlaying) Stop();
        else
        {
            NumericInput.CommitAll(this);
            try { Start(); }
            catch (ArgumentException ex) { ShowError(ex.Message); }
        }
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
    public void Start()
    {
        NumericInput.Prepare(this);
        if (ReadDouble(MinimumBox, "Начало прохода w", -4, 4) >= ReadDouble(MaximumBox, "Конец прохода w", -4, 4))
            throw new ArgumentException("Начало прохода должно быть меньше конца. Неподвижный срез остаётся доступен.");
        (Capture() ?? new()).Validate();
        _direction = 1; IsPlaying = true; _clock.Restart(); PlayButton.Content = "Ⅱ Пауза";
    }
    public void Stop()
    {
        IsPlaying = false; _clock.Stop();
        if (PlayButton is not null) PlayButton.Content = "▶ Проход по срезам";
    }
    public void OnFrameDisplayed(double slice)
    {
        if (!IsPlaying) return;
        var settings = Capture() ?? new();
        double next = settings.Advance(slice, Math.Clamp(_clock.Elapsed.TotalSeconds, .001, .2), ref _direction);
        _clock.Restart(); SliceRequested?.Invoke(next);
    }
    public void ShowError(string message) { Stop(); ErrorText.Text = message; }
    private void AtlasClicked(object sender, RoutedEventArgs e) { Stop(); AtlasRequested?.Invoke(this, EventArgs.Empty); }
}
