using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Models;
using Image = System.Windows.Controls.Image;

namespace FractalExplorerWPF.Views;

public partial class JuliaParameterAtlasWindow : Window
{
    private readonly MandelbrotState _source;
    private readonly SemaphoreSlim _renderGate = new(1, 1);
    private readonly Stack<ParameterAtlasRange> _history = new();
    private CancellationTokenSource? _renderCts;
    private ParameterAtlasRange _range;
    private Button? _selectedButton;
    private bool _closed;
    public decimal SelectedReal { get; private set; }
    public decimal SelectedImaginary { get; private set; }
    public event EventHandler? ConstantApplied;

    public JuliaParameterAtlasWindow(MandelbrotState source)
    {
        if (source.Variant != MandelbrotVariant.Julia)
            throw new ArgumentException("Атлас доступен только для классического Жюлиа.", nameof(source));
        _source = source.Clone();
        SelectedReal = source.JuliaCReal;
        SelectedImaginary = source.JuliaCImaginary;
        InitializeComponent();
        SetRange(ParameterAtlasRange.Around(SelectedReal, SelectedImaginary, 0.4m, 0.4m));
        NumericInput.Decimal(this, MinRealBox, "минимум Re(C)", -10m, 10m);
        NumericInput.Decimal(this, MaxRealBox, "максимум Re(C)", -10m, 10m);
        NumericInput.Decimal(this, MinImaginaryBox, "минимум Im(C)", -10m, 10m);
        NumericInput.Decimal(this, MaxImaginaryBox, "максимум Im(C)", -10m, 10m);
        NumericInput.OrderedPair(this, MinRealBox, MaxRealBox);
        NumericInput.OrderedPair(this, MinImaginaryBox, MaxImaginaryBox);
        Loaded += async (_, _) => await BuildAtlasAsync();
    }

    private void SetRange(ParameterAtlasRange range)
    {
        _range = range;
        MinRealBox.Text = range.MinX.ToString(CultureInfo.InvariantCulture);
        MaxRealBox.Text = range.MaxX.ToString(CultureInfo.InvariantCulture);
        MinImaginaryBox.Text = range.MinY.ToString(CultureInfo.InvariantCulture);
        MaxImaginaryBox.Text = range.MaxY.ToString(CultureInfo.InvariantCulture);
    }

    private async Task BuildAtlasAsync()
    {
        if (_closed) return;
        NumericInput.CommitAll(this);
        var range = new ParameterAtlasRange(Parse(MinRealBox), Parse(MaxRealBox), Parse(MinImaginaryBox), Parse(MaxImaginaryBox));
        if (!range.IsValid) { StatusText.Text = "Укажите возрастающие диапазоны от −10 до 10."; return; }
        if (range != _range) { _history.Push(_range); _range = range; }
        _renderCts?.Cancel();
        using var cts = new CancellationTokenSource();
        _renderCts = cts;
        int size = int.Parse((string)((ComboBoxItem)GridSizeBox.SelectedItem).Tag, CultureInfo.InvariantCulture);
        TilesPanel.Children.Clear();
        TilesPanel.Columns = size;
        _selectedButton = null;
        SelectedPreview.Source = null;
        SelectedText.Text = "Выберите ячейку";
        ApplyButton.IsEnabled = NarrowButton.IsEnabled = false;
        BackButton.IsEnabled = _history.Count > 0;
        HorizontalRangeText.Text = $"Re(C) →   {range.MinX:G8} … {range.MaxX:G8}";
        VerticalRangeText.Text = $"Im(C) ↑   {range.MinY:G8} … {range.MaxY:G8}";
        CancelAtlasButton.IsEnabled = true;
        StatusText.Text = $"Подготовка {size * size} вариантов…";
        bool entered = false;
        try
        {
            var tiles = new List<(Button Button, Image Image, decimal Real, decimal Imaginary, int Row, int Column)>();
            for (int row = 0; row < size; row++)
            for (int column = 0; column < size; column++)
            {
                var (real, imaginary) = range.Sample(column, row, size);
                var image = new Image { Height = size == 7 ? 68 : 92, Stretch = Stretch.Uniform };
                var content = new StackPanel();
                content.Children.Add(image);
                content.Children.Add(new TextBlock { Text = $"{real:G5}; {imaginary:G5}i", FontSize = 11,
                    TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center });
                var button = new Button { Content = content, Margin = new Thickness(3), Padding = new Thickness(3),
                    BorderThickness = new Thickness(2), IsEnabled = false,
                    ToolTip = $"Re(C) = {real}\nIm(C) = {imaginary}\nДвойной щелчок — применить" };
                button.Click += (_, _) => SelectTile(button, image, real, imaginary);
                button.MouseDoubleClick += (_, e) => { SelectTile(button, image, real, imaginary); ApplySelection(); e.Handled = true; };
                TilesPanel.Children.Add(button);
                tiles.Add((button, image, real, imaginary, row, column));
            }
            // Даже при частом перестроении одновременно считается только одна миниатюра.
            await _renderGate.WaitAsync(cts.Token);
            entered = true;
            int done = 0;
            // Сначала центр диапазона, затем соседние варианты.
            foreach (var tile in tiles.OrderBy(t => Math.Abs(t.Row - size / 2) + Math.Abs(t.Column - size / 2)))
            {
                cts.Token.ThrowIfCancellationRequested();
                byte[] pixels = await Task.Run(() => JuliaParameterAtlasRenderer.Render(_source, tile.Real, tile.Imaginary, 144, 108, cts.Token), cts.Token);
                if (cts.IsCancellationRequested || _closed || !ReferenceEquals(_renderCts, cts)) return;
                var bitmap = BitmapSource.Create(144, 108, 96, 96, PixelFormats.Bgra32, null, pixels, 144 * 4);
                bitmap.Freeze();
                tile.Image.Source = bitmap;
                tile.Button.IsEnabled = true;
                done++;
                if (_selectedButton is null) SelectTile(tile.Button, tile.Image, tile.Real, tile.Imaginary);
                StatusText.Text = $"{done} / {size * size} · ЦП, 1 поток · до {Math.Min(_source.Iterations, JuliaParameterAtlasRenderer.PreviewIterations)} итераций";
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

    private void SelectTile(Button button, Image image, decimal real, decimal imaginary)
    {
        _selectedButton?.ClearValue(BorderBrushProperty);
        _selectedButton = button;
        button.SetResourceReference(BorderBrushProperty, "Theme.InteractiveHoverBrush");
        SelectedReal = real;
        SelectedImaginary = imaginary;
        SelectedPreview.Source = image.Source;
        SelectedText.Text = $"Re(C) = {real:G29}\nIm(C) = {imaginary:G29}";
        ApplyButton.IsEnabled = NarrowButton.IsEnabled = true;
    }

    private void ApplySelection()
    {
        if (_closed || _selectedButton is null) return;
        NumericInput.CommitAll(this);
        ConstantApplied?.Invoke(this, EventArgs.Empty);
    }

    private async void Build_OnClick(object sender, RoutedEventArgs e) => await BuildAtlasAsync();
    private void Apply_OnClick(object sender, RoutedEventArgs e) => ApplySelection();
    private void Cancel_OnClick(object sender, RoutedEventArgs e) => _renderCts?.Cancel();
    private async void Narrow_OnClick(object sender, RoutedEventArgs e)
    {
        var next = ParameterAtlasRange.Around(SelectedReal, SelectedImaginary, (_range.MaxX - _range.MinX) / 2, (_range.MaxY - _range.MinY) / 2);
        if (!next.IsValid || next == _range) { StatusText.Text = "Достигнут предел точности диапазона."; return; }
        _history.Push(_range);
        SetRange(next);
        await BuildAtlasAsync();
    }
    private async void Overview_OnClick(object sender, RoutedEventArgs e)
    {
        var next = new ParameterAtlasRange(-2m, 1m, -1.2m, 1.2m);
        if (_range != next) _history.Push(_range);
        SetRange(next);
        await BuildAtlasAsync();
    }
    private async void Back_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_history.TryPop(out var range)) return;
        SetRange(range);
        await BuildAtlasAsync();
    }
    private static decimal Parse(TextBox box) => decimal.Parse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture);
    private void Window_OnClosed(object? sender, EventArgs e) { _closed = true; _renderCts?.Cancel(); }
}
