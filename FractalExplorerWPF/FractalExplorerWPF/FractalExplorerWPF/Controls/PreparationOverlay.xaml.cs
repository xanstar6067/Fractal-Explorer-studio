using System.Windows;
using System.Windows.Controls;

namespace FractalExplorerWPF.Controls;

/// <summary>Единый экран подготовки: ожидание, измеримый прогресс и доступное действие.</summary>
public partial class PreparationOverlay : UserControl
{
    public PreparationOverlay() => InitializeComponent();

    public static readonly DependencyProperty HeadingProperty = Register(nameof(Heading), "Подготовка 3D-фрактала");
    public static readonly DependencyProperty MessageProperty = Register(nameof(Message), "Подготовка первого кадра…");
    public static readonly DependencyProperty DetailProperty = Register(nameof(Detail),
        "Первый запуск может занять некоторое время: подготавливаем данные и программы для видеокарты.");
    public static readonly DependencyProperty ActionTextProperty = Register(nameof(ActionText), "Остановить и показать");
    public static readonly DependencyProperty ProgressProperty = Register(nameof(Progress), 0.0);
    public static readonly DependencyProperty IsIndeterminateProperty = Register(nameof(IsIndeterminate), true);
    public static readonly DependencyProperty IsBusyProperty = Register(nameof(IsBusy), true);
    public static readonly DependencyProperty IsActionEnabledProperty = Register(nameof(IsActionEnabled), true);

    public string Heading { get => (string)GetValue(HeadingProperty); set => SetValue(HeadingProperty, value); }
    public string Message { get => (string)GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
    public string Detail { get => (string)GetValue(DetailProperty); set => SetValue(DetailProperty, value); }
    public string ActionText { get => (string)GetValue(ActionTextProperty); set => SetValue(ActionTextProperty, value); }
    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public bool IsIndeterminate { get => (bool)GetValue(IsIndeterminateProperty); set => SetValue(IsIndeterminateProperty, value); }
    public bool IsBusy { get => (bool)GetValue(IsBusyProperty); set => SetValue(IsBusyProperty, value); }
    public bool IsActionEnabled { get => (bool)GetValue(IsActionEnabledProperty); set => SetValue(IsActionEnabledProperty, value); }

    public event RoutedEventHandler? ActionClick;

    private void ActionButton_OnClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        ActionClick?.Invoke(this, e);
    }

    // Подготовка перекрывает жесты камеры, кнопка сохраняет собственную обработку мыши.
    protected override void OnMouseDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        e.Handled = true;
    }

    protected override void OnMouseWheel(System.Windows.Input.MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        e.Handled = true;
    }

    private static DependencyProperty Register<T>(string name, T value) =>
        DependencyProperty.Register(name, typeof(T), typeof(PreparationOverlay), new PropertyMetadata(value));
}
