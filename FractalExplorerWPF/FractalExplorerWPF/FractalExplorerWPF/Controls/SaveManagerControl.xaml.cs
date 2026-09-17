using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FractalExplorerWPF.Controls;

public partial class SaveManagerControl : UserControl
{
    public event EventHandler? SelectionChanged;
    public event EventHandler? ItemDoubleClicked;
    public event EventHandler? SaveRequested;
    public event EventHandler? DeleteRequested;
    public event EventHandler? LoadRequested;
    public event EventHandler? RenderPreviewRequested;
    public event EventHandler? CancelPreviewRequested;
    public event EventHandler? PointsOfInterestModeChanged;
    public event EventHandler? CloseRequested;
    public event EventHandler? CloudRequested;
    public event EventHandler<string>? SearchTextChanged;

    private static readonly AnimationTimeline PulseAnimation;

    static SaveManagerControl()
    {
        var pulse = new DoubleAnimation(0.14, 0.32, TimeSpan.FromMilliseconds(900))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };
        pulse.Freeze();
        PulseAnimation = pulse;
    }

    private bool _isRendering;
    private int? _renderPercent;

    public SaveManagerControl()
    {
        InitializeComponent();
        RenderPreviewButton.SizeChanged += (_, _) => UpdateRenderFillWidth();
    }

    public object? SelectedItem
    {
        get => SavesList.SelectedItem;
        set => SavesList.SelectedItem = value;
    }

    public string SaveName
    {
        get => SaveNameBox.Text;
        set => SaveNameBox.Text = value;
    }

    public bool IsPointsOfInterestMode => PointsOfInterestCheckBox.IsChecked == true;

    public void SetItems(IEnumerable items)
    {
        SavesList.ItemsSource = null;
        SavesList.ItemsSource = items;
    }

    public void SetPointsOfInterestAvailable(bool available)
    {
        PointsOfInterestCheckBox.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        if (!available) PointsOfInterestCheckBox.IsChecked = false;
    }

    public void SetPreview(ImageSource? image, string emptyText = "Выберите сохранение")
    {
        PreviewImage.Source = image;
        EmptyPreviewText.Text = emptyText;
        EmptyPreviewText.Visibility = image is null ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetDetails(string text) => DetailsText.Text = text;

    public void SetStatus(string text)
    {
        StatusText.Text = text;
        StatusText.ToolTip = text;
    }

    private const double RenderFillOpacity = 0.42;

    /// <summary>Включает/выключает режим рендера у кнопки «Рендер превью» — без отдельной строки прогресса.</summary>
    public void SetBusy(bool busy)
    {
        _isRendering = busy;
        if (busy)
        {
            RenderPreviewButton.BorderBrush = (System.Windows.Media.Brush)FindResource("Theme.InteractiveHoverBrush");
            SetRenderProgress(null, TimeSpan.Zero);
        }
        else
        {
            RenderFillBar.BeginAnimation(OpacityProperty, null);
            RenderFillBar.Opacity = RenderFillOpacity;
            RenderFillBar.Width = 0;
            RenderButtonText.Text = "Рендер превью";
            RenderPreviewButton.ClearValue(Control.BorderBrushProperty);
        }
    }

    public void SetRenderProgress(int? percent, TimeSpan elapsed)
    {
        _renderPercent = percent;
        if (percent.HasValue)
        {
            RenderFillBar.BeginAnimation(OpacityProperty, null);
            RenderFillBar.Opacity = RenderFillOpacity;
            UpdateRenderFillWidth();
            RenderButtonText.Text = $"{percent}%  ·  нажмите для отмены  ·  {elapsed.TotalSeconds:F1} сек.";
        }
        else
        {
            RenderFillBar.Width = RenderPreviewButton.ActualWidth;
            RenderFillBar.BeginAnimation(OpacityProperty, PulseAnimation);
            RenderButtonText.Text = $"Вычисление...  ·  нажмите для отмены  ·  {elapsed.TotalSeconds:F1} сек.";
        }
    }

    public void SetCancelling()
    {
        RenderPreviewButton.IsEnabled = false;
        RenderFillBar.BeginAnimation(OpacityProperty, null);
        RenderFillBar.Opacity = RenderFillOpacity;
        RenderButtonText.Text = "Отмена...";
    }

    public void SetButtonStates(bool hasSelection, bool canEdit, bool isRendering)
    {
        LoadButton.IsEnabled = hasSelection;
        DeleteButton.IsEnabled = hasSelection && canEdit;
        SaveButton.IsEnabled = canEdit;
        SaveNameBox.IsEnabled = canEdit;
        // Кнопка остаётся активной во время рендера — повторный клик по ней отменяет рендер.
        RenderPreviewButton.IsEnabled = hasSelection;
    }

    private void UpdateRenderFillWidth()
    {
        if (!_isRendering || _renderPercent is not { } percent) return;
        RenderFillBar.Width = RenderPreviewButton.ActualWidth * Math.Clamp(percent, 0, 100) / 100.0;
    }

    private void SavesList_OnSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        SelectionChanged?.Invoke(this, EventArgs.Empty);

    private void SavesList_OnMouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        ItemDoubleClicked?.Invoke(this, EventArgs.Empty);

    private void SaveButton_OnClick(object sender, RoutedEventArgs e) => SaveRequested?.Invoke(this, EventArgs.Empty);
    private void DeleteButton_OnClick(object sender, RoutedEventArgs e) => DeleteRequested?.Invoke(this, EventArgs.Empty);
    private void LoadButton_OnClick(object sender, RoutedEventArgs e) => LoadRequested?.Invoke(this, EventArgs.Empty);

    private void RenderPreviewButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isRendering) CancelPreviewRequested?.Invoke(this, EventArgs.Empty);
        else RenderPreviewRequested?.Invoke(this, EventArgs.Empty);
    }

    private void PointsOfInterestCheckBox_OnChanged(object sender, RoutedEventArgs e) => PointsOfInterestModeChanged?.Invoke(this, EventArgs.Empty);
    private void CloseButton_OnClick(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
    private void CloudButton_OnClick(object sender, RoutedEventArgs e) => CloudRequested?.Invoke(this, EventArgs.Empty);

    private void SearchBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        SearchTextChanged?.Invoke(this, SearchBox.Text);
    }
}
