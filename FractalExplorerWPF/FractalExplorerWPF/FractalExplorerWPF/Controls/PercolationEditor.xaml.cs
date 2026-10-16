using System.Windows;
using System.Windows.Controls;
using FractalExplorerWPF.Core.PercolationMath;

namespace FractalExplorerWPF.Controls;

public partial class PercolationEditor : UserControl
{
    private bool _syncing;
    private double _critical;
    public event Action<double>? ProbabilityChanged;
    public event Action<int>? ColoringChanged;

    public PercolationEditor() => InitializeComponent();

    public void SetParameters(int mode, double probability, int coloring)
    {
        _syncing = true;
        try
        {
            _critical = Percolation.CriticalProbability(mode == 1 ? PercolationLattice.Triangular : PercolationLattice.Square);
            ProbabilitySlider.Value = probability;
            ColoringBox.SelectedIndex = Math.Clamp(coloring, 0, 3);
            ThresholdText.Text = $"pс {(mode == 1 ? "=" : "≈")} {_critical:F6}. На конечном поле наличие сквозного кластера зависит от seed.";
        }
        finally { _syncing = false; }
    }

    public void SetStatistics(PercolationStatistics value, int size)
    {
        string YesNo(bool value) => value ? "есть" : "нет";
        StatisticsText.Text = $"Заполнено: {value.Occupied:N0} ({(double)value.Occupied / (size * size):P1})\n" +
            $"Кластеров: {value.ClusterCount:N0}; крупнейший: {value.LargestSize:N0}\n" +
            $"Слева направо: {YesNo(value.HorizontalCrossing)}\nСверху вниз: {YesNo(value.VerticalCrossing)}\n" +
            (value.HorizontalCrossing || value.VerticalCrossing ? $"Кратчайший путь: {value.PathLength:N0} рёбер" : "Сквозного пути пока нет");
    }

    public void ClearStatistics() => StatisticsText.Text = "Пересчёт…";
    private void Probability_OnChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_syncing) ProbabilityChanged?.Invoke(e.NewValue);
    }
    private void Coloring_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncing && ColoringBox.SelectedIndex >= 0) ColoringChanged?.Invoke(ColoringBox.SelectedIndex);
    }
    private void Below_OnClick(object sender, RoutedEventArgs e) => ProbabilityChanged?.Invoke(_critical - 0.08);
    private void Critical_OnClick(object sender, RoutedEventArgs e) => ProbabilityChanged?.Invoke(_critical);
    private void Above_OnClick(object sender, RoutedEventArgs e) => ProbabilityChanged?.Invoke(_critical + 0.08);
}
