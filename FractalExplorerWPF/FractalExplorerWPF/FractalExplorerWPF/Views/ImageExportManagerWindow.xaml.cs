using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Infrastructure;
using Microsoft.Win32;

namespace FractalExplorerWPF.Views;

public partial class ImageExportManagerWindow : Window
{
    private static readonly (string Label, double Value)[] AllSsaaFactors =
    [
        ("Выкл. (1×)", 1), ("Низкое (2×)", 2), ("Высокое (4×)", 4),
        ("Ультра (8×)", 8), ("Экстрим (10×)", 10)
    ];

    private static readonly (string Label, double Value)[] BicubicFactors =
    [
        ("1,1× — минимальное", 1.1), ("1,2× — очень мягкое", 1.2),
        ("1,3× — мягкое", 1.3), ("1,4× — умеренное", 1.4),
        ("1,5× — стандартное", 1.5), ("2× — сильное", 2),
        ("2,5× — экстремальное", 2.5)
    ];

    private static readonly (string Label, double Value)[] LanczosFactors =
    [
        ("4× — суперсемплинг Ultra", 4), ("2× — суперсемплинг High", 2),
        ("1,5× — суперсемплинг Medium", 1.5), ("0,75× — апскейл Quality", 0.75),
        ("0,5× — апскейл Balanced", 0.5), ("0,25× — апскейл Performance", 0.25)
    ];

    private readonly ImageExportConfiguration _configuration;
    private readonly DispatcherTimer _elapsedTimer;
    private readonly Stopwatch _stopwatch = new();
    private CancellationTokenSource? _cts;
    private bool _isRendering;
    private bool _closeWhenIdle;
    private bool _loading;
    private readonly Queue<(double Seconds, int Percentage)> _renderSamples = new();
    private bool _estimatingRender;
    private int _renderPercentage;
    private ImageExportProcessingMode _displayedMode = ImageExportProcessingMode.Ssaa;
    private (string Label, double Value)[] _displayedFactors = AllSsaaFactors;
    private double _selectedSsaa = 1;
    private double _selectedBicubic = 1.5;
    private double _selectedLanczos = 2;

    private ImageExportManagerWindow(ImageExportConfiguration configuration)
    {
        _configuration = configuration;
        InitializeComponent();
        Title = configuration.WindowTitle;
        _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _elapsedTimer.Tick += (_, _) => UpdateTimingText();
        Loaded += Window_OnLoaded;
    }

    public static bool? Open(Window owner, ImageExportConfiguration configuration)
    {
        var window = new ImageExportManagerWindow(configuration) { Owner = owner };
        return window.ShowDialog();
    }

    private void Window_OnLoaded(object sender, RoutedEventArgs e)
    {
        ImageExportSettings settings = ImageExportSettingsStore.Load(
            _configuration.InitialWidth, _configuration.InitialHeight);
        _loading = true;
        WidthBox.Text = Math.Clamp(settings.Width, 1, 16_384).ToString(CultureInfo.InvariantCulture);
        HeightBox.Text = Math.Clamp(settings.Height, 1, 16_384).ToString(CultureInfo.InvariantCulture);
        FormatBox.SelectedIndex = Math.Clamp((int)settings.Format, 0, 2);
        ProcessingModeBox.SelectedIndex = Math.Clamp((int)settings.ProcessingMode, 0, 2);
        JpegQualitySlider.Value = Math.Clamp(settings.JpegQuality, 1, 100);
        _selectedSsaa = Math.Min(settings.SsaaFactor, _configuration.MaxSsaaFactor);
        _selectedBicubic = settings.BicubicFactor;
        _selectedLanczos = settings.LanczosFactor;
        _loading = false;
        RefreshQualityFactors();
        UpdateFormatUi();
        UpdateMemoryHint();
    }

    private void Preset_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string value }) return;
        string[] parts = value.Split(',');
        WidthBox.Text = parts[0];
        HeightBox.Text = parts[1];
        UpdateMemoryHint();
    }

    private void Rotate_OnClick(object sender, RoutedEventArgs e)
    {
        (WidthBox.Text, HeightBox.Text) = (HeightBox.Text, WidthBox.Text);
        UpdateMemoryHint();
    }

    private void FormatBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateFormatUi();

    private void UpdateFormatUi()
    {
        if (JpegQualityPanel is not null)
            JpegQualityPanel.Visibility = FormatBox.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void JpegQualitySlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (JpegQualityValue is not null)
            JpegQualityValue.Text = $"{e.NewValue:F0}%";
    }

    private void ProcessingModeBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && QualityFactorBox is not null)
        {
            SaveSelectedFactors(_displayedMode, DisplayedFactor);
            RefreshQualityFactors();
        }
    }

    private void RefreshQualityFactors()
    {
        ImageExportProcessingMode mode = CurrentProcessingMode;
        (string Label, double Value)[] values = GetFactors(mode);
        double desired = mode switch
        {
            ImageExportProcessingMode.Bicubic => _selectedBicubic,
            ImageExportProcessingMode.Lanczos => _selectedLanczos,
            _ => _selectedSsaa
        };
        _displayedMode = mode;
        _displayedFactors = values;
        QualityFactorBox.ItemsSource = values.Select(item => item.Label).ToArray();
        int selected = Array.FindIndex(values, item => Math.Abs(item.Value - desired) < 0.0001);
        QualityFactorBox.SelectedIndex = selected >= 0 ? selected : 0;
        QualityFactorLabel.Text = mode switch
        {
            ImageExportProcessingMode.Bicubic => "Коэффициент апскейла",
            ImageExportProcessingMode.Lanczos => "Коэффициент Ланцоша",
            _ => "Сглаживание (SSAA)"
        };
        QualityHint.Text = mode switch
        {
            ImageExportProcessingMode.Bicubic => "Рендер в меньшем разрешении с последующим бикубическим увеличением.",
            ImageExportProcessingMode.Lanczos => "Рендер в масштабе коэффициента и финальное масштабирование фильтром Ланцоша 3.",
            _ => "Рендер увеличенного числа выборок с уменьшением до выбранного разрешения."
        };
        UpdateMemoryHint();
    }

    private (string Label, double Value)[] GetFactors(ImageExportProcessingMode mode) => mode switch
    {
        ImageExportProcessingMode.Bicubic => BicubicFactors,
        ImageExportProcessingMode.Lanczos => LanczosFactors,
        _ => AllSsaaFactors.Where(item => item.Value <= Math.Max(1, _configuration.MaxSsaaFactor)).ToArray()
    };

    private ImageExportProcessingMode CurrentProcessingMode =>
        (ImageExportProcessingMode)Math.Clamp(ProcessingModeBox?.SelectedIndex ?? 0, 0, 2);

    private double CurrentFactor
    {
        get
        {
            return DisplayedFactor;
        }
    }

    private double DisplayedFactor
    {
        get
        {
            int index = Math.Clamp(QualityFactorBox?.SelectedIndex ?? 0, 0, _displayedFactors.Length - 1);
            return _displayedFactors[index].Value;
        }
    }

    private void ExportOption_OnChanged(object sender, EventArgs e)
    {
        if (!_loading)
            UpdateMemoryHint();
    }

    private async void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isRendering || !TryCreatePlan(out ExportPlan plan)) return;
        SaveSelectedFactors(plan.Mode, plan.Factor);
        PersistSettings(plan);

        string extension = plan.Format switch
        {
            ImageExportFormat.Jpeg => ".jpg",
            ImageExportFormat.Bmp => ".bmp",
            _ => ".png"
        };
        var dialog = new SaveFileDialog
        {
            Filter = plan.Format switch
            {
                ImageExportFormat.Jpeg => "JPEG image|*.jpg;*.jpeg",
                ImageExportFormat.Bmp => "Bitmap image|*.bmp",
                _ => "PNG image|*.png"
            },
            DefaultExt = extension,
            AddExtension = true,
            FileName = $"{_configuration.FileNamePrefix}_{DateTime.Now:yyyyMMdd_HHmmss}{extension}",
            Title = "Сохранить изображение"
        };
        if (dialog.ShowDialog(this) != true) return;

        SetRenderingState(true);
        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;
        _stopwatch.Restart();
        _renderSamples.Clear();
        _renderSamples.Enqueue((0, 0));
        _renderPercentage = 0;
        _estimatingRender = true;
        RemainingText.Visibility = Visibility.Visible;
        UpdateTimingText();
        _elapsedTimer.Start();
        BitmapSource? bitmap = null;
        try
        {
            bool needsResize = plan.RenderWidth != plan.OutputWidth || plan.RenderHeight != plan.OutputHeight ||
                               (!_configuration.HasNativeSsaa && plan.Mode == ImageExportProcessingMode.Ssaa && plan.Factor > 1);
            int renderProgressLimit = needsResize ? 90 : 96;
            StatusText.Text = "Рендеринг изображения...";
            var renderProgress = new Progress<int>(value =>
            {
                // Ignore queued reports from an earlier export or a completed render stage.
                if (!_estimatingRender || _cts?.Token != token || token.IsCancellationRequested) return;
                int percentage = Math.Clamp(value, 0, 100);
                if (percentage <= _renderPercentage) return;
                _renderPercentage = percentage;
                _renderSamples.Enqueue((_stopwatch.Elapsed.TotalSeconds, percentage));
                ProgressBar.Value = percentage * renderProgressLimit / 100.0;
                UpdateTimingText();
            });
            bitmap = await _configuration.RenderAsync(
                new ImageExportRenderRequest(plan.RenderWidth, plan.RenderHeight, plan.RenderSsaaFactor),
                token, renderProgress);
            _estimatingRender = false;
            RemainingText.Visibility = Visibility.Collapsed;
            if (token.IsCancellationRequested) { StatusText.Text = "Операция отменена."; return; }

            if (bitmap.PixelWidth != plan.OutputWidth || bitmap.PixelHeight != plan.OutputHeight)
            {
                StatusText.Text = plan.ResizeWithLanczos
                    ? "Масштабирование фильтром Ланцоша 3..."
                    : "Бикубическое масштабирование...";
                if (plan.ResizeWithLanczos)
                {
                    bitmap = await Task.Run(() => BitmapResampler.ResizeLanczos3(bitmap, plan.OutputWidth,
                        plan.OutputHeight, token,
                        value => Dispatcher.Invoke(() => ProgressBar.Value = 90 + value / 10.0)));
                }
                else
                {
                    bitmap = BitmapResampler.ResizeBicubic(bitmap, plan.OutputWidth, plan.OutputHeight);
                }
                if (token.IsCancellationRequested) { StatusText.Text = "Операция отменена."; return; }
            }

            StatusText.Text = "Запись файла...";
            ProgressBar.Value = 98;
            if (bitmap.CanFreeze && !bitmap.IsFrozen) bitmap.Freeze();
            await Task.Run(() => SaveBitmap(bitmap, dialog.FileName, plan.Format, plan.JpegQuality));
            ProgressBar.Value = 100;
            _stopwatch.Stop();
            StatusText.Text = $"Сохранено: {dialog.FileName}";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Операция отменена.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Не удалось сохранить изображение.";
            MessageBox.Show(this, ex.Message, "Ошибка экспорта", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _stopwatch.Stop();
            _elapsedTimer.Stop();
            _estimatingRender = false;
            RemainingText.Visibility = Visibility.Collapsed;
            UpdateTimingText();
            _cts.Dispose();
            _cts = null;
            bitmap = null;
            if (_configuration.ReleaseMemoryAfterExport)
                await MemoryPressureRelief.ReleaseAsync();
            SetRenderingState(false);
            if (_closeWhenIdle)
                _ = Dispatcher.BeginInvoke(Close);
        }
    }

    private void UpdateTimingText()
    {
        TimeSpan elapsed = _stopwatch.Elapsed;
        ElapsedText.Text = $"{(long)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}.{elapsed.Milliseconds / 100}";
        if (!_estimatingRender) return;
        if (_cts?.IsCancellationRequested == true)
        {
            RemainingText.Text = "Рендер отменяется...";
            return;
        }
        if (_renderPercentage >= 100)
        {
            RemainingText.Text = "Завершение рендера...";
            return;
        }

        double seconds = elapsed.TotalSeconds;
        // Keep an anchor just before the last 10 seconds, including sparse progress reports.
        while (_renderSamples.Count > 2 && _renderSamples.ElementAt(1).Seconds <= seconds - 10)
            _renderSamples.Dequeue();
        var first = _renderSamples.Peek();
        double sampleDuration = seconds - first.Seconds;
        int completed = _renderPercentage - first.Percentage;
        if (sampleDuration < 1 || completed <= 0)
        {
            RemainingText.Text = "До завершения рендера: оценка скорости...";
            return;
        }

        // Use raw render progress: the export bar also reserves a share for resizing and saving.
        double remainingSeconds = (100 - _renderPercentage) * sampleDuration / completed;
        TimeSpan remaining = TimeSpan.FromSeconds(Math.Ceiling(remainingSeconds));
        RemainingText.Text = $"До завершения рендера ≈ {(long)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}";
    }

    internal static void SaveBitmap(BitmapSource bitmap, string path, ImageExportFormat format, int jpegQuality)
    {
        BitmapEncoder encoder = format switch
        {
            ImageExportFormat.Jpeg => new JpegBitmapEncoder { QualityLevel = jpegQuality },
            ImageExportFormat.Bmp => new BmpBitmapEncoder(),
            _ => new PngBitmapEncoder()
        };
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        ImageExportFile.Write(path, encoder.Save);
    }

    private bool TryCreatePlan(out ExportPlan plan)
    {
        plan = default;
        if (!int.TryParse(WidthBox.Text, out int width) || !int.TryParse(HeightBox.Text, out int height) ||
            width is < 1 or > 16_384 || height is < 1 or > 16_384)
        {
            MessageBox.Show(this, "Размер должен быть от 1 до 16384 пикселей.", "Размер изображения",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        ImageExportProcessingMode mode = CurrentProcessingMode;
        double factor = CurrentFactor;
        int renderWidth;
        int renderHeight;
        int renderSsaa;
        bool lanczos;
        switch (mode)
        {
            case ImageExportProcessingMode.Bicubic:
                renderWidth = Math.Max(1, (int)Math.Round(width / factor));
                renderHeight = Math.Max(1, (int)Math.Round(height / factor));
                renderSsaa = 1;
                lanczos = false;
                break;
            case ImageExportProcessingMode.Lanczos:
                renderWidth = Math.Max(1, (int)Math.Round(width * factor));
                renderHeight = Math.Max(1, (int)Math.Round(height * factor));
                renderSsaa = 1;
                lanczos = true;
                break;
            default:
                if (_configuration.HasNativeSsaa)
                {
                    renderWidth = width;
                    renderHeight = height;
                    renderSsaa = (int)factor;
                }
                else
                {
                    renderWidth = checked(width * (int)factor);
                    renderHeight = checked(height * (int)factor);
                    renderSsaa = 1;
                }
                lanczos = factor > 1;
                break;
        }

        long actualPixels = _configuration.HasNativeSsaa && mode == ImageExportProcessingMode.Ssaa
            ? checked((long)renderWidth * renderHeight * renderSsaa * renderSsaa)
            : checked((long)renderWidth * renderHeight);
        if (actualPixels > 160_000_000)
        {
            MessageBox.Show(this,
                "Выбранные разрешение и качество требуют слишком много памяти. Уменьшите размер или коэффициент.",
                "Размер изображения", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        plan = new ExportPlan(width, height, renderWidth, renderHeight, renderSsaa, mode, factor,
            (ImageExportFormat)Math.Clamp(FormatBox.SelectedIndex, 0, 2),
            Math.Clamp((int)Math.Round(JpegQualitySlider.Value), 1, 100), lanczos);
        return true;
    }

    private void UpdateMemoryHint()
    {
        if (MemoryHint is null) return;
        if (!int.TryParse(WidthBox?.Text, out int width) || !int.TryParse(HeightBox?.Text, out int height))
        {
            MemoryHint.Text = "Введите корректное разрешение.";
            return;
        }
        try
        {
            double factor = CurrentFactor;
            long pixels = CurrentProcessingMode switch
            {
                ImageExportProcessingMode.Bicubic =>
                    (long)Math.Max(1, Math.Round(width / factor)) * (long)Math.Max(1, Math.Round(height / factor)),
                ImageExportProcessingMode.Lanczos =>
                    (long)Math.Max(1, Math.Round(width * factor)) * (long)Math.Max(1, Math.Round(height * factor)),
                _ => (long)width * height * (long)factor * (long)factor
            };
            MemoryHint.Text = $"Рабочий кадр: {pixels:N0} пикселей, примерно {pixels * 4 / 1024d / 1024d:N0} МБ без служебных данных.";
        }
        catch (OverflowException)
        {
            MemoryHint.Text = "Выбран слишком большой размер.";
        }
    }

    private void SaveSelectedFactors(ImageExportProcessingMode mode, double factor)
    {
        switch (mode)
        {
            case ImageExportProcessingMode.Bicubic: _selectedBicubic = factor; break;
            case ImageExportProcessingMode.Lanczos: _selectedLanczos = factor; break;
            default: _selectedSsaa = factor; break;
        }
    }

    private void PersistSettings(ExportPlan plan) => ImageExportSettingsStore.Save(new ImageExportSettings
    {
        Width = plan.OutputWidth,
        Height = plan.OutputHeight,
        Format = plan.Format,
        ProcessingMode = plan.Mode,
        SsaaFactor = (int)_selectedSsaa,
        BicubicFactor = _selectedBicubic,
        LanczosFactor = _selectedLanczos,
        JpegQuality = plan.JpegQuality
    });

    private void SetRenderingState(bool rendering)
    {
        _isRendering = rendering;
        if (rendering)
        {
            ProgressBar.Value = 0;
            ElapsedText.Text = "00:00:00.0";
        }
        OptionsPanel.IsEnabled = !rendering;
        SaveButton.IsEnabled = !rendering;
        CancelButton.Content = rendering ? "Отменить" : "Закрыть";
        if (!rendering && ProgressBar.Value < 100 && StatusText.Text == "Готово")
            ProgressBar.Value = 0;
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isRendering)
            _cts?.Cancel();
        else
            Close();
    }

    private void Window_OnClosing(object? sender, CancelEventArgs e)
    {
        if (_isRendering)
        {
            e.Cancel = true;
            _closeWhenIdle = true;
            StatusText.Text = "Отмена операции...";
            _cts?.Cancel();
        }
    }

    private readonly record struct ExportPlan(
        int OutputWidth,
        int OutputHeight,
        int RenderWidth,
        int RenderHeight,
        int RenderSsaaFactor,
        ImageExportProcessingMode Mode,
        double Factor,
        ImageExportFormat Format,
        int JpegQuality,
        bool ResizeWithLanczos);
}
