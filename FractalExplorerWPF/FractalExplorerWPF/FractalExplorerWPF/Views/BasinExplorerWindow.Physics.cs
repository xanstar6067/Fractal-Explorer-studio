using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Infrastructure;
using Point = System.Windows.Point;

namespace FractalExplorerWPF.Views;

public partial class BasinExplorerWindow
{
    private List<BasinForceCenter> _forceCenters = [];
    private int _draggedCenter = -1;

    private void ConfigureExtendedOptions()
    {
        if (!UsesPhysics) return;
        FillOptions(ColoringModeBox,
        [
            (BasinColoringMode.Basins, "Цвет конечного центра"),
            (BasinColoringMode.ConvergenceSpeed, "Цвет + яркость по времени"),
            (BasinColoringMode.OrbitOutcome, "Диагностика: исход траектории"),
            (BasinColoringMode.IterationCount, "Тепловая карта времени")
        ]);
        FillOptions(MarkerModeBox,
        [
            (BasinMarkerMode.Hidden, "Скрыть"),
            (BasinMarkerMode.Markers, "Центры и радиусы захвата"),
            (BasinMarkerMode.MarkersWithLabels, "Центры, радиусы и подписи")
        ]);
    }

    private void UpdateExtendedLayout()
    {
        bool logistic = Kind == BasinExplorerKind.ComplexLogistic;
        if (!logistic && !UsesPhysics) return;
        FormulaPanel.Visibility = Visibility.Collapsed;
        LogisticPanel.Visibility = logistic ? Visibility.Visible : Visibility.Collapsed;
        PhysicsPanel.Visibility = UsesPhysics ? Visibility.Visible : Visibility.Collapsed;
        ParameterCPanel.Visibility = logistic && !IsLogisticParameter ? Visibility.Visible : Visibility.Collapsed;
        ParameterRealLabel.Text = "Re λ";
        ParameterImaginaryLabel.Text = "Im λ";
        LogisticSeedPanel.Visibility = IsLogisticParameter ? Visibility.Visible : Visibility.Collapsed;
        AttractorsExpander.Visibility = UsesPhysics ? Visibility.Collapsed : Visibility.Visible;
        AttractorDiscoveryPanel.Visibility = IsLogisticParameter ? Visibility.Collapsed : Visibility.Visible;
        AttractorSearchRadiusGrid.Visibility = IsLogisticParameter ? Visibility.Collapsed : Visibility.Visible;
        ApplyFormulaButton.Content = UsesPhysics ? "Применить параметры" : "Применить / найти циклы";
        RestoringForcePanel.Visibility = Kind == BasinExplorerKind.MagneticPendulum ? Visibility.Visible : Visibility.Collapsed;
        CaptureModeBox.IsEnabled = Kind != BasinExplorerKind.MagneticPendulum;
        if (UsesPhysics)
        {
            IterationsLabel.Text = "Лимит шагов";
            ShadingScaleLabel.ToolTip = "Шкала времени в условных единицах";
            ColoringHintText.Text = "Яркость и тепловая карта показывают время движения. В диагностике: цвет центра — захват, синий — уход, серый — лимит, розовый — переполнение.";
        }
        else if (IsLogisticParameter)
            ColoringHintText.Text = "Цвет соответствует периоду 1…N; фон — уход и нераспознанные орбиты. Нейтральные циклы не считаются притягивающими.";
        foreach (ComboBoxItem item in ColoringModeBox.Items)
            if (item.Tag is BasinColoringMode.CyclePhase) item.IsEnabled = !IsLogisticParameter;
        if (IsLogisticParameter && SelectedColoringMode == BasinColoringMode.CyclePhase)
            SelectOption(ColoringModeBox, BasinColoringMode.Period);
    }

    private void PopulateExtendedSettings(BasinExplorerState state)
    {
        LogisticPlaneBox.SelectedIndex = state.LogisticPlane == LogisticPlaneMode.Parameter ? 1 : 0;
        SetComplex(LogisticSeedRealBox, LogisticSeedImaginaryBox, state.LogisticSeed);
        PhysicalBasinSettings p = state.Physics;
        _forceCenters = p.Centers.Select(c => c.Clone()).ToList();
        RefreshForceCenters(0);
        CaptureModeBox.SelectedIndex = Kind == BasinExplorerKind.MagneticPendulum ? 1 : (int)p.CaptureMode;
        DampingBox.Text = FormatNumber(p.Damping);
        RestoringForceBox.Text = FormatNumber(p.RestoringForce);
        PhysicalHeightBox.Text = FormatNumber(p.Height);
        SetComplex(VelocityXBox, VelocityYBox, p.InitialVelocity);
        PhysicsTimeStepBox.Text = FormatNumber(p.TimeStep);
        PhysicsMaxTimeBox.Text = FormatNumber(p.MaxTime);
        SettleSpeedBox.Text = FormatNumber(p.SettleSpeed);
        SettleTimeBox.Text = FormatNumber(p.SettleTime);
        PhysicalEscapeBox.Text = FormatNumber(p.EscapeDistance);
    }

    private void CaptureExtendedSettings(BasinExplorerState state)
    {
        if (UsesPhysics) state.Physics = ReadPhysicsSettings();
        if (Kind != BasinExplorerKind.ComplexLogistic) return;
        state.Formula = "c*z*(1-z)";
        state.LogisticPlane = IsLogisticParameter ? LogisticPlaneMode.Parameter : LogisticPlaneMode.InitialValues;
        state.LogisticSeed = ReadComplex(LogisticSeedRealBox, LogisticSeedImaginaryBox, "Начальное z₀");
    }

    private PhysicalBasinSettings ReadPhysicsSettings()
    {
        ReadSelectedCenter();
        return new()
        {
            Centers = _forceCenters.Select(c => c.Clone()).ToList(),
            CaptureMode = Kind == BasinExplorerKind.MagneticPendulum ? PhysicalCaptureMode.Settle : (PhysicalCaptureMode)CaptureModeBox.SelectedIndex,
            Damping = ReadDouble(DampingBox, "Трение", 0, 10),
            RestoringForce = Kind == BasinExplorerKind.MagneticPendulum ? ReadDouble(RestoringForceBox, "Возвратная сила", 0, 10) : 0,
            Height = ReadDouble(PhysicalHeightBox, "Высота / смягчение", 0.03, 5),
            TimeStep = ReadDouble(PhysicsTimeStepBox, "Шаг времени", 0.001, 0.1),
            MaxTime = ReadDouble(PhysicsMaxTimeBox, "Время моделирования", 0.1, 1000),
            SettleSpeed = ReadDouble(SettleSpeedBox, "Скорость покоя", 0.001, 1),
            SettleTime = ReadDouble(SettleTimeBox, "Время покоя", 0.1, 10),
            EscapeDistance = ReadDouble(PhysicalEscapeBox, "Радиус ухода", 2, 1e6),
            InitialVelocity = new(ReadDouble(VelocityXBox, "Скорость X", -100, 100), ReadDouble(VelocityYBox, "Скорость Y", -100, 100))
        };
    }

    private bool ApplyPhysics(bool showMessage, bool scheduleRender)
    {
        try
        {
            _engine.ConfigurePhysics(ReadPhysicsSettings());
            _formulaDirty = false;
            DebugOutput.Text = _engine.DebugInfo;
            StatusText.Text = $"Центров: {_forceCenters.Count}. Правая кнопка показывает траекторию.";
            _orbitTrace = [];
            UpdateOverlay();
            if (scheduleRender) ScheduleRender();
            return true;
        }
        catch (InvalidOperationException ex)
        {
            _formulaDirty = true;
            StatusText.Text = ex.Message;
            if (showMessage) MessageBox.Show(this, ex.Message, "Параметры", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private void ExtendedParameter_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_updatingUi) return;
        _formulaDirty = true;
        _orbitTrace = [];
        UpdateExtendedLayout();
        UpdateOverlay();
        ScheduleRender();
    }

    private void EditCenters_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_updatingUi) return;
        if (EditCentersBox.IsChecked == true && SelectedMarkerMode == BasinMarkerMode.Hidden)
            StatusText.Text = "Редактирование включено. Для просмотра центров включите маркеры вверху панели.";
        EndForceCenterDrag();
        UpdateOverlay();
    }

    private void RefreshForceCenters(int selected)
    {
        bool previous = _updatingUi;
        _updatingUi = true;
        ForceCentersBox.ItemsSource = null;
        ForceCentersBox.ItemsSource = _forceCenters;
        ForceCentersBox.SelectedIndex = Math.Clamp(selected, 0, Math.Max(0, _forceCenters.Count - 1));
        PopulateSelectedCenter();
        _updatingUi = previous;
    }

    private void ForceCenter_OnSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingUi) return;
        bool previous = _updatingUi;
        _updatingUi = true;
        PopulateSelectedCenter();
        _updatingUi = previous;
        UpdateOverlay();
    }

    private void PopulateSelectedCenter()
    {
        if (ForceCentersBox.SelectedItem is not BasinForceCenter c) return;
        ForceCenterXBox.Text = FormatNumber(c.X);
        ForceCenterYBox.Text = FormatNumber(c.Y);
        ForceCenterStrengthBox.Text = FormatNumber(c.Strength);
        ForceCenterRadiusBox.Text = FormatNumber(c.CaptureRadius);
    }

    private void ReadSelectedCenter()
    {
        if (ForceCentersBox.SelectedItem is not BasinForceCenter c) return;
        double x = ReadDouble(ForceCenterXBox, "Координата X", -1e4, 1e4);
        double y = ReadDouble(ForceCenterYBox, "Координата Y", -1e4, 1e4);
        double strength = ReadDouble(ForceCenterStrengthBox, "Сила центра", 0.01, 100);
        double radius = ReadDouble(ForceCenterRadiusBox, "Радиус захвата", 0.01, 10);
        c.X = x; c.Y = y; c.Strength = strength; c.CaptureRadius = radius;
    }

    private void CenterParameter_OnChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingUi) return;
        _formulaDirty = true;
        _renderCts?.Cancel();
        try { ReadSelectedCenter(); }
        catch (InvalidOperationException ex) { StatusText.Text = ex.Message; return; }
        ForceCentersBox.Items.Refresh();
        _orbitTrace = [];
        UpdateOverlay();
        ScheduleRender();
    }

    private void RemoveForceCenter_OnClick(object sender, RoutedEventArgs e)
    {
        int selected = ForceCentersBox.SelectedIndex;
        if (selected < 0 || _forceCenters.Count <= 1) { StatusText.Text = "Оставьте хотя бы один центр."; return; }
        _forceCenters.RemoveAt(selected);
        RefreshForceCenters(selected);
        _formulaDirty = true;
        _orbitTrace = [];
        UpdateOverlay();
        ScheduleRender();
    }

    private bool HandleExtendedMouseDown(MouseButtonEventArgs e)
    {
        if (IsLogisticParameter && e.ClickCount == 2)
        {
            (double x, double y) = ScreenToPlane(e.GetPosition(CanvasHost));
            try
            {
                BasinExplorerState state = CaptureState("Выбранное λ");
                state.ParameterC = new(x, y); state.LogisticPlane = LogisticPlaneMode.InitialValues;
                state.CenterX = 0.5; state.CenterY = 0; state.Zoom = 2; state.UseSavedAttractors = false;
                state.ColoringMode = BasinColoringMode.ConvergenceSpeed;
                ApplyState(state, keepPalette: true, showMessage: true);
            }
            catch (InvalidOperationException ex) { StatusText.Text = ex.Message; }
            e.Handled = true;
            return true;
        }
        if (!UsesPhysics || EditCentersBox.IsChecked != true) return false;
        Point click = e.GetPosition(CanvasHost);
        int selected = _forceCenters.FindIndex(c => (PlaneToScreen(new(c.X, c.Y)) - click).Length <= 14);
        if (selected < 0)
        {
            if (_forceCenters.Count == 16) { StatusText.Text = "Максимум 16 центров."; return true; }
            (double x, double y) = ScreenToPlane(click);
            if (Math.Abs(x) > 1e4 || Math.Abs(y) > 1e4) { StatusText.Text = "Координаты центра должны быть в пределах ±10000."; return true; }
            _forceCenters.Add(new() { X = x, Y = y });
            selected = _forceCenters.Count - 1;
        }
        RefreshForceCenters(selected);
        _draggedCenter = selected;
        _renderCts?.Cancel();
        _renderTimer.Stop();
        _formulaDirty = true;
        _orbitTrace = [];
        CanvasHost.CaptureMouse();
        UpdateOverlay();
        e.Handled = true;
        return true;
    }

    private bool MoveForceCenter(MouseEventArgs e)
    {
        if (_draggedCenter < 0) return false;
        (double x, double y) = ScreenToPlane(e.GetPosition(CanvasHost));
        BasinForceCenter c = _forceCenters[_draggedCenter];
        c.X = Math.Clamp(x, -1e4, 1e4); c.Y = Math.Clamp(y, -1e4, 1e4);
        RefreshForceCenters(_draggedCenter);
        UpdateOverlay();
        e.Handled = true;
        return true;
    }

    private bool EndForceCenterDrag()
    {
        if (_draggedCenter < 0) return false;
        _draggedCenter = -1;
        CanvasHost.ReleaseMouseCapture();
        ScheduleRender();
        return true;
    }

    private void CanvasHost_OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_draggedCenter < 0) return;
        _draggedCenter = -1;
        ScheduleRender();
    }

    private void DrawForceCenters(bool labels)
    {
        var colors = NewtonPaletteManager.AdjustColors(_paletteManager.ActivePalette, _forceCenters.Count);
        for (int i = 0; i < _forceCenters.Count; i++)
        {
            BasinForceCenter c = _forceCenters[i];
            var position = new Complex(c.X, c.Y);
            Point screen = PlaneToScreen(position);
            double radius = c.CaptureRadius * Math.Max(1, CanvasHost.ActualWidth) * _zoom / BaseScale;
            if (radius < 1e6)
            {
                var ring = new Ellipse { Width = 2 * radius, Height = 2 * radius, Stroke = new SolidColorBrush(colors[i]),
                    StrokeThickness = 1, Opacity = 0.6 };
                Canvas.SetLeft(ring, screen.X - radius); Canvas.SetTop(ring, screen.Y - radius);
                MarkerOverlay.Children.Add(ring);
            }
            AddCircleMarker(position, colors[i], i == ForceCentersBox.SelectedIndex ? 16 : 12, labels ? $"{i + 1} · {c.Strength:G3}" : null);
        }
    }
}
