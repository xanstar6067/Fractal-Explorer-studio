using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FractalExplorerWPF.Models;
using static FractalExplorerWPF.Controls.PanelFields;

namespace FractalExplorerWPF.Controls;

public partial class QuantumOrbitalEditor : UserControl
{
    private bool _loading = true;
    private QuantumOrbitalSettings _applied = new();
    private bool Mixing => SuperpositionBox.IsChecked == true;
    public event EventHandler? SettingsChanged;
    public QuantumOrbitalEditor()
    {
        InitializeComponent(); Load(new());
        RegisterState(NBox, LBox, MBox, () => true);
        RegisterState(N2Box, L2Box, M2Box, () => Mixing);
        NumericInput.Real(this, MixBox, "Доля второго состояния", 0, 1, active: () => Mixing);
        NumericInput.Real(this, PhaseBox, "Относительная фаза", -180, 180, active: () => Mixing);
        NumericInput.Real(this, ExtentBox, "Полуразмер области", 2, 256);
        NumericInput.Real(this, LevelBox, "Уровень плотности", .001, .8);
        NumericInput.Real(this, CutPositionBox, "Положение среза", -1, 1, active: () => CutBox.SelectedIndex > 0);
    }
    private void RegisterState(TextBox n, TextBox l, TextBox m, Func<bool> active)
    {
        NumericInput.Integer(this, n, "Главное число n", 1, 8, active: active);
        NumericInput.Integer(this, l, "Орбитальное число l", () => 0, () => Value(n, 1, 8) - 1, active: active);
        NumericInput.Integer(this, m, "Магнитное число m", () => -Value(l, 0, Value(n, 1, 8) - 1), () => Value(l, 0, Value(n, 1, 8) - 1), active: active);
    }
    private static int Value(TextBox box, int min, int max) => int.TryParse(box.Text, out int v) ? Math.Clamp(v, min, max) : min;
    private static void Text(TextBox box, double value) => box.SetCurrentValue(TextBox.TextProperty, value.ToString("R", CultureInfo.InvariantCulture));
    public QuantumOrbitalSettings Capture()
    {
        bool loading = _loading; _loading = true;
        try
        {
            NumericInput.Prepare(this);
            var s = _applied with
            {
                N = ReadInt(NBox, "n", 1, 8),
                L = ReadInt(LBox, "l", 0, Value(NBox, 1, 8) - 1),
                M = ReadInt(MBox, "m", -Value(LBox, 0, 7), Value(LBox, 0, 7)),
                Basis = (QuantumOrbitalBasis)Math.Max(0, BasisBox.SelectedIndex),
                Superposition = Mixing,
                Extent = ReadDouble(ExtentBox, "Область", 2, 256),
                Size = new[] { 64, 96, 128, 192 }[Math.Max(0, SizeBox.SelectedIndex)],
                Level = ReadDouble(LevelBox, "Уровень", .001, .8),
                CutAxis = Math.Max(0, CutBox.SelectedIndex),
                CutPosition = CutBox.SelectedIndex > 0 ? ReadDouble(CutPositionBox, "Срез", -1, 1) : _applied.CutPosition
            };
            if (Mixing)
            {
                s.N2 = ReadInt(N2Box, "n₂", 1, 8); s.L2 = ReadInt(L2Box, "l₂", 0, s.N2 - 1);
                s.M2 = ReadInt(M2Box, "m₂", -s.L2, s.L2);
                s.Mix = ReadDouble(MixBox, "Доля", 0, 1); s.Phase = ReadDouble(PhaseBox, "Фаза", -180, 180);
            }
            s.Validate(); _applied = s; return s;
        }
        finally { _loading = loading; }
    }
    public void Load(QuantumOrbitalSettings? settings)
    {
        var s = settings is null ? new() : settings with { }; s.Validate();
        _loading = true;
        try
        {
            _applied = s; BasisBox.SelectedIndex = (int)s.Basis; SuperpositionBox.IsChecked = s.Superposition;
            SizeBox.SelectedIndex = Array.IndexOf(new[] { 64, 96, 128, 192 }, s.Size); CutBox.SelectedIndex = s.CutAxis;
            Text(NBox, s.N); Text(LBox, s.L); Text(MBox, s.M);
            Text(N2Box, s.N2); Text(L2Box, s.L2); Text(M2Box, s.M2); Text(ExtentBox, s.Extent);
            MixSlider.Value = s.Mix; Text(MixBox, s.Mix); PhaseSlider.Value = s.Phase; Text(PhaseBox, s.Phase);
            LevelSlider.Value = s.Level; Text(LevelBox, s.Level); CutPositionSlider.Value = s.CutPosition; Text(CutPositionBox, s.CutPosition);
            UpdateControls();
        }
        finally { _loading = false; }
    }
    private void UpdateControls()
    {
        SecondPanel.IsEnabled = Mixing;
        CutPositionSlider.IsEnabled = CutPositionBox.IsEnabled = CutBox.SelectedIndex > 0;
        int n = Value(NBox, 1, 8), l = Value(LBox, 0, n - 1);
        SummaryText.Text = $"Радиальных узлов: {n - l - 1}. " +
            (BasisBox.SelectedIndex == 0 ? $"Угловых узлов вещественной орбитали: {l}. " : "Азимутальная фаза комплексной орбитали: mφ. ") +
            "Единица длины a₀ — боровский радиус. Ось квантования — Z.";
    }
    private void OnChanged(object sender, EventArgs e)
    {
        if (_loading || SummaryText is null) return;
        UpdateControls(); SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
    private void FitClicked(object sender, RoutedEventArgs e)
    {
        NumericInput.CommitAll(this); var s = Capture();
        int n = s.Superposition ? Math.Max(s.N, s.N2) : s.N;
        Text(ExtentBox, Math.Clamp(3 * n * n, 2, 256));
    }
}
