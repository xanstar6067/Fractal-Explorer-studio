using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Models;
using Image = System.Windows.Controls.Image;

namespace FractalExplorerWPF.Views;

/// <summary>Общая сетка атласа: две оси, фиксированные компоненты, история и отменяемые поколения.</summary>
public partial class ParameterAtlasWindow : Window
{
    private readonly SemaphoreSlim _renderGate = new(1, 1);
    private readonly Stack<ParameterAtlasRange> _history = new();
    private readonly List<TextBox> _componentBoxes = [];
    private readonly List<TextBlock> _componentLabels = [];
    private CancellationTokenSource? _renderCts;
    private ParameterAtlasRange _range;
    private string[] _componentNames = [];
    private decimal[] _point = [];
    private decimal[]? _selection;
    private int _xAxis, _yAxis = 1;
    private Button? _selectedButton;
    private bool _closed, _changing, _dirtyFixed;
    public bool IsRendering => _renderCts is not null;
    public event EventHandler? ParametersApplied;
    protected decimal[] SelectedComponents => (decimal[])(_selection ?? _point).Clone();

    protected ParameterAtlasWindow() => InitializeComponent();

    protected virtual int PreviewIterationCount => 250;
    protected virtual decimal ComponentMinimum => decimal.MinValue;
    protected virtual decimal ComponentMaximum => decimal.MaxValue;
    protected virtual decimal RangeMinimum => -10m;
    protected virtual decimal RangeMaximum => 10m;
    protected int XAxisIndex => _xAxis;
    protected int YAxisIndex => _yAxis;
    protected virtual (decimal Minimum, decimal Maximum) ComponentBounds(int index) => (ComponentMinimum, ComponentMaximum);
    protected virtual (decimal Minimum, decimal Maximum) AxisBounds(int index) => (RangeMinimum, RangeMaximum);
    protected virtual decimal InitialRangeWidth(int index) => .4m;
    protected virtual bool IsIntegerComponent(int index) => false;
    protected virtual int IntegerComponentStep(int index) => 1;
    protected virtual ParameterAtlasRange OverviewRange => new(-2m, 2m, -2m, 2m);
    protected virtual Func<decimal[], CancellationToken, byte[]> CreateRenderSnapshot() =>
        throw new NotSupportedException("Не задан рендерер атласа.");

    protected virtual Func<decimal[], CancellationToken, Task<byte[]>> CreateAsyncRenderSnapshot()
    {
        var render = CreateRenderSnapshot();
        return (point, token) => Task.Run(() => render(point, token), token);
    }
    protected virtual string PreviewStatus(int completed, int total, int iterations) =>
        $"{completed} / {total} · ЦП, 1 поток · до {iterations} итераций";
    protected virtual void ReleaseRenderResources() { }

    protected void InitializeAtlas(string title, string[] names, decimal[] point, string hint, int xAxis = 0, int yAxis = 1)
    {
        if (names.Length < 2 || names.Length != point.Length) throw new ArgumentException("Требуется не менее двух компонент.");
        if ((uint)xAxis >= names.Length || (uint)yAxis >= names.Length || xAxis == yAxis)
            throw new ArgumentException("Оси должны указывать на две разные компоненты.");
        _changing = true;
        _xAxis = xAxis; _yAxis = yAxis;
        _componentNames = (string[])names.Clone();
        _point = (decimal[])point.Clone();
        Title = HeaderText.Text = title;
        ApplicationHintText.Text = hint;
        XAxisBox.ItemsSource = names; YAxisBox.ItemsSource = names;
        XAxisBox.SelectedIndex = xAxis; YAxisBox.SelectedIndex = yAxis;
        XAxisBox.Visibility = YAxisBox.Visibility = names.Length > 2 ? Visibility.Visible : Visibility.Collapsed;
        if (names.Length > 2)
        {
            FixedPointPanel.Visibility = Visibility.Visible;
            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                var panel = new StackPanel { Width = 180, Margin = new Thickness(0, 0, 12, 0) };
                var label = new TextBlock { Text = names[i], TextWrapping = TextWrapping.Wrap };
                panel.Children.Add(label); _componentLabels.Add(label);
                var box = new TextBox { Text = Format(point[i]), Margin = new Thickness(0, 4, 0, 0) };
                NumericSpinner.SetIsEnabled(box, true);
                panel.Children.Add(box); FixedPointPanel.Children.Add(panel); _componentBoxes.Add(box);
                var bounds = ComponentBounds(i);
                if (IsIntegerComponent(i))
                    NumericInput.Integer(this, box, names[i], (int)bounds.Minimum, (int)bounds.Maximum,
                        step: IntegerComponentStep(i), active: () => index != _xAxis && index != _yAxis);
                else
                    NumericInput.Decimal(this, box, names[i], bounds.Minimum, bounds.Maximum, active: () => index != _xAxis && index != _yAxis);
                box.TextChanged += (_, _) =>
                {
                    if (_changing || index == _xAxis || index == _yAxis) return;
                    _dirtyFixed = true;
                    InvalidateSelection("Фиксированные параметры изменены. Нажмите «Построить».");
                };
            }
        }
        UpdateAxesUi();
        SetRange(InitialRange());
        RegisterRangeInputs();
        NumericInput.OrderedPair(this, MinRealBox, MaxRealBox);
        NumericInput.OrderedPair(this, MinImaginaryBox, MaxImaginaryBox);
        _changing = false;
        Loaded += async (_, _) => await BuildAtlasAsync();
    }

    protected void RebuildCurrentSourceIfLoaded()
    {
        if (!_closed && IsLoaded && !_dirtyFixed) _ = RenderAtlasAsync(_range, TilesPanel.Columns);
    }

    private void UpdateAxesUi()
    {
        XAxisLabel.Text = $"По горизонтали: {_componentNames[_xAxis]}, от / до";
        YAxisLabel.Text = $"По вертикали: {_componentNames[_yAxis]}, от / до";
        NumericSpinner.SetIsInteger(MinRealBox, IsIntegerComponent(_xAxis));
        NumericSpinner.SetIsInteger(MaxRealBox, IsIntegerComponent(_xAxis));
        NumericSpinner.SetIsInteger(MinImaginaryBox, IsIntegerComponent(_yAxis));
        NumericSpinner.SetIsInteger(MaxImaginaryBox, IsIntegerComponent(_yAxis));
        for (int i = 0; i < _componentBoxes.Count; i++)
        {
            _componentBoxes[i].IsEnabled = i != _xAxis && i != _yAxis;
            _componentLabels[i].Text = _componentNames[i] + (i == _xAxis ? " — ось X" : i == _yAxis ? " — ось Y" : " — фиксировано");
        }
    }

    private void Axes_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_changing || XAxisBox.SelectedIndex < 0 || YAxisBox.SelectedIndex < 0) return;
        NumericInput.CommitAll(this);
        ReadPointFields();
        int x = XAxisBox.SelectedIndex, y = YAxisBox.SelectedIndex;
        if (x == y) { if (ReferenceEquals(sender, XAxisBox)) y = _xAxis; else x = _yAxis; }
        _changing = true;
        _xAxis = x; _yAxis = y;
        XAxisBox.SelectedIndex = x; YAxisBox.SelectedIndex = y;
        UpdateAxesUi();
        _changing = false;
        _history.Clear();
        SetRange(InitialRange());
        InvalidateSelection("Оси изменены. Нажмите «Построить».");
        if (IsLoaded) _ = BuildAtlasAsync();
    }

    private void ReadPointFields()
    {
        for (int i = 0; i < _componentBoxes.Count; i++) _point[i] = Parse(_componentBoxes[i]);
        if (_dirtyFixed) _history.Clear();
        _dirtyFixed = false;
    }

    private void InvalidateSelection(string message)
    {
        _renderCts?.Cancel(); _renderCts = null;
        _selection = null; _selectedButton = null;
        TilesPanel.Children.Clear(); SelectedPreview.Source = null;
        ApplyButton.IsEnabled = NarrowButton.IsEnabled = CancelAtlasButton.IsEnabled = false;
        BackButton.IsEnabled = false;
        SelectedText.Text = "Выберите ячейку"; StatusText.Text = message;
    }

    private void SetRange(ParameterAtlasRange range)
    {
        _range = range;
        MinRealBox.Text = Format(range.MinX); MaxRealBox.Text = Format(range.MaxX);
        MinImaginaryBox.Text = Format(range.MinY); MaxImaginaryBox.Text = Format(range.MaxY);
    }

    private ParameterAtlasRange InitialRange() => RangeAround(_point[_xAxis], _point[_yAxis],
        InitialRangeWidth(_xAxis), InitialRangeWidth(_yAxis));

    private ParameterAtlasRange RangeAround(decimal xValue, decimal yValue, decimal width, decimal height)
    {
        var x = AxisBounds(_xAxis); var y = AxisBounds(_yAxis);
        return BoundedRange(ParameterAtlasRange.Around(xValue, yValue, width, height, x.Minimum, x.Maximum, y.Minimum, y.Maximum));
    }

    private void RegisterRangeInputs()
    {
        NumericInput.Decimal(this, MinRealBox, "минимум оси X", () => AxisBounds(_xAxis).Minimum, () => AxisBounds(_xAxis).Maximum, integer: () => IsIntegerComponent(_xAxis));
        NumericInput.Decimal(this, MaxRealBox, "максимум оси X", () => AxisBounds(_xAxis).Minimum, () => AxisBounds(_xAxis).Maximum, integer: () => IsIntegerComponent(_xAxis));
        NumericInput.Decimal(this, MinImaginaryBox, "минимум оси Y", () => AxisBounds(_yAxis).Minimum, () => AxisBounds(_yAxis).Maximum, integer: () => IsIntegerComponent(_yAxis));
        NumericInput.Decimal(this, MaxImaginaryBox, "максимум оси Y", () => AxisBounds(_yAxis).Minimum, () => AxisBounds(_yAxis).Maximum, integer: () => IsIntegerComponent(_yAxis));
    }

    private ParameterAtlasRange BoundedRange(ParameterAtlasRange range)
    {
        var x = AxisBounds(_xAxis); var y = AxisBounds(_yAxis);
        return new(Math.Max(x.Minimum, IsIntegerComponent(_xAxis) ? decimal.Floor(range.MinX) : range.MinX),
            Math.Min(x.Maximum, IsIntegerComponent(_xAxis) ? decimal.Ceiling(range.MaxX) : range.MaxX),
            Math.Max(y.Minimum, IsIntegerComponent(_yAxis) ? decimal.Floor(range.MinY) : range.MinY),
            Math.Min(y.Maximum, IsIntegerComponent(_yAxis) ? decimal.Ceiling(range.MaxY) : range.MaxY));
    }

    private decimal[] SampleAxis(int index, decimal minimum, decimal maximum, int size, bool reverse = false)
    {
        bool integer = IsIntegerComponent(index);
        int step = IntegerComponentStep(index);
        if (integer && step > 1)
        {
            decimal origin = ComponentBounds(index).Minimum;
            decimal first = origin + decimal.Ceiling((minimum - origin) / step) * step;
            decimal last = origin + decimal.Floor((maximum - origin) / step) * step;
            if (first > last) return [];
            int length = (int)Math.Min(size, (last - first) / step + 1);
            var samples = new decimal[length];
            for (int i = 0; i < length; i++)
                samples[reverse ? length - 1 - i : i] = length == 1 ? first : first + step *
                    decimal.Round((last - first) / step * i / (length - 1), 0, MidpointRounding.AwayFromZero);
            return samples;
        }
        int count = integer ? (int)Math.Min(size, maximum - minimum + 1) : size;
        var values = new decimal[count];
        for (int i = 0; i < count; i++)
        {
            decimal value = minimum + (maximum - minimum) * i / (count - 1);
            values[reverse ? count - 1 - i : i] = integer ? decimal.Round(value, 0, MidpointRounding.AwayFromZero) : value;
        }
        return values;
    }

    protected Task BuildAtlasAsync()
    {
        if (_closed) return Task.CompletedTask;
        NumericInput.CommitAll(this);
        ReadPointFields();
        var range = new ParameterAtlasRange(Parse(MinRealBox), Parse(MaxRealBox), Parse(MinImaginaryBox), Parse(MaxImaginaryBox));
        if (!range.IsValid || range != BoundedRange(range))
        { StatusText.Text = "Укажите возрастающие диапазоны в пределах выбранных параметров."; return Task.CompletedTask; }
        int size = int.Parse((string)((ComboBoxItem)GridSizeBox.SelectedItem).Tag, CultureInfo.InvariantCulture);
        return RenderAtlasAsync(range, size);
    }

    protected async Task RenderAtlasAsync(ParameterAtlasRange range, int size)
    {
        if (_closed) return;
        var render = CreateAsyncRenderSnapshot();
        var point = (decimal[])_point.Clone();
        int xAxis = _xAxis, yAxis = _yAxis, iterations = PreviewIterationCount;
        var xValues = SampleAxis(xAxis, range.MinX, range.MaxX, size);
        var yValues = SampleAxis(yAxis, range.MinY, range.MaxY, size, reverse: true);
        if (xValues.Length == 0 || yValues.Length == 0)
        { InvalidateSelection("В диапазоне нет допустимых значений выбранной оси. Расширьте диапазон."); return; }
        int columns = xValues.Length, rows = yValues.Length, total = columns * rows;
        if (range != _range) { _history.Push(_range); _range = range; }
        _renderCts?.Cancel();
        using var cts = new CancellationTokenSource(); _renderCts = cts;
        TilesPanel.Children.Clear(); TilesPanel.Columns = columns;
        _selection = null; _selectedButton = null; SelectedPreview.Source = null;
        SelectedText.Text = "Выберите ячейку";
        ApplyButton.IsEnabled = NarrowButton.IsEnabled = false;
        BackButton.IsEnabled = _history.Count > 0;
        HorizontalRangeText.Text = $"{_componentNames[xAxis]} →   {range.MinX:G8} … {range.MaxX:G8}";
        VerticalRangeText.Text = $"{_componentNames[yAxis]} ↑   {range.MinY:G8} … {range.MaxY:G8}";
        CancelAtlasButton.IsEnabled = true; StatusText.Text = $"Подготовка {total} вариантов…";
        bool entered = false;
        try
        {
            var tiles = new List<(Button Button, Image Image, decimal[] Point, int Row, int Column)>();
            for (int row = 0; row < rows; row++)
            for (int column = 0; column < columns; column++)
            {
                var (x, y) = (xValues[column], yValues[row]);
                var sample = (decimal[])point.Clone(); sample[xAxis] = x; sample[yAxis] = y;
                var image = new Image { Height = size == 7 ? 68 : 92, Stretch = Stretch.Uniform };
                var content = new StackPanel(); content.Children.Add(image);
                content.Children.Add(new TextBlock { Text = $"{x:G5}; {y:G5}", FontSize = 11,
                    TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center });
                var button = new Button { Content = content, Margin = new Thickness(3), Padding = new Thickness(3),
                    BorderThickness = new Thickness(2), IsEnabled = false, ToolTip = Describe(sample) + "\nДвойной щелчок — применить" };
                button.Click += (_, _) => SelectTile(button, image, sample);
                button.MouseDoubleClick += (_, e) => { SelectTile(button, image, sample); ApplySelection(); e.Handled = true; };
                TilesPanel.Children.Add(button); tiles.Add((button, image, sample, row, column));
            }
            await _renderGate.WaitAsync(cts.Token); entered = true;
            int done = 0;
            foreach (var tile in tiles.OrderBy(t => Math.Abs(t.Row - rows / 2) + Math.Abs(t.Column - columns / 2)))
            {
                byte[] pixels = await render(tile.Point, cts.Token);
                if (cts.IsCancellationRequested || _closed || !ReferenceEquals(_renderCts, cts)) return;
                var bitmap = BitmapSource.Create(144, 108, 96, 96, PixelFormats.Bgra32, null, pixels, 144 * 4);
                bitmap.Freeze(); tile.Image.Source = bitmap; tile.Button.IsEnabled = true; done++;
                if (_selectedButton is null) SelectTile(tile.Button, tile.Image, tile.Point);
                StatusText.Text = PreviewStatus(done, total, iterations);
            }
        }
        catch (OperationCanceledException)
        {
            if (!_closed && ReferenceEquals(_renderCts, cts)) StatusText.Text = "Расчёт остановлен. Готовые ячейки доступны для выбора.";
        }
        catch (Exception ex)
        {
            if (!_closed && ReferenceEquals(_renderCts, cts)) StatusText.Text = $"Ошибка атласа: {ex.Message}";
        }
        finally
        {
            if (entered) _renderGate.Release();
            if (ReferenceEquals(_renderCts, cts)) { _renderCts = null; CancelAtlasButton.IsEnabled = false; }
        }
    }

    private string Describe(decimal[] point) => string.Join("\n", point.Select((value, i) => $"{_componentNames[i]} = {value:G29}"));
    private void SelectTile(Button button, Image image, decimal[] point)
    {
        _selectedButton?.ClearValue(BorderBrushProperty); _selectedButton = button;
        button.SetResourceReference(BorderBrushProperty, "Theme.InteractiveHoverBrush");
        _selection = (decimal[])point.Clone();
        _point[_xAxis] = point[_xAxis]; _point[_yAxis] = point[_yAxis];
        _changing = true;
        if (_componentBoxes.Count > 0)
        { _componentBoxes[_xAxis].Text = Format(point[_xAxis]); _componentBoxes[_yAxis].Text = Format(point[_yAxis]); }
        _changing = false;
        SelectedPreview.Source = image.Source; SelectedText.Text = Describe(point);
        ApplyButton.IsEnabled = NarrowButton.IsEnabled = true;
    }

    private void ApplySelection()
    {
        NumericInput.CommitAll(this);
        if (!_closed && _selection is not null) ParametersApplied?.Invoke(this, EventArgs.Empty);
    }
    private async void Build_OnClick(object sender, RoutedEventArgs e) => await BuildAtlasAsync();
    private void Apply_OnClick(object sender, RoutedEventArgs e) => ApplySelection();
    private void Cancel_OnClick(object sender, RoutedEventArgs e) => _renderCts?.Cancel();
    private async void Narrow_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selection is null) return;
        var next = RangeAround(_selection[_xAxis], _selection[_yAxis], (_range.MaxX - _range.MinX) / 2, (_range.MaxY - _range.MinY) / 2);
        if (!next.IsValid || next == _range) { StatusText.Text = "Достигнут предел точности диапазона."; return; }
        _history.Push(_range); SetRange(next); await BuildAtlasAsync();
    }
    private async void Overview_OnClick(object sender, RoutedEventArgs e)
    { var next = OverviewRange; if (_range != next) _history.Push(_range); SetRange(next); await BuildAtlasAsync(); }
    private async void Back_OnClick(object sender, RoutedEventArgs e)
    { if (!_history.TryPop(out var range)) return; SetRange(range); await BuildAtlasAsync(); }
    private static string Format(decimal value) => value.ToString(CultureInfo.InvariantCulture);
    private static decimal Parse(TextBox box) => decimal.Parse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture);
    private async void Window_OnClosed(object? sender, EventArgs e)
    {
        _closed = true; _renderCts?.Cancel();
        await _renderGate.WaitAsync();
        try { ReleaseRenderResources(); }
        finally { _renderGate.Release(); }
    }
}
