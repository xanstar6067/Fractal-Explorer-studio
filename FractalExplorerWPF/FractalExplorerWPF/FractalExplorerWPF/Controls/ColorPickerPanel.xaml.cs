using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Infrastructure.ColorPicking;
using Color = System.Windows.Media.Color;
using MediaBrushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;

namespace FractalExplorerWPF.Controls;

/// <summary>A reusable WPF color editor with palettes, HSL/RGBA controls and a screen eyedropper.</summary>
public partial class ColorPickerPanel : UserControl
{
    private const int CustomColorCount = 16;
    private readonly ScreenEyedropper _eyedropper = new();
    private readonly Color?[] _customColors = new Color?[CustomColorCount];
    private readonly List<Button> _customButtons = [];
    private Color _originalColor = Colors.White;
    private Color _selectedColor = Colors.White;
    private double _hue;
    private double _saturation;
    private double _lightness;
    private bool _updating;
    private bool _initialized;

    public event EventHandler? SelectedColorChanged;

    public Color SelectedColor => _selectedColor;

    public ColorPickerPanel()
    {
        InitializeComponent();
        BuildStandardPalette();
        LoadCustomColors();
        BuildCustomPalette();
    }

    public void Initialize(Color initialColor)
    {
        _originalColor = initialColor;
        _initialized = true;
        ApplySelectedColor(initialColor);
    }

    private void ApplySelectedColor(Color color, bool preserveHue = false)
    {
        _selectedColor = color;
        double previousHue = _hue;
        RgbToHsl(color, out _hue, out _saturation, out _lightness);
        if (preserveHue) _hue = previousHue;

        _updating = true;
        try
        {
            RedSlider.Value = color.R;
            GreenSlider.Value = color.G;
            BlueSlider.Value = color.B;
            AlphaSlider.Value = color.A;
            RedValue.Text = color.R.ToString(CultureInfo.InvariantCulture);
            GreenValue.Text = color.G.ToString(CultureInfo.InvariantCulture);
            BlueValue.Text = color.B.ToString(CultureInfo.InvariantCulture);
            AlphaValue.Text = color.A.ToString(CultureInfo.InvariantCulture);
            HexBox.Text = ToHex(color);
        }
        finally
        {
            _updating = false;
        }

        OriginalPreview.Background = new SolidColorBrush(_originalColor);
        SelectedPreview.Background = new SolidColorBrush(_selectedColor);
        OriginalHex.Text = ToHex(_originalColor);
        SelectedHex.Text = ToHex(_selectedColor);
        OriginalHex.Foreground = ContrastBrush(_originalColor);
        SelectedHex.Foreground = ContrastBrush(_selectedColor);
        OriginalLabel.Foreground = OriginalHex.Foreground;
        SelectedLabel.Foreground = SelectedHex.Foreground;
        UpdateColorMatrix();
        UpdateMarkers();
        SelectedColorChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ChannelSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized || _updating) return;
        ApplySelectedColor(Color.FromArgb(
            (byte)Math.Round(AlphaSlider.Value),
            (byte)Math.Round(RedSlider.Value),
            (byte)Math.Round(GreenSlider.Value),
            (byte)Math.Round(BlueSlider.Value)));
    }

    private void ColorMatrix_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_initialized) return;
        UpdateColorMatrix();
        UpdateMarkers();
    }

    private void ColorMatrix_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ColorMatrix.CaptureMouse();
        UpdateFromMatrix(e.GetPosition(ColorMatrix));
    }

    private void ColorMatrix_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) UpdateFromMatrix(e.GetPosition(ColorMatrix));
        else if (ColorMatrix.IsMouseCaptured) ColorMatrix.ReleaseMouseCapture();
    }

    private void UpdateFromMatrix(Point point)
    {
        if (ColorMatrix.ActualWidth <= 1 || ColorMatrix.ActualHeight <= 1) return;
        _saturation = Math.Clamp(point.X / ColorMatrix.ActualWidth, 0, 1);
        _lightness = Math.Clamp(1 - point.Y / ColorMatrix.ActualHeight, 0, 1);
        ApplySelectedColor(FromHsl(_selectedColor.A, _hue, _saturation, _lightness), preserveHue: true);
    }

    private void HueSlider_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        HueSlider.CaptureMouse();
        UpdateHue(e.GetPosition(HueSlider).Y);
    }

    private void HueSlider_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) UpdateHue(e.GetPosition(HueSlider).Y);
        else if (HueSlider.IsMouseCaptured) HueSlider.ReleaseMouseCapture();
    }

    private void UpdateHue(double y)
    {
        if (HueSlider.ActualHeight <= 1) return;
        _hue = (360 - Math.Clamp(y / HueSlider.ActualHeight, 0, 1) * 360) % 360;
        ApplySelectedColor(FromHsl(_selectedColor.A, _hue, _saturation, _lightness), preserveHue: true);
    }

    private void UpdateColorMatrix()
    {
        int width = Math.Max(1, (int)Math.Round(ColorMatrix.ActualWidth));
        int height = Math.Max(1, (int)Math.Round(ColorMatrix.ActualHeight));
        if (width <= 1 || height <= 1) return;

        int stride = width * 4;
        byte[] pixels = new byte[stride * height];
        for (int y = 0; y < height; y++)
        {
            double lightness = 1 - y / (double)(height - 1);
            for (int x = 0; x < width; x++)
            {
                Color color = FromHsl(255, _hue, x / (double)(width - 1), lightness);
                int offset = y * stride + x * 4;
                pixels[offset] = color.B;
                pixels[offset + 1] = color.G;
                pixels[offset + 2] = color.R;
                pixels[offset + 3] = 255;
            }
        }

        var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
        ColorMatrixImage.Source = bitmap;
    }

    private void UpdateMarkers()
    {
        Canvas.SetLeft(MatrixMarker, _saturation * Math.Max(0, ColorMatrix.ActualWidth - 1) - MatrixMarker.Width / 2);
        Canvas.SetTop(MatrixMarker, (1 - _lightness) * Math.Max(0, ColorMatrix.ActualHeight - 1) - MatrixMarker.Height / 2);
        Canvas.SetLeft(HueMarker, 0);
        Canvas.SetTop(HueMarker, (1 - _hue / 360) * Math.Max(0, HueSlider.ActualHeight - 1) - HueMarker.Height / 2);
    }

    private void Eyedropper_OnClick(object sender, RoutedEventArgs e)
    {
        Window? window = Window.GetWindow(this);
        if (window is null) return;
        double previousOpacity = window.Opacity;
        window.Opacity = 0;
        window.IsHitTestVisible = false;
        try
        {
            if (_eyedropper.TryPickColor(window, out Color color))
                ApplySelectedColor(Color.FromArgb(_selectedColor.A, color.R, color.G, color.B));
        }
        finally
        {
            window.Opacity = previousOpacity;
            window.IsHitTestVisible = true;
            window.Activate();
        }
    }

    private void ApplyHex_OnClick(object sender, RoutedEventArgs e) => ApplyHex();

    private void HexBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        ApplyHex();
    }

    private void ApplyHex()
    {
        if (TryParseHex(HexBox.Text, out Color color))
        {
            HexBox.ClearValue(ForegroundProperty);
            ApplySelectedColor(color);
        }
        else
        {
            HexBox.Foreground = MediaBrushes.Firebrick;
        }
    }

    private void CopyHex_OnClick(object sender, RoutedEventArgs e) => Clipboard.SetText(ToHex(_selectedColor));

    private void Transparent_OnClick(object sender, RoutedEventArgs e) =>
        ApplySelectedColor(Color.FromArgb(0, _selectedColor.R, _selectedColor.G, _selectedColor.B));

    private void BuildStandardPalette()
    {
        foreach ((Color color, string name) in StandardColors)
        {
            Button button = CreatePaletteButton(color, name);
            button.Click += (_, _) => ApplySelectedColor(color);
            StandardPalette.Children.Add(button);
        }
    }

    private void BuildCustomPalette()
    {
        CustomPalette.Children.Clear();
        _customButtons.Clear();
        for (int index = 0; index < CustomColorCount; index++)
        {
            int capturedIndex = index;
            Button button = CreatePaletteButton(_customColors[index], "ЛКМ — выбрать или заполнить; ПКМ — очистить");
            button.Click += (_, _) => SelectOrFillCustomColor(capturedIndex);
            button.PreviewMouseRightButtonUp += (_, args) =>
            {
                args.Handled = true;
                _customColors[capturedIndex] = null;
                RefreshCustomButton(capturedIndex);
                SaveCustomColors();
            };
            _customButtons.Add(button);
            CustomPalette.Children.Add(button);
        }
    }

    private static Button CreatePaletteButton(Color? color, string tooltip)
    {
        var button = new Button { Margin = new Thickness(1), Padding = new Thickness(1), MinWidth = 20, MinHeight = 24, ToolTip = tooltip };
        SetPaletteButtonContent(button, color);
        return button;
    }

    private static void SetPaletteButtonContent(Button button, Color? color)
    {
        button.Content = color is Color value
            ? new Border { Background = new SolidColorBrush(value), MinWidth = 16, MinHeight = 18, BorderBrush = MediaBrushes.Gray, BorderThickness = new Thickness(1) }
            : new TextBlock { Text = "×", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.55 };
    }

    private void SelectOrFillCustomColor(int index)
    {
        if (_customColors[index] is Color color)
        {
            ApplySelectedColor(color);
            return;
        }
        _customColors[index] = _selectedColor;
        RefreshCustomButton(index);
        SaveCustomColors();
    }

    private void AddCustomColor_OnClick(object sender, RoutedEventArgs e)
    {
        int index = Array.FindIndex(_customColors, color => color is null);
        if (index < 0) index = 0;
        _customColors[index] = _selectedColor;
        RefreshCustomButton(index);
        SaveCustomColors();
    }

    private void RefreshCustomButton(int index) => SetPaletteButtonContent(_customButtons[index], _customColors[index]);

    private void LoadCustomColors()
    {
        try
        {
            string path = AppPaths.GetSettingsFile("color_picker_custom_colors.json");
            if (!File.Exists(path)) return;
            List<string?>? values = JsonSerializer.Deserialize<List<string?>>(File.ReadAllText(path));
            if (values is null) return;
            for (int index = 0; index < Math.Min(values.Count, CustomColorCount); index++)
                if (values[index] is string text && TryParseHex(text, out Color color)) _customColors[index] = color;
        }
        catch { }
    }

    private void SaveCustomColors()
    {
        try
        {
            string path = AppPaths.EnsureDirectoryFor(AppPaths.GetSettingsFile("color_picker_custom_colors.json"));
            File.WriteAllText(path, JsonSerializer.Serialize(_customColors.Select(color => color is Color value ? ToHex(value) : null)));
        }
        catch { }
    }

    private static string ToHex(Color color) => color.A == 255
        ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
        : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    private static bool TryParseHex(string? text, out Color color)
    {
        color = Colors.Transparent;
        string value = text?.Trim().TrimStart('#') ?? string.Empty;
        if (value.Length == 6) value = "FF" + value;
        if (value.Length != 8 || !uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint argb)) return false;
        color = Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
        return true;
    }

    private static System.Windows.Media.Brush ContrastBrush(Color color)
    {
        double luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) * color.A / 255;
        return luminance > 150 ? MediaBrushes.Black : MediaBrushes.White;
    }

    private static void RgbToHsl(Color color, out double hue, out double saturation, out double lightness)
    {
        double red = color.R / 255d;
        double green = color.G / 255d;
        double blue = color.B / 255d;
        double max = Math.Max(red, Math.Max(green, blue));
        double min = Math.Min(red, Math.Min(green, blue));
        double delta = max - min;
        lightness = (max + min) / 2;
        if (delta < 1e-9)
        {
            hue = 0;
            saturation = 0;
            return;
        }
        saturation = delta / (1 - Math.Abs(2 * lightness - 1));
        hue = max == red
            ? 60 * (((green - blue) / delta) % 6)
            : max == green
                ? 60 * ((blue - red) / delta + 2)
                : 60 * ((red - green) / delta + 4);
        if (hue < 0) hue += 360;
    }

    private static Color FromHsl(byte alpha, double hue, double saturation, double lightness)
    {
        double chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        double sector = hue / 60;
        double x = chroma * (1 - Math.Abs(sector % 2 - 1));
        (double red, double green, double blue) = sector switch
        {
            < 1 => (chroma, x, 0d),
            < 2 => (x, chroma, 0d),
            < 3 => (0d, chroma, x),
            < 4 => (0d, x, chroma),
            < 5 => (x, 0d, chroma),
            _ => (chroma, 0d, x)
        };
        double match = lightness - chroma / 2;
        return Color.FromArgb(alpha,
            (byte)Math.Clamp(Math.Round((red + match) * 255), 0, 255),
            (byte)Math.Clamp(Math.Round((green + match) * 255), 0, 255),
            (byte)Math.Clamp(Math.Round((blue + match) * 255), 0, 255));
    }

    private static readonly (Color Color, string Name)[] StandardColors =
    [
        (Color.FromRgb(255,128,128), "Светло-красный"), (Color.FromRgb(255,255,128), "Светло-жёлтый"), (Color.FromRgb(128,255,128), "Светло-зелёный"), (Color.FromRgb(0,255,128), "Аквамариновый"),
        (Color.FromRgb(128,255,255), "Светло-бирюзовый"), (Color.FromRgb(0,128,255), "Лазурный"), (Color.FromRgb(255,128,192), "Светло-розовый"), (Color.FromRgb(255,128,255), "Светло-пурпурный"),
        (Colors.Red, "Красный"), (Colors.Yellow, "Жёлтый"), (Color.FromRgb(128,255,0), "Салатовый"), (Color.FromRgb(0,255,64), "Изумрудный"),
        (Colors.Cyan, "Бирюзовый"), (Color.FromRgb(0,128,192), "Сине-бирюзовый"), (Color.FromRgb(128,128,192), "Серо-голубой"), (Colors.Magenta, "Пурпурный"),
        (Color.FromRgb(128,64,64), "Коричнево-красный"), (Color.FromRgb(255,128,64), "Светло-оранжевый"), (Colors.Lime, "Зелёный"), (Colors.Teal, "Тёмно-бирюзовый"),
        (Color.FromRgb(0,64,128), "Тёмно-лазурный"), (Color.FromRgb(128,128,255), "Светло-синий"), (Color.FromRgb(128,0,64), "Тёмно-розовый"), (Colors.DeepPink, "Розовый"),
        (Colors.Maroon, "Бордовый"), (Colors.Orange, "Оранжевый"), (Colors.Green, "Тёмно-зелёный"), (Color.FromRgb(0,128,64), "Хвойный"),
        (Colors.Blue, "Синий"), (Color.FromRgb(0,0,160), "Индиго"), (Colors.Purple, "Фиолетовый"), (Color.FromRgb(128,0,255), "Ярко-фиолетовый"),
        (Color.FromRgb(64,0,0), "Очень тёмно-красный"), (Color.FromRgb(128,64,0), "Тёмно-коричневый"), (Colors.DarkGreen, "Тёмно-зелёный"), (Color.FromRgb(0,64,64), "Тёмный морской"),
        (Colors.Navy, "Тёмно-синий"), (Color.FromRgb(0,0,64), "Ночной синий"), (Color.FromRgb(64,0,64), "Тёмно-пурпурный"), (Color.FromRgb(64,0,128), "Индиго-фиолетовый"),
        (Colors.Black, "Чёрный"), (Color.FromRgb(64,64,64), "Тёмно-серый"), (Colors.Gray, "Серый"), (Colors.Silver, "Серебристый"),
        (Colors.White, "Белый"), (Colors.LightYellow, "Светло-жёлтый"), (Colors.Moccasin, "Кремовый"), (Colors.Gold, "Золотой")
    ];
}
