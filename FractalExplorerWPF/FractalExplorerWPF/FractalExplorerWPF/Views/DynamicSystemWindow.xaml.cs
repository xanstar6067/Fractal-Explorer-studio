using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FractalExplorer.Engines;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Infrastructure.ColorPicking;
using FractalExplorerWPF.Models;
using Microsoft.Win32;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace FractalExplorerWPF.Views;

public partial class DynamicSystemWindow : Window
{
    private readonly DynamicSystemKind _kind;
    private readonly DynamicSystemSaveStore _saves;
    private readonly DynamicPaletteStore? _paletteStore;
    private readonly Dictionary<string, TextBox> _boxes = [];
    private readonly Dictionary<string, ComboBox> _choices = [];
    private readonly Dictionary<string, StackPanel> _fieldPanels = [];
    private readonly Dictionary<string, TextBlock> _fieldLabels = [];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly DispatcherTimer _visualizationTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly ConcurrentQueue<DynamicTileRenderEvent> _visualizationEvents = new();
    private readonly TransformGroup _previewTransform = new();
    private readonly ScaleTransform _previewScale = new(1, 1);
    private readonly TranslateTransform _previewTranslation = new();
    private DynamicSystemState _state;
    private List<DynamicPalette> _palettes = [];
    private CancellationTokenSource? _cts;
    private WriteableBitmap? _progressiveBitmap;
    private bool _rendering, _syncing, _panning, _controls = true, _fullscreen, _hasRenderedFrame;
    private Point _panStart;
    private double _renderedCenterX, _renderedCenterY, _renderedZoom = 1;
    private double _renderedAMin, _renderedAMax, _renderedBMin, _renderedBMax;
    private WindowStyle _oldStyle;
    private WindowState _oldState;
    private TextBlock? _attractorFormulaText;
    private StackPanel? _quadraticPanel;
    private TextBox? _quadraticCodeBox;
    private ComboBox? _quadraticPresetsBox;
    private TextBlock? _quadraticStatus;
    private Button? _quadraticSearchButton;
    private readonly Dictionary<int, TextBox> _quadraticBoxes = [];
    private CancellationTokenSource? _quadraticSearchCts;
    private bool _quadraticCoefficientsDirty;
    private static readonly Color[] QuadraticPresetColors =
    [
        Color.FromRgb(150, 231, 255), // ледяной плащ
        Color.FromRgb(255, 178, 117), // комета
        Color.FromRgb(186, 160, 255), // лезвие
        Color.FromRgb(133, 244, 205), // острова
        Color.FromRgb(255, 218, 151)  // игла
    ];

    public DynamicSystemWindow(DynamicSystemKind kind, Attractor2DKind? initialAttractor = null)
    {
        _kind = kind; _state = DynamicSystemState.CreateDefault(kind); _saves = new(kind);
        if (kind == DynamicSystemKind.Attractors2D && initialAttractor is { } selected)
        {
            _state.ApplyAttractor2DPreset(selected);
            if (selected == Attractor2DKind.SprottQuadratic)
                SprottQuadraticMap.ApplyCode(_state, SprottQuadraticMap.Presets[0].Code);
        }
        if (kind is DynamicSystemKind.Lyapunov or DynamicSystemKind.LogisticMap) _paletteStore = new(kind);
        InitializeComponent();
        _previewTransform.Children.Add(_previewScale);
        _previewTransform.Children.Add(_previewTranslation);
        StableImage.RenderTransformOrigin = new Point(0.5, 0.5);
        StableImage.RenderTransform = _previewTransform;
        Title = DisplayName(kind);
        BuildParameterPanel(); LoadPalettes(); SyncControls(); UpdateAttractorPresentation(); UpdateSwatches();
        _timer.Tick += (_, _) => { _timer.Stop(); _ = RenderAsync(); };
        _visualizationTimer.Tick += (_, _) => FlushVisualizationEvents(false);
        Loaded += (_, _) => Schedule();
    }

    private void BuildParameterPanel()
    {
        if (_kind == DynamicSystemKind.Attractors2D)
        {
            AddChoice("Формула", "Attractor2DMode",
            new ChoiceOption[]
            {
                new("Клиффорд", nameof(Attractor2DKind.Clifford)),
                new("Питер де Йонг", nameof(Attractor2DKind.PeterDeJong)),
                new("Tinkerbell", nameof(Attractor2DKind.Tinkerbell)),
                new("Gumowski–Mira", nameof(Attractor2DKind.GumowskiMira)),
                new("Спротт · квадратичная карта", nameof(Attractor2DKind.SprottQuadratic))
            });
            _attractorFormulaText = new TextBlock
            {
                Margin = new Thickness(0, 2, 0, 10),
                TextWrapping = TextWrapping.Wrap
            };
            _attractorFormulaText.SetResourceReference(TextBlock.ForegroundProperty, "Theme.SecondaryTextBrush");
            ParameterPanel.Children.Add(_attractorFormulaText);
            BuildQuadraticPanel();
        }
        foreach ((string label, string key) in Fields(_kind)) AddField(label, key);
        if (_kind is DynamicSystemKind.Lorenz or DynamicSystemKind.Rossler) AddChoice("Проекция", "ProjectionMode", ["XY", "XZ", "YZ"]);
        if (_kind == DynamicSystemKind.LogisticMap) AddChoice("Режим", "VisualizationMode", ["Orbit", "Bifurcation", "Cobweb"]);
        if (_kind is DynamicSystemKind.Lyapunov or DynamicSystemKind.Attractors2D) AddChoice("Сглаживание", "SsaaFactor", ["1", "2", "4"]);
        AddThreadChoice();
        PaletteButton.Visibility = _paletteStore is null ? Visibility.Collapsed : Visibility.Visible;
        FractalColorPanel.Visibility = _kind is DynamicSystemKind.Bifurcation or DynamicSystemKind.Attractors2D ? Visibility.Visible : Visibility.Collapsed;
        BackgroundColorPanel.Visibility = _kind is DynamicSystemKind.Lyapunov or DynamicSystemKind.Henon or DynamicSystemKind.Ikeda ? Visibility.Collapsed : Visibility.Visible;
        FractalColorButton.Content = _kind == DynamicSystemKind.Attractors2D ? "Цвет плотности" : "Цвет фрактала";
        UpdateAttractorPresentation();
    }

    private void BuildQuadraticPanel()
    {
        var panel = new StackPanel { Visibility = Visibility.Collapsed };
        _quadraticPanel = panel;
        panel.Children.Add(new TextBlock { Text = "Готовые формы" });
        var presets = new ComboBox { ItemsSource = SprottQuadraticMap.Presets.Select(p => new ChoiceOption(p.Name, p.Code)).ToArray(),
            DisplayMemberPath = nameof(ChoiceOption.Display) };
        _quadraticPresetsBox = presets;
        presets.SelectionChanged += (_, _) =>
        {
            if (_syncing || presets.SelectedItem is not ChoiceOption option) return;
            ApplyQuadraticCode(option.Value);
        };
        panel.Children.Add(presets);
        panel.Children.Add(new TextBlock { Text = "Код формы · E + 12 букв A–Y" });
        _quadraticCodeBox = new TextBox { ToolTip = "Код Спротта позволяет точно повторить найденный аттрактор." };
        panel.Children.Add(_quadraticCodeBox);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var apply = new Button { Content = "Открыть код", Padding = new Thickness(7, 2, 7, 2) };
        apply.Click += (_, _) => ApplyQuadraticCode(_quadraticCodeBox.Text);
        buttons.Children.Add(apply);
        _quadraticSearchButton = new Button { Content = "Найти новую", Padding = new Thickness(7, 2, 7, 2) };
        _quadraticSearchButton.Click += QuadraticSearch_OnClick;
        buttons.Children.Add(_quadraticSearchButton);
        panel.Children.Add(buttons);
        _quadraticStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 8) };
        _quadraticStatus.SetResourceReference(TextBlock.ForegroundProperty, "Theme.SecondaryTextBrush");
        panel.Children.Add(_quadraticStatus);
        var expander = new Expander { Header = "12 коэффициентов · точная настройка", IsExpanded = false };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (int row = 0; row < 6; row++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int i = 0; i < 12; i++)
        {
            var field = new StackPanel { Margin = i % 2 == 0 ? new Thickness(0, 0, 5, 0) : new Thickness(5, 0, 0, 0) };
            string term = (i % 6) switch { 0 => "1", 1 => "x", 2 => "x²", 3 => "xy", 4 => "y", _ => "y²" };
            field.Children.Add(new TextBlock { Text = $"a{i + 1} · {term}" });
            var box = new TextBox();
            NumericSpinner.SetIsEnabled(box, true);
            box.TextChanged += (_, _) => { if (!_syncing) { _quadraticCoefficientsDirty = true; Schedule(); } };
            field.Children.Add(box);
            _quadraticBoxes[i] = box;
            Grid.SetColumn(field, i / 6);
            Grid.SetRow(field, i % 6);
            grid.Children.Add(field);
        }
        var content = new StackPanel();
        content.Children.Add(grid);
        var fit = new Button { Content = "Подобрать кадр по орбите" };
        fit.Click += QuadraticFit_OnClick;
        content.Children.Add(fit);
        expander.Content = content;
        panel.Children.Add(expander);
        ParameterPanel.Children.Add(panel);
    }

    private static IEnumerable<(string, string)> Fields(DynamicSystemKind kind) => kind switch
    {
        DynamicSystemKind.Lyapunov => [("Мин. A","AMin"),("Макс. A","AMax"),("Мин. B","BMin"),("Макс. B","BMax"),("Паттерн A/B","Pattern"),("Итерации","Iterations"),("Прогрев","TransientIterations")],
        DynamicSystemKind.Lorenz => [("σ","Sigma"),("ρ","Rho"),("β","Beta"),("dt","Dt"),("Шаги","Steps"),("Старт X","StartX"),("Старт Y","StartY"),("Старт Z","StartZ"),("Центр X","CenterX"),("Центр Y","CenterY"),("Масштаб","Zoom")],
        DynamicSystemKind.Rossler => [("a","A"),("b","B"),("c","C"),("dt","Dt"),("Шаги","Steps"),("Старт X","StartX"),("Старт Y","StartY"),("Старт Z","StartZ"),("Центр X","CenterX"),("Центр Y","CenterY"),("Масштаб","Zoom")],
        DynamicSystemKind.LogisticMap => [("Параметр r","R"),("Нач. x₀","X0"),("Итерации","Iterations"),("Прогрев","TransientIterations"),("Bif r min","BifurcationRMin"),("Bif r max","BifurcationRMax"),("Bif samples","BifurcationSamples"),("Bif transient","BifurcationTransient"),("Bif plotted","BifurcationPlottedPoints"),("Cobweb шаги","CobwebSteps"),("Центр X","CenterX"),("Центр Y","CenterY"),("Масштаб","Zoom")],
        DynamicSystemKind.Bifurcation => [("r min","RMin"),("r max","RMax"),("x min","XMin"),("x max","XMax"),("Прогрев","TransientIterations"),("Samples / r","SamplesPerR"),("Итерации","Iterations"),("Центр X","CenterX"),("Центр Y","CenterY"),("Масштаб","Zoom")],
        DynamicSystemKind.Henon => [("Параметр a","A"),("Параметр b","B"),("Нач. x₀","X0"),("Нач. y₀","Y0"),("Итерации","Iterations"),("Пропуск","DiscardIterations"),("Центр X","CenterX"),("Центр Y","CenterY"),("Масштаб","Zoom")],
        DynamicSystemKind.Attractors2D => [("a","A"),("b","B"),("c","C"),("d","D"),("Нач. x₀","X0"),("Нач. y₀","Y0"),("Число точек","Iterations"),("Прогрев","DiscardIterations"),("Гамма плотности","DensityGamma"),("Центр X","CenterX"),("Центр Y","CenterY"),("Масштаб","Zoom")],
        _ => [("Параметр u","U"),("Нач. x₀","X0"),("Нач. y₀","Y0"),("Итерации","Iterations"),("Пропуск","DiscardIterations"),("X min","RangeXMin"),("X max","RangeXMax"),("Y min","RangeYMin"),("Y max","RangeYMax"),("Центр X","CenterX"),("Центр Y","CenterY"),("Масштаб","Zoom")]
    };

    private void AddField(string label, string key)
    {
        var panel = new StackPanel();
        var labelBlock = new TextBlock { Text = label };
        panel.Children.Add(labelBlock);
        var box = new TextBox { Tag = key };
        if (typeof(DynamicSystemState).GetProperty(key)!.PropertyType != typeof(string))
        {
            NumericSpinner.SetIsEnabled(box, true);
            NumericSpinner.SetIsInteger(box, typeof(DynamicSystemState).GetProperty(key)!.PropertyType == typeof(int));
        }
        panel.Children.Add(box); _boxes[key] = box;
        _fieldPanels[key] = panel; _fieldLabels[key] = labelBlock;
        box.TextChanged += (_, _) => { if (!_syncing) Schedule(); };
        ParameterPanel.Children.Add(panel);
    }

    private void AddChoice(string label, string key, string[] values)
        => AddChoice(label, key, values.Select(value => new ChoiceOption(value, value)).ToArray());

    private void AddThreadChoice()
    {
        ChoiceOption[] values =
        [
            .. Enumerable.Range(1, Environment.ProcessorCount)
                .Select(value => new ChoiceOption(value.ToString(CultureInfo.InvariantCulture), value.ToString(CultureInfo.InvariantCulture))),
            new("Auto", "Auto")
        ];
        AddChoice("Потоки ЦП", "Threads", values);
    }

    private void AddChoice(string label, string key, ChoiceOption[] values)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = label });
        var combo = new ComboBox { Name = key + "Box", ItemsSource=values, DisplayMemberPath=nameof(ChoiceOption.Display), Tag=key };
        _choices[key]=combo; combo.SelectionChanged += Choice_OnChanged; panel.Children.Add(combo); ParameterPanel.Children.Add(panel);
    }

    private void Choice_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || sender is not ComboBox { Tag: string key, SelectedItem: ChoiceOption option }) return;
        PropertyInfo? property = typeof(DynamicSystemState).GetProperty(key); if (property is null) return;
        object value = key == "Threads" && option.Value == "Auto"
            ? Environment.ProcessorCount
            : property.PropertyType == typeof(int) ? int.Parse(option.Value, CultureInfo.InvariantCulture) : option.Value;
        property.SetValue(_state, value);
        if (key == "Attractor2DMode")
        {
            _state.ApplyAttractor2DPreset(Attractor2DRenderer.ParseKind(option.Value));
            if (option.Value == nameof(Attractor2DKind.SprottQuadratic))
                SprottQuadraticMap.ApplyCode(_state, SprottQuadraticMap.Presets[0].Code);
            SyncControls();
            UpdateAttractorPresentation();
        }
        Schedule();
    }

    private void UpdateAttractorPresentation()
    {
        if (_kind != DynamicSystemKind.Attractors2D || _attractorFormulaText is null) return;
        Attractor2DKind kind = Attractor2DRenderer.ParseKind(_state.Attractor2DMode);
        bool quadratic = kind == Attractor2DKind.SprottQuadratic;
        Title = quadratic ? "Генератор аттракторов Спротта" : DisplayName(_kind);
        Heading.Text = quadratic ? "Квадратичные карты" : "Параметры";
        if (_quadraticPanel is not null) _quadraticPanel.Visibility = quadratic ? Visibility.Visible : Visibility.Collapsed;
        foreach (string key in new[] { "A", "B", "C" })
            if (_fieldPanels.TryGetValue(key, out StackPanel? panel)) panel.Visibility = quadratic ? Visibility.Collapsed : Visibility.Visible;
        if (_fieldPanels.TryGetValue("D", out StackPanel? dPanel))
            dPanel.Visibility = kind is Attractor2DKind.GumowskiMira or Attractor2DKind.SprottQuadratic ? Visibility.Collapsed : Visibility.Visible;
        if (_fieldLabels.TryGetValue("C", out TextBlock? cLabel))
            cLabel.Text = kind == Attractor2DKind.GumowskiMira ? "μ" : "c";
        _attractorFormulaText.Text = kind switch
        {
            Attractor2DKind.Clifford => "x′ = sin(a·y) + c·cos(a·x)\ny′ = sin(b·x) + d·cos(b·y)",
            Attractor2DKind.PeterDeJong => "x′ = sin(a·y) − cos(b·x)\ny′ = sin(c·x) − cos(d·y)",
            Attractor2DKind.Tinkerbell => "x′ = x² − y² + a·x + b·y\ny′ = 2xy + c·x + d·y",
            Attractor2DKind.SprottQuadratic => "x′ и y′ — полные квадратичные многочлены от x и y.\nИщите ограниченные хаотические орбиты; код E + 12 букв хранит все коэффициенты с шагом 0,1.",
            _ => "x′ = y + a(1 − b·y²)y + f(x)\ny′ = −x + f(x′),  f(t) = μt + 2(1−μ)t²/(1+t²)"
        } + "\n\nЦвет показывает плотность посещения точек орбитой.";
    }

    private void ApplyQuadraticCode(string code)
    {
        try
        {
            SprottQuadraticMap.Analysis view = SprottQuadraticMap.ApplyCode(_state, code);
            int presetIndex = Array.FindIndex(SprottQuadraticMap.Presets,
                p => p.Code.Equals(code.Trim(), StringComparison.OrdinalIgnoreCase));
            if (presetIndex >= 0)
            {
                _state.FractalColor = QuadraticPresetColors[presetIndex];
                UpdateSwatches();
            }
            SyncControls();
            UpdatePreviewTransform();
            if (_quadraticStatus is not null)
                _quadraticStatus.Text = $"Оценка λ₁ ≈ {view.Lyapunov:F2} бит/шаг · кадр подобран по орбите";
            Schedule();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            if (_quadraticStatus is not null) _quadraticStatus.Text = ex.Message;
        }
    }

    private void ReadQuadraticCoefficients(bool updateCode = true)
    {
        double[] values = new double[SprottQuadraticMap.CoefficientCount];
        for (int i = 0; i < values.Length; i++)
        {
            if (!_quadraticBoxes.TryGetValue(i, out TextBox? box) ||
                !TryDouble(box.Text, out double value) || !double.IsFinite(value) || Math.Abs(value) > 10)
                throw new InvalidOperationException($"Коэффициент a{i + 1} должен быть числом от −10 до 10.");
            values[i] = value;
        }
        _state.QuadraticCoefficients = values;
        if (updateCode && _quadraticCodeBox is not null)
        {
            _quadraticCodeBox.Text = SprottQuadraticMap.Encode(values) ?? "";
            if (_quadraticPresetsBox is not null)
            {
                bool wasSyncing = _syncing;
                _syncing = true;
                _quadraticPresetsBox.SelectedItem = _quadraticPresetsBox.Items.OfType<ChoiceOption>()
                    .FirstOrDefault(p => p.Value == _quadraticCodeBox.Text);
                _syncing = wasSyncing;
            }
        }
        _quadraticCoefficientsDirty = false;
    }

    private void QuadraticFit_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            ReadQuadraticCoefficients();
            SprottQuadraticMap.Analysis view = SprottQuadraticMap.Analyze(_state.QuadraticCoefficients, CancellationToken.None)
                ?? throw new InvalidOperationException("Орбита расходится или сжимается в точку. Измените коэффициенты.");
            _state.QuadraticSpan = view.Span;
            _state.CenterX = view.CenterX;
            _state.CenterY = view.CenterY;
            _state.Zoom = 1;
            SyncControls();
            UpdatePreviewTransform();
            if (_quadraticStatus is not null) _quadraticStatus.Text = $"Оценка λ₁ ≈ {view.Lyapunov:F2} бит/шаг";
            Schedule();
        }
        catch (InvalidOperationException ex)
        {
            if (_quadraticStatus is not null) _quadraticStatus.Text = ex.Message;
        }
    }

    private async void QuadraticSearch_OnClick(object sender, RoutedEventArgs e)
    {
        if (_quadraticSearchCts is not null) { _quadraticSearchCts.Cancel(); return; }
        using var cts = new CancellationTokenSource();
        _quadraticSearchCts = cts;
        if (_quadraticSearchButton is not null) _quadraticSearchButton.Content = "Остановить поиск";
        if (_quadraticStatus is not null) _quadraticStatus.Text = "Ищу ограниченную орбиту с положительной экспонентой Ляпунова…";
        try
        {
            var progress = new Progress<int>(value =>
            {
                if (_quadraticStatus is not null) _quadraticStatus.Text = $"Поиск: {value}% попыток";
            });
            SprottQuadraticMap.SearchResult result = await Task.Run(() => SprottQuadraticMap.Search(cts.Token, progress));
            ApplyQuadraticCode(result.Code);
            if (_quadraticStatus is not null)
                _quadraticStatus.Text = $"Найдена новая форма за {result.Attempts:N0} попыток · λ₁ ≈ {result.View.Lyapunov:F2} бит/шаг. Сохраните её через менеджер сохранений.";
        }
        catch (OperationCanceledException)
        {
            if (_quadraticStatus is not null) _quadraticStatus.Text = "Поиск остановлен";
        }
        catch (Exception ex)
        {
            if (_quadraticStatus is not null) _quadraticStatus.Text = ex.Message;
        }
        finally
        {
            _quadraticSearchCts = null;
            if (_quadraticSearchButton is not null) _quadraticSearchButton.Content = "Найти новую";
        }
    }

    private void LoadPalettes()
    {
        if (_paletteStore is null) return;
        _palettes = _paletteStore.Load();
    }
    private DynamicPalette? ActivePalette => _palettes.FirstOrDefault(p => p.Name == _state.PaletteName) ?? _palettes.FirstOrDefault();

    private DynamicSystemState CaptureState(string name)
    {
        foreach ((string key, TextBox box) in _boxes)
        {
            PropertyInfo p = typeof(DynamicSystemState).GetProperty(key)!;
            if (p.PropertyType == typeof(string)) { p.SetValue(_state, box.Text.Trim()); continue; }
            if (p.PropertyType == typeof(int)) { if (!int.TryParse(box.Text, out int v) || v < 0) throw new InvalidOperationException($"Некорректное значение: {key}"); p.SetValue(_state, v); }
            else { if (!TryDouble(box.Text, out double v) || !double.IsFinite(v)) throw new InvalidOperationException($"Некорректное значение: {key}"); p.SetValue(_state, v); }
        }
        if (_kind == DynamicSystemKind.Attractors2D &&
            Attractor2DRenderer.ParseKind(_state.Attractor2DMode) == Attractor2DKind.SprottQuadratic)
            ReadQuadraticCoefficients(_quadraticCoefficientsDirty);
        if (_state.Zoom <= 0 || _state.Threads < 1 || _state.Threads > Environment.ProcessorCount) throw new InvalidOperationException($"Масштаб должен быть положительным, число потоков — от 1 до {Environment.ProcessorCount}.");
        if (_kind == DynamicSystemKind.Lyapunov && (_state.AMax <= _state.AMin || _state.BMax <= _state.BMin || !_state.Pattern.Any(c => c is 'A' or 'a' or 'B' or 'b'))) throw new InvalidOperationException("Проверьте диапазоны A/B и паттерн.");
        if (_kind == DynamicSystemKind.Attractors2D && (_state.Iterations < 1 || _state.DensityGamma is < .05 or > 8)) throw new InvalidOperationException("Число точек должно быть положительным, гамма плотности — от 0.05 до 8.");
        DynamicSystemState result = _state.Clone(name); result.Timestamp = DateTime.Now; result.PaletteName = ActivePalette?.Name ?? string.Empty; return result;
    }

    private void LoadState(DynamicSystemState state) { _cts?.Cancel(); EndVisualization(); CurrentImage.Source=null; int threads = _state.Threads; _state = state.Clone(); _state.Kind = _kind; _state.Threads = threads; SyncControls(); UpdateAttractorPresentation(); UpdateSwatches(); UpdatePreviewTransform(); Schedule(); }
    private void SyncControls()
    {
        _syncing=true;
        foreach ((string key, TextBox box) in _boxes) box.Text = Format(typeof(DynamicSystemState).GetProperty(key)!.GetValue(_state));
        if (_kind == DynamicSystemKind.Attractors2D && _quadraticCodeBox is not null)
        {
            for (int i = 0; i < SprottQuadraticMap.CoefficientCount; i++)
                if (_quadraticBoxes.TryGetValue(i, out TextBox? box))
                    box.Text = Format(_state.QuadraticCoefficients is { Length: SprottQuadraticMap.CoefficientCount }
                        ? _state.QuadraticCoefficients[i] : 0);
            _quadraticCodeBox.Text = SprottQuadraticMap.Encode(_state.QuadraticCoefficients ?? []) ?? "";
            if (_quadraticPresetsBox is not null)
                _quadraticPresetsBox.SelectedItem = _quadraticPresetsBox.Items.OfType<ChoiceOption>()
                    .FirstOrDefault(p => p.Value == _quadraticCodeBox.Text);
        }
        foreach ((string key,ComboBox combo) in _choices)
        {
            string value = Format(typeof(DynamicSystemState).GetProperty(key)!.GetValue(_state));
            if (key == "Threads" && _state.Threads == Environment.ProcessorCount) value = "Auto";
            combo.SelectedItem = combo.Items.OfType<ChoiceOption>().FirstOrDefault(option => option.Value == value);
        }
        _syncing=false;
    }

    private async Task RenderAsync()
    {
        if (_rendering) { Schedule(); return; }
        DynamicSystemState state; try { state = CaptureState("preview"); } catch (Exception ex) { StatusText.Text=ex.Message; return; }
        _cts?.Cancel(); _cts?.Dispose(); _cts=new(); CancellationToken token=_cts.Token; _rendering=true; CancelButton.IsEnabled=true; RenderBadge.Visibility=Visibility.Visible; var watch=Stopwatch.StartNew();
        WriteableBitmap? overlay=null;BitmapSource? image=null;
        try
        {
            RenderSurfaceMetrics surface=RenderSurfaceMetrics.Measure(CanvasSurface);DpiScale dpi=surface.Dpi;int width=surface.PixelWidth,height=surface.PixelHeight;
            Action<MandelbrotRenderTile,byte[]>? tileReady=null;
            Action<MandelbrotRenderTile>? tileStarted=null;
            if (_kind==DynamicSystemKind.Lyapunov)
            {
                int factor=Math.Clamp(state.SsaaFactor,1,4);
                int renderWidth=width*factor,renderHeight=height*factor;
                overlay=ProgressiveRenderBitmap.CreateOverlay(renderWidth,renderHeight,dpi.PixelsPerInchX,dpi.PixelsPerInchY); CurrentImage.Source=overlay;
                BeginVisualization(overlay,renderWidth,renderHeight);
                tileStarted=tile=>_visualizationEvents.Enqueue(new(true,tile,null));
                tileReady=(tile,data)=>_visualizationEvents.Enqueue(new(false,tile,data));
            }
            var progress=new Progress<int>(p=>{ProgressBar.Value=p;ProgressText.Text=$"Рендер: {p}%";RenderBadgeText.Text=$"{p}%";});
            image=await DynamicSystemRenderer.RenderAsync(state,width,height,ActivePalette,token,progress,tileReady,tileStarted:tileStarted,dpiX:dpi.PixelsPerInchX,dpiY:dpi.PixelsPerInchY);
            FlushVisualizationEvents(true);
            if(token.IsCancellationRequested){CurrentImage.Source=null;StatusText.Text="Рендер отменён";return;} StableImage.Source=image; CurrentImage.Source=null; RememberRenderedViewport(state); UpdatePreviewTransform(); ProgressBar.Value=100; ProgressText.Text="Готово"; StatusText.Text=$"Готово за {watch.Elapsed.TotalSeconds:F2} сек.";
        }
        catch(OperationCanceledException){CurrentImage.Source=null;StatusText.Text="Рендер отменён";}
        catch(Exception ex){CurrentImage.Source=null;MessageBox.Show(this,ex.Message,DisplayName(_kind),MessageBoxButton.OK,MessageBoxImage.Error);}
        finally
        {
            CurrentImage.Source=null;EndVisualization();overlay=null;image=null;
            if(_kind==DynamicSystemKind.Lyapunov)
            {
                await Dispatcher.Yield(DispatcherPriority.Background);
                await MemoryPressureRelief.ReleaseAsync();
            }
            _rendering=false;CancelButton.IsEnabled=false;RenderBadge.Visibility=Visibility.Collapsed;
        }
    }

    public BitmapSource? CaptureCurrentPreview(int width, int height) =>
        SavePreviewCapture.Capture(SavePreviewLayer, CanvasHost.Background, width, height, StableImage, CurrentImage);

    public Task<BitmapSource> RenderStatePreviewAsync(
        DynamicSystemState state, int width, int height, CancellationToken token, IProgress<int>? progress = null) =>
        DynamicSystemRenderer.RenderAsync(state.Clone(), width, height, FindPalette(state.PaletteName), token, progress, null, false);
    private DynamicPalette? FindPalette(string name)=>_palettes.FirstOrDefault(p=>p.Name==name)??_palettes.FirstOrDefault();

    private void Schedule(){if(!IsLoaded)return;_timer.Stop();_timer.Start();}
    private void Render_OnClick(object sender,RoutedEventArgs e){_timer.Stop();_cts?.Cancel();_=RenderAsync();}
    private void Cancel_OnClick(object sender,RoutedEventArgs e)=>_cts?.Cancel();
    private void Reset_OnClick(object sender,RoutedEventArgs e)
    {
        DynamicSystemState defaults=DynamicSystemState.CreateDefault(_kind);
        if(_kind==DynamicSystemKind.Attractors2D)
        {
            Attractor2DKind selected = Attractor2DRenderer.ParseKind(_state.Attractor2DMode);
            defaults.ApplyAttractor2DPreset(selected);
            if (selected == Attractor2DKind.SprottQuadratic)
            {
                defaults.QuadraticCoefficients = _state.QuadraticCoefficients.ToArray();
                defaults.QuadraticSpan = _state.QuadraticSpan;
                defaults.CenterX = _state.CenterX;
                defaults.CenterY = _state.CenterY;
            }
        }
        _state.CenterX=defaults.CenterX;_state.CenterY=defaults.CenterY;_state.Zoom=1;
        if(_kind==DynamicSystemKind.Lyapunov){_state.AMin=defaults.AMin;_state.AMax=defaults.AMax;_state.BMin=defaults.BMin;_state.BMax=defaults.BMax;}
        SyncControls();UpdatePreviewTransform();Schedule();
    }
    private void Palette_OnClick(object sender, RoutedEventArgs e)
    {
        if (_paletteStore is null) return;
        if (_kind == DynamicSystemKind.Lyapunov)
        {
            var dialog = new LyapunovPaletteWindow(_paletteStore, _palettes, ActivePalette) { Owner = this };
            dialog.PaletteApplied += (_, _) => ApplyPalette(dialog.SelectedPalette?.Name);
            dialog.ShowDialog();
            return;
        }
        var genericDialog = new DynamicPaletteWindow(_paletteStore, _palettes, ActivePalette) { Owner = this };
        if (genericDialog.ShowDialog() == true) ApplyPalette(genericDialog.SelectedPalette?.Name);
    }

    private void ApplyPalette(string? paletteName)
    {
        if (!string.IsNullOrWhiteSpace(paletteName)) _state.PaletteName = paletteName;
        LoadPalettes();
        Schedule();
    }
    private void FractalColor_OnClick(object sender,RoutedEventArgs e){if(ColorSelectionService.Default.TrySelectColor(this,_state.FractalColor,out Color c)){_state.FractalColor=c;UpdateSwatches();Schedule();}}
    private void BackgroundColor_OnClick(object sender,RoutedEventArgs e){if(ColorSelectionService.Default.TrySelectColor(this,_state.BackgroundColor,out Color c)){_state.BackgroundColor=c;UpdateSwatches();Schedule();}}
    private void UpdateSwatches(){FractalColorSwatch.Background=new SolidColorBrush(_state.FractalColor);BackgroundColorSwatch.Background=new SolidColorBrush(_state.BackgroundColor);}

    private void Saves_OnClick(object sender,RoutedEventArgs e)
    {
        IReadOnlyList<DynamicSystemState> presets=_kind switch
        {
            DynamicSystemKind.Lyapunov =>
            [
                new(){Kind=_kind,SaveName="Классический AB",Timestamp=DateTime.MinValue,PointOfInterestId="classic_ab",AMin=2.5,AMax=4,BMin=2.5,BMax=4,Pattern="AB",Iterations=320,TransientIterations=80,PaletteName="Классическая Ляпунова"},
                new(){Kind=_kind,SaveName="ABBA-структуры",Timestamp=DateTime.MinValue,PointOfInterestId="abba",AMin=3.2,AMax=4,BMin=2.6,BMax=3.6,Pattern="ABBA",Iterations=350,TransientIterations=100,PaletteName="Классическая Ляпунова"}
            ],
            DynamicSystemKind.Attractors2D => Attractor2DPointsOfInterest(),
            _ => []
        };
        SaveManagerWindow.Open(this,new SaveManagerConfiguration<DynamicSystemState>{WindowTitle=$"Сохранение/Загрузка: {DisplayName(_kind)}",Store=_saves,CaptureState=CaptureState,CapturePreview=CaptureCurrentPreview,LoadState=LoadState,RenderPreviewAsync=RenderStatePreviewAsync,GetName=s=>s.SaveName,GetTimestamp=s=>s.Timestamp,GetDetails=s=>$"{s.Timestamp:g} · {Details(s)}",PointsOfInterest=presets});
    }

    private static IReadOnlyList<DynamicSystemState> Attractor2DPointsOfInterest() =>
    [
        CreateAttractor2DPoint("Клиффорд — классический", "clifford_classic", Attractor2DKind.Clifford),
        CreateAttractor2DPoint("Питер де Йонг — вихрь", "de_jong_swirl", Attractor2DKind.PeterDeJong),
        CreateAttractor2DPoint("Tinkerbell — классический", "tinkerbell_classic", Attractor2DKind.Tinkerbell),
        CreateAttractor2DPoint("Gumowski–Mira — организм", "gumowski_mira_organism", Attractor2DKind.GumowskiMira),
        .. SprottQuadraticMap.Presets.Select(p => CreateSprottPoint(p.Name, p.Code))
    ];

    private static DynamicSystemState CreateAttractor2DPoint(string name, string id, Attractor2DKind kind)
    {
        DynamicSystemState state=DynamicSystemState.CreateDefault(DynamicSystemKind.Attractors2D);
        state.ApplyAttractor2DPreset(kind);state.SaveName=name;state.PointOfInterestId=id;state.Timestamp=DateTime.MinValue;
        return state;
    }

    private static DynamicSystemState CreateSprottPoint(string name, string code)
    {
        DynamicSystemState state = DynamicSystemState.CreateDefault(DynamicSystemKind.Attractors2D);
        SprottQuadraticMap.ApplyCode(state, code);
        int presetIndex = Array.FindIndex(SprottQuadraticMap.Presets, p => p.Code == code);
        if (presetIndex >= 0) state.FractalColor = QuadraticPresetColors[presetIndex];
        state.SaveName = $"Спротт — {name}";
        state.PointOfInterestId = $"sprott_{code}";
        state.Timestamp = DateTime.MinValue;
        return state;
    }

    private void Export_OnClick(object sender,RoutedEventArgs e)
    {
        int w=Math.Max(1,(int)CanvasSurface.ActualWidth),h=Math.Max(1,(int)CanvasSurface.ActualHeight);_cts?.Cancel();try{_=CaptureState("export");}catch(Exception ex){MessageBox.Show(this,ex.Message,"Параметры экспорта",MessageBoxButton.OK,MessageBoxImage.Warning);return;}
        ImageExportManagerWindow.Open(this,new ImageExportConfiguration{FileNamePrefix=_kind.ToString(),InitialWidth=w,InitialHeight=h,MaxSsaaFactor=4,ReleaseMemoryAfterExport=_kind==DynamicSystemKind.Lyapunov,RenderAsync=(request,token,progress)=>{DynamicSystemState state=CaptureState("export");state.SsaaFactor=request.SsaaFactor;return DynamicSystemRenderer.RenderAsync(state,request.Width,request.Height,ActivePalette,token,progress,null,false);}});
    }

    private void CanvasHost_OnSizeChanged(object sender,SizeChangedEventArgs e){UpdatePreviewTransform();Schedule();}
    private void CanvasHost_OnMouseWheel(object sender,MouseWheelEventArgs e){CommitAndBakePreview();double k=e.Delta>0?.82:1.22;Point p=e.GetPosition(CanvasSurface);if(_kind==DynamicSystemKind.Lyapunov){double ax=_state.AMin+p.X/Math.Max(1,CanvasSurface.ActualWidth)*(_state.AMax-_state.AMin),by=_state.BMax-p.Y/Math.Max(1,CanvasSurface.ActualHeight)*(_state.BMax-_state.BMin);_state.AMin=ax+(_state.AMin-ax)*k;_state.AMax=ax+(_state.AMax-ax)*k;_state.BMin=by+(_state.BMin-by)*k;_state.BMax=by+(_state.BMax-by)*k;}else{double w=Math.Max(1,CanvasSurface.ActualWidth),h=Math.Max(1,CanvasSurface.ActualHeight);var before=ViewSpans(_state,w,h);double fx=p.X/w-.5,fy=.5-p.Y/h,wx=_state.CenterX+fx*before.X,wy=_state.CenterY+fy*before.Y;_state.Zoom=Math.Clamp(_state.Zoom/k,.01,1_000_000);var after=ViewSpans(_state,w,h);_state.CenterX=wx-fx*after.X;_state.CenterY=wy-fy*after.Y;}SyncControls();UpdatePreviewTransform();Schedule();e.Handled=true;}
    private void CanvasHost_OnMouseLeftButtonDown(object sender,MouseButtonEventArgs e){CommitAndBakePreview();_panning=true;_panStart=e.GetPosition(CanvasSurface);CanvasHost.CaptureMouse();Mouse.OverrideCursor=Cursors.SizeAll;}
    private void CanvasHost_OnMouseMove(object sender,MouseEventArgs e){if(!_panning)return;Point p=e.GetPosition(CanvasSurface);double dx=(p.X-_panStart.X)/Math.Max(1,CanvasSurface.ActualWidth),dy=(p.Y-_panStart.Y)/Math.Max(1,CanvasSurface.ActualHeight);if(_kind==DynamicSystemKind.Lyapunov){double aw=_state.AMax-_state.AMin,bh=_state.BMax-_state.BMin;_state.AMin-=dx*aw;_state.AMax-=dx*aw;_state.BMin+=dy*bh;_state.BMax+=dy*bh;}else{var span=ViewSpans(_state,CanvasSurface.ActualWidth,CanvasSurface.ActualHeight);_state.CenterX-=dx*span.X;_state.CenterY+=dy*span.Y;}_panStart=p;SyncControls();UpdatePreviewTransform();}
    private void CanvasHost_OnMouseLeftButtonUp(object sender,MouseButtonEventArgs e){if(!_panning)return;_panning=false;CanvasHost.ReleaseMouseCapture();Mouse.OverrideCursor=null;Schedule();}

    private void BeginVisualization(WriteableBitmap bitmap,int renderWidth,int renderHeight)
    {
        while(_visualizationEvents.TryDequeue(out _)){}
        _progressiveBitmap=bitmap;
        RenderOverlay.BeginSession(renderWidth,renderHeight);
        _visualizationTimer.Start();
    }

    private void FlushVisualizationEvents(bool drainAll)
    {
        if(_progressiveBitmap is not{}bitmap)return;
        int processed=0;bool changed=false;
        while((drainAll||processed<512)&&_visualizationEvents.TryDequeue(out DynamicTileRenderEvent visualEvent))
        {
            if(visualEvent.IsStart)RenderOverlay.StartTile(visualEvent.Tile);
            else if(visualEvent.Pixels is not null&&ProgressiveRenderBitmap.WriteTile(bitmap,visualEvent.Tile,visualEvent.Pixels))RenderOverlay.CompleteTile(visualEvent.Tile);
            processed++;changed=true;
        }
        if(changed)RenderOverlay.Refresh();
    }

    private void EndVisualization()
    {
        _visualizationTimer.Stop();
        _progressiveBitmap=null;
        while(_visualizationEvents.TryDequeue(out _)){}
        RenderOverlay.EndSession();
    }

    private void CommitAndBakePreview()
    {
        if(_kind!=DynamicSystemKind.Lyapunov||_progressiveBitmap is null)
        {
            _cts?.Cancel();
            EndVisualization();
            CurrentImage.Source=null;
            return;
        }

        _cts?.Cancel();
        FlushVisualizationEvents(true);
        RenderSurfaceMetrics surface=RenderSurfaceMetrics.Measure(ImageLayer);
        try
        {
            var baked=new RenderTargetBitmap(surface.PixelWidth,surface.PixelHeight,
                surface.Dpi.PixelsPerInchX,surface.Dpi.PixelsPerInchY,PixelFormats.Pbgra32);
            baked.Render(ImageLayer);
            baked.Freeze();
            StableImage.Source=baked;
            RememberRenderedViewport(_state);
            UpdatePreviewTransform();
        }
        catch(InvalidOperationException)
        {
            // Layout can briefly be unavailable during minimization or a resize transition.
        }
        finally
        {
            CurrentImage.Source=null;
            EndVisualization();
        }
    }

    private void RememberRenderedViewport(DynamicSystemState state)
    {
        _renderedCenterX=state.CenterX;_renderedCenterY=state.CenterY;_renderedZoom=state.Zoom;
        _renderedAMin=state.AMin;_renderedAMax=state.AMax;_renderedBMin=state.BMin;_renderedBMax=state.BMax;
        _hasRenderedFrame=true;
    }

    private void UpdatePreviewTransform()
    {
        if(!_hasRenderedFrame||CanvasSurface.ActualWidth<=0||CanvasSurface.ActualHeight<=0)return;
        double width=CanvasSurface.ActualWidth,height=CanvasSurface.ActualHeight;
        if(_kind==DynamicSystemKind.Lyapunov)
        {
            double currentWidth=_state.AMax-_state.AMin,currentHeight=_state.BMax-_state.BMin;
            double renderedWidth=_renderedAMax-_renderedAMin,renderedHeight=_renderedBMax-_renderedBMin;
            if(currentWidth<=0||currentHeight<=0||renderedWidth<=0||renderedHeight<=0)return;
            double currentCenterX=(_state.AMin+_state.AMax)/2,currentCenterY=(_state.BMin+_state.BMax)/2;
            double renderedCenterX=(_renderedAMin+_renderedAMax)/2,renderedCenterY=(_renderedBMin+_renderedBMax)/2;
            _previewScale.ScaleX=renderedWidth/currentWidth;_previewScale.ScaleY=renderedHeight/currentHeight;
            _previewTranslation.X=(renderedCenterX-currentCenterX)/currentWidth*width;
            _previewTranslation.Y=(currentCenterY-renderedCenterY)/currentHeight*height;
            return;
        }
        if(_state.Zoom<=0||_renderedZoom<=0)return;
        var currentSpan=ViewSpans(_state,width,height);
        _previewScale.ScaleX=_previewScale.ScaleY=_state.Zoom/_renderedZoom;
        _previewTranslation.X=(_renderedCenterX-_state.CenterX)/currentSpan.X*width;
        _previewTranslation.Y=(_state.CenterY-_renderedCenterY)/currentSpan.Y*height;
    }
    private void Toggle_OnClick(object sender,RoutedEventArgs e)=>FractalControlPanel.Toggle(ref _controls,ControlsColumn,ControlsHost,ToggleButton,250,Schedule);
    private void Window_OnKeyDown(object sender,KeyEventArgs e){if(e.Key==Key.F11||e.Key==Key.Escape&&_fullscreen){if(!_fullscreen){_oldStyle=WindowStyle;_oldState=WindowState;WindowStyle=WindowStyle.None;WindowState=WindowState.Maximized;}else{WindowStyle=_oldStyle;WindowState=_oldState;}_fullscreen=!_fullscreen;}}
    private void Window_OnClosing(object? sender,System.ComponentModel.CancelEventArgs e){_timer.Stop();EndVisualization();_cts?.Cancel();_cts?.Dispose();_quadraticSearchCts?.Cancel();}

    private static string DisplayName(DynamicSystemKind k)=>k switch{DynamicSystemKind.Lyapunov=>"Экспонента Ляпунова",DynamicSystemKind.Lorenz=>"Аттрактор Лоренца",DynamicSystemKind.Rossler=>"Аттрактор Рёсслера",DynamicSystemKind.LogisticMap=>"Логистическое отображение",DynamicSystemKind.Bifurcation=>"Диаграмма бифуркации",DynamicSystemKind.Henon=>"Карта Хенона",DynamicSystemKind.Ikeda=>"Отображение Икэды",DynamicSystemKind.Attractors2D=>"Странные аттракторы",_=>"Динамическая система"};
    private static string Details(DynamicSystemState s)=>s.Kind switch{DynamicSystemKind.Lyapunov=>$"{s.Pattern} · {s.Iterations} итераций · {s.PaletteName}",DynamicSystemKind.Attractors2D=>$"{Attractor2DDisplayName(Attractor2DRenderer.ParseKind(s.Attractor2DMode))} · {s.Iterations:N0} точек · масштаб {s.Zoom:G5}",_=>$"Масштаб {s.Zoom:G5} · {Math.Max(s.Iterations,s.Steps):N0} итераций"};
    private static string Attractor2DDisplayName(Attractor2DKind kind)=>kind switch{Attractor2DKind.Clifford=>"Клиффорд",Attractor2DKind.PeterDeJong=>"Питер де Йонг",Attractor2DKind.Tinkerbell=>"Tinkerbell",Attractor2DKind.SprottQuadratic=>"Карта Спротта",_=>"Gumowski–Mira"};
    // Видимая область по X и Y в мировых координатах — так же, как её строят движки:
    // Лоренц, Рёсслер и логистические режимы растягивают квадрат на весь кадр, Хенон
    // и странные аттракторы держат пиксели квадратными, Икэда берёт высоту из RangeY.
    private static (double X,double Y) ViewSpans(DynamicSystemState s,double width,double height)
    {
        double zoom=Math.Max(1e-9,s.Zoom),aspect=Math.Max(1,height)/Math.Max(1,width);
        return s.Kind switch
        {
            DynamicSystemKind.Lorenz=>Square((double)FractalLorenzEngine.BaseScale/zoom),
            DynamicSystemKind.Rossler=>Square((double)FractalRosslerEngine.BaseScale/zoom),
            DynamicSystemKind.Henon=>((double)FractalHenonEngine.BaseScale/zoom,(double)FractalHenonEngine.BaseScale/zoom*aspect),
            DynamicSystemKind.Ikeda=>(Math.Max(1e-9,(s.RangeXMax-s.RangeXMin)/zoom),Math.Max(1e-9,(s.RangeYMax-s.RangeYMin)/zoom)),
            DynamicSystemKind.Attractors2D=>(Attractor2DRenderer.GetBaseSpan(Attractor2DRenderer.ParseKind(s.Attractor2DMode),s)/zoom,Attractor2DRenderer.GetBaseSpan(Attractor2DRenderer.ParseKind(s.Attractor2DMode),s)/zoom*aspect),
            _=>Square(1/zoom)
        };
        static (double,double) Square(double span)=>(span,span);
    }
    private static string Format(object? value)=>value switch{double d=>d.ToString("G15",CultureInfo.InvariantCulture),float f=>f.ToString("G9",CultureInfo.InvariantCulture),null=>string.Empty,_=>Convert.ToString(value,CultureInfo.InvariantCulture)??string.Empty};
    private static bool TryDouble(string text,out double value)=>double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out value)||double.TryParse(text,NumberStyles.Float,CultureInfo.CurrentCulture,out value);
    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T:DependencyObject{for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){DependencyObject child=VisualTreeHelper.GetChild(root,i);if(child is T match)yield return match;foreach(T nested in FindVisualChildren<T>(child))yield return nested;}}
    private sealed record ChoiceOption(string Display,string Value)
    {
        public override string ToString()=>Display;
    }
    private readonly record struct DynamicTileRenderEvent(bool IsStart,MandelbrotRenderTile Tile,byte[]? Pixels);
}
