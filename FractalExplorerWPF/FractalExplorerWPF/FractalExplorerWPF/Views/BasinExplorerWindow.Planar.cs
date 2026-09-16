using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class BasinExplorerWindow
{
    private void ConfigurePlanarOptions()
    {
        if (!UsesPlanar) return;
        bool discrete = Kind == BasinExplorerKind.GradientDescent;
        FillOptions(ColoringModeBox,
        [
            (BasinColoringMode.Basins, discrete ? "Цвет минимума" : "Цвет аттрактора"),
            (BasinColoringMode.ConvergenceSpeed, discrete ? "Цвет + скорость сходимости" : "Цвет + время приближения"),
            (BasinColoringMode.OrbitOutcome, "Диагностика: исход траектории"),
            (BasinColoringMode.IterationCount, discrete ? "Тепловая карта итераций" : "Тепловая карта времени")
        ]);
        FillOptions(MarkerModeBox,
        [
            (BasinMarkerMode.Hidden, "Скрыть"),
            (BasinMarkerMode.Markers, "Аттракторы и траектория"),
            (BasinMarkerMode.MarkersWithLabels, "Аттракторы, траектория и подписи")
        ]);
    }

    private void UpdatePlanarLayout()
    {
        bool discrete = Kind == BasinExplorerKind.GradientDescent;
        PlanarPanel.Visibility = Visibility.Visible;
        FormulaPanel.Visibility = Kind == BasinExplorerKind.ComplexGradientFlow ? Visibility.Visible : Visibility.Collapsed;
        FormulaLabel.Text = "Аналитическая функция f(z) для V = ½|f(z)|²";
        ParameterCPanel.Visibility = AttractorsExpander.Visibility = Visibility.Collapsed;
        PotentialPanel.Visibility = discrete ? Visibility.Visible : Visibility.Collapsed;
        VectorFieldPanel.Visibility = Kind == BasinExplorerKind.PolynomialVectorField ? Visibility.Visible : Visibility.Collapsed;
        FlowIntegrationPanel.Visibility = discrete ? Visibility.Collapsed : Visibility.Visible;
        MomentumPanel.Visibility = OptimizerBox.SelectedIndex > 0 ? Visibility.Visible : Visibility.Collapsed;
        AdamPanel.Visibility = OptimizerBox.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
        ApplyFormulaButton.Content = "Применить / найти аттракторы";
        IterationsLabel.Text = discrete ? "Лимит итераций" : "Лимит шагов";
        ColoringHintText.Text = "Обычная раскраска: фон — уход, лимит или нераспознанный результат. Диагностика: синий — уход, серый — лимит, жёлтый — слишком малый шаг интегратора, розовый — NaN/переполнение.";
    }

    private void PopulatePlanarSettings(PlanarBasinSettings p)
    {
        PotentialBox.Text = p.Potential; FieldXBox.Text = p.FieldX; FieldYBox.Text = p.FieldY;
        OptimizerBox.SelectedIndex = (int)p.Optimizer;
        LearningRateBox.Text = FormatNumber(p.LearningRate);
        OptimizerMomentumBox.Text = FormatNumber(p.Momentum);
        AdamBeta2Box.Text = FormatNumber(p.Beta2);
        AdamEpsilonBox.Text = FormatNumber(p.AdamEpsilon);
        FlowTimeStepBox.Text = FormatNumber(p.TimeStep);
        FlowMaxTimeBox.Text = FormatNumber(p.MaxTime);
        FlowAccuracyBox.Text = FormatNumber(p.IntegrationTolerance);
        PlanarToleranceBox.Text = FormatNumber(p.ConvergenceTolerance);
        PlanarSearchRadiusBox.Text = FormatNumber(p.SearchRadius);
        PlanarEscapeRadiusBox.Text = FormatNumber(p.EscapeRadius);
    }

    private PlanarBasinSettings ReadPlanarSettings() => new()
    {
        Potential = PotentialBox.Text.Trim(), FieldX = FieldXBox.Text.Trim(), FieldY = FieldYBox.Text.Trim(),
        Optimizer = (BasinOptimizer)Math.Clamp(OptimizerBox.SelectedIndex, 0, 3),
        LearningRate = ReadDouble(LearningRateBox, "Шаг α", 1e-6, 10),
        Momentum = ReadDouble(OptimizerMomentumBox, "Momentum / β₁", 0, 0.9999),
        Beta2 = ReadDouble(AdamBeta2Box, "β₂", 0, 0.99999),
        AdamEpsilon = ReadDouble(AdamEpsilonBox, "ε Adam", 1e-12, 0.1),
        TimeStep = ReadDouble(FlowTimeStepBox, "Макс. шаг времени", 0.001, 1),
        MaxTime = ReadDouble(FlowMaxTimeBox, "Макс. время", 0.01, 2000),
        IntegrationTolerance = ReadDouble(FlowAccuracyBox, "Точность интегратора", 1e-10, 1e-3),
        ConvergenceTolerance = ReadDouble(PlanarToleranceBox, "Допуск сходимости", 1e-8, 0.01),
        SearchRadius = ReadDouble(PlanarSearchRadiusBox, "Радиус поиска", 0.1, 100),
        EscapeRadius = ReadDouble(PlanarEscapeRadiusBox, "Радиус ухода", 2, 1e6)
    };

    private bool ApplyPlanar(bool showMessage, bool scheduleRender, IReadOnlyList<PlanarBasinAttractor>? supplied = null)
    {
        try
        {
            using (new WaitCursorScope())
            {
                _engine.MaxIterations = ReadInt(IterationsBox, "Лимит шагов", 1, 100_000);
                _engine.RootSearchRadius = ReadDouble(PlanarSearchRadiusBox, "Радиус поиска", 0.1, 100);
                _engine.ConfigurePlanar(ReadPlanarSettings(), FormulaBox.Text.Trim(), supplied);
            }
            _appliedFormula = FormulaBox.Text.Trim();
            _formulaDirty = false;
            _orbitTrace = [];
            DebugOutput.Text = _engine.DebugInfo;
            PlanarTargetsBox.ItemsSource = _engine.PlanarAttractors.Select((a, i) => a.Describe(i)).ToArray();
            PlanarTargetCountText.Text = $"Найдено аттракторов: {_engine.TargetCount}";
            StatusText.Text = _engine.TargetCount > 0
                ? $"Найдено аттракторов: {_engine.TargetCount}. Правая кнопка — результат и траектория."
                : "Аттракторы не найдены. Увеличьте радиус поиска, время или лимит шагов; проверьте устойчивость системы.";
            UpdatePlanarLayout(); UpdateOverlay();
            if (scheduleRender) ScheduleRender();
            return true;
        }
        catch (Exception ex)
        {
            _formulaDirty = true;
            StatusText.Text = ex.Message; DebugOutput.Text = ex.Message;
            PlanarTargetsBox.ItemsSource = null; PlanarTargetCountText.Text = "Параметры не применены";
            MarkerOverlay.Children.Clear();
            if (showMessage) MessageBox.Show(this, ex.Message, "Формула или параметры", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private void PlanarParameter_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_updatingUi || !UsesPlanar) return;
        _formulaDirty = true; _orbitTrace = [];
        UpdatePlanarLayout(); UpdateOverlay(); ScheduleRender();
    }

    private void DrawPlanarMarkers(bool labels)
    {
        var attractors = _engine.PlanarAttractors;
        var colors = NewtonPaletteManager.AdjustColors(_paletteManager.ActivePalette, attractors.Count);
        for (int i = 0; i < attractors.Count; i++)
        {
            var a = attractors[i];
            if (a.IsCycle)
                MarkerOverlay.Children.Add(new Polygon
                {
                    Stroke = new SolidColorBrush(colors[i]), StrokeThickness = 1.5, StrokeDashArray = [4, 3],
                    Points = new PointCollection(a.Points.Select(p => ClampToOverlay(PlaneToScreen(p))))
                });
            AddCircleMarker(a.Points[0], colors[i], 11, labels ? a.Describe(i) : null);
        }
    }

    private string DescribePlanarOrbit(BasinExplorerEngine engine, BasinOrbitResult result, double x, double y)
    {
        string outcome = result.TargetIndex >= 0 ? engine.PlanarAttractors[result.TargetIndex].Describe(result.TargetIndex) : result.Outcome switch
        {
            BasinOrbitOutcome.Escaped => "уход", BasinOrbitOutcome.NonFinite => "NaN / переполнение",
            BasinOrbitOutcome.Degenerate => "интегратору требуется слишком малый шаг", _ => "аттрактор не распознан за отведённый лимит"
        };
        string measure = Kind == BasinExplorerKind.GradientDescent ? "итераций" : "время";
        return $"({x:G5}; {y:G5}): {outcome}; {measure} {result.SmoothIterations:G5}, шагов {result.Iterations}.";
    }
}
