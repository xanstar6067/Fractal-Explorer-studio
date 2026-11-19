using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FractalExplorerWPF.Core.PercolationMath;
using FractalExplorerWPF.Models;
using static FractalExplorerWPF.Controls.PanelFields;

namespace FractalExplorerWPF.Controls;

public partial class Percolation3DEditor : UserControl
{
    private bool _loading = true;
    private Percolation3DSettings _applied = new();
    public event EventHandler? SettingsChanged;

    public Percolation3DEditor()
    {
        InitializeComponent();
        Load(new());
        NumericInput.Integer(this, SizeBox, "Сторона решётки", Percolation3DSettings.MinSize, Percolation3DSettings.MaxSize);
        NumericInput.Integer(this, SeedBox, "Начальное число", int.MinValue, int.MaxValue);
        NumericInput.Real(this, ProbabilityBox, "Порог заполнения", 0, 1, appliedValue: () => _applied.Probability);
        NumericInput.Real(this, CutPositionBox, "Положение среза", 0, 1,
            active: () => CutBox.SelectedIndex > 0, appliedValue: () => _applied.CutPosition);
    }

    public Percolation3DSettings Capture()
    {
        bool loading = _loading;
        _loading = true;
        try
        {
            NumericInput.Prepare(this);
            _applied = new()
            {
                Size = ReadInt(SizeBox, "Сторона решётки", Percolation3DSettings.MinSize, Percolation3DSettings.MaxSize),
                Seed = ReadInt(SeedBox, "Начальное число", int.MinValue, int.MaxValue),
                Probability = ReadDouble(ProbabilityBox, "Порог заполнения", 0, 1),
                Medium = (Percolation3DMedium)MediumBox.SelectedIndex,
                Axis = (Percolation3DAxis)AxisBox.SelectedIndex,
                View = (Percolation3DView)ViewBox.SelectedIndex,
                ShowPath = PathBox.IsChecked == true,
                Cut = (Percolation3DCut)CutBox.SelectedIndex,
                CutAxis = (Percolation3DAxis)CutAxisBox.SelectedIndex,
                CutPosition = CutBox.SelectedIndex > 0
                    ? ReadDouble(CutPositionBox, "Положение среза", 0, 1)
                    : _applied.CutPosition
            };
            ProbabilitySlider.Value = _applied.Probability;
            CutPositionSlider.Value = _applied.CutPosition;
            return _applied;
        }
        finally { _loading = loading; }
    }

    public void Load(Percolation3DSettings settings)
    {
        settings.Validate();
        _loading = true;
        try
        {
            _applied = settings;
            SizeBox.Text = settings.Size.ToString(CultureInfo.InvariantCulture);
            SeedBox.Text = settings.Seed.ToString(CultureInfo.InvariantCulture);
            WriteSlider(ProbabilitySlider, ProbabilityBox, settings.Probability);
            MediumBox.SelectedIndex = (int)settings.Medium;
            AxisBox.SelectedIndex = (int)settings.Axis;
            ViewBox.SelectedIndex = (int)settings.View;
            PathBox.IsChecked = settings.ShowPath;
            CutBox.SelectedIndex = (int)settings.Cut;
            CutAxisBox.SelectedIndex = (int)settings.CutAxis;
            WriteSlider(CutPositionSlider, CutPositionBox, settings.CutPosition);
            StatisticsText.Text = "Подготовка образца…";
            UpdateControls();
        }
        finally { _loading = false; }
    }

    private static void WriteSlider(Slider slider, TextBox box, double value)
    {
        slider.Value = value;
        WriteValue(box, value);
    }

    private static void WriteValue(TextBox box, double value) =>
        box.SetCurrentValue(TextBox.TextProperty, value.ToString("R", CultureInfo.InvariantCulture));

    private void ProbabilitySliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_loading) WriteValue(ProbabilityBox, Math.Round(e.NewValue, 6));
    }

    private void CutSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_loading) WriteValue(CutPositionBox, Math.Round(e.NewValue, 6));
    }

    internal void ShowStatistics(Percolation3DSettings settings, Percolation3DStatistics statistics)
    {
        int total = settings.Size * settings.Size * settings.Size;
        string axes = string.Join(", ", Enum.GetValues<Percolation3DAxis>().Where(statistics.Crosses));
        string crossing = statistics.CrossingAxes == 0 ? "Сквозных осей нет" : $"Проход по осям {axes}";
        string route = statistics.PathSteps == 0 ? $"по {settings.Axis} пути нет" :
            $"по {settings.Axis} · сквозных кластеров {statistics.SpanningClusters:N0} · путь {statistics.PathSteps:N0} шаг.";
        StatisticsText.Text = $"Заполнено {(double)statistics.Occupied / total:P1} · кластеров {statistics.ClusterCount:N0}\n" +
            $"Крупнейший кластер · {statistics.LargestSize:N0} ячеек\n{crossing}\n{route}";
    }

    internal void ShowError(string message) => StatisticsText.Text = message;

    private void UpdateControls()
    {
        bool independent = MediumBox.SelectedIndex == (int)Percolation3DMedium.Independent;
        ProbabilityLabel.Text = independent ? "Вероятность заполнения p" : "Порог поля p";
        CriticalButton.Visibility = independent ? Visibility.Visible : Visibility.Collapsed;
        MediumHint.Text = independent
            ? "При том же seed увеличение p добавляет ячейки к прежнему образцу."
            : "Соседние ячейки образуют полости и каналы. Фактическая доля пор указана над настройками.";
        CutPositionPanel.IsEnabled = CutAxisBox.IsEnabled = CutBox.SelectedIndex > 0;
    }

    private void FieldChanged(object sender, EventArgs e)
    {
        if (_loading) return;
        StatisticsText.Text = "Пересчёт связности…";
        ViewChanged(sender, e);
    }

    private void ViewChanged(object sender, EventArgs e)
    {
        if (_loading) return;
        UpdateControls();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RandomSeedClicked(object sender, RoutedEventArgs e)
    {
        NumericInput.CommitAll(this);
        SeedBox.Text = Random.Shared.NextInt64(int.MinValue, (long)int.MaxValue + 1).ToString(CultureInfo.InvariantCulture);
    }

    private void CriticalClicked(object sender, RoutedEventArgs e)
    {
        NumericInput.CommitAll(this);
        WriteValue(ProbabilityBox, Percolation3DSettings.CriticalProbability);
    }
}
