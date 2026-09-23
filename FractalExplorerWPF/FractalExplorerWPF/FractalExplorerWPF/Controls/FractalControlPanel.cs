using System.Windows;
using System.Windows.Controls;

namespace FractalExplorerWPF.Controls;

public static class FractalControlPanel
{
    public static void Toggle(
        ref bool isVisible,
        ColumnDefinition column,
        FrameworkElement panel,
        Button button,
        double expandedWidth,
        Action? layoutChanged = null)
    {
        isVisible = !isVisible;
        column.Width = isVisible ? new GridLength(expandedWidth) : new GridLength(0);
        panel.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        if (button.Parent is Grid grid)
        {
            foreach (UIElement child in grid.Children)
            {
                if (child is not Border viewport || Grid.GetColumn(viewport) != Grid.GetColumn(button))
                    continue;

                Thickness margin = viewport.Margin;
                viewport.Margin = new Thickness(
                    isVisible ? 0 : margin.Right,
                    margin.Top,
                    margin.Right,
                    margin.Bottom);
                break;
            }
        }
        button.Content = isVisible ? "✕" : "☰";
        button.ToolTip = isVisible ? "Скрыть панель параметров" : "Показать панель параметров";
        layoutChanged?.Invoke();
    }
}
