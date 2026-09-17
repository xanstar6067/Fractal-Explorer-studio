using System.Windows;
using System.Windows.Controls;
using Size = System.Windows.Size;

namespace FractalExplorerWPF.Controls;

/// <summary>
/// Сетка плиток одинаковой ширины: в строку помещается столько колонок шириной не меньше
/// <see cref="MinItemWidth"/>, сколько позволяет ширина, и они растягиваются на всю строку.
/// Высота строки — по самой высокой плитке, поэтому подписи в несколько строк не обрезаются.
/// </summary>
public sealed class UniformWrapPanel : Panel
{
    public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.Register(
        nameof(MinItemWidth), typeof(double), typeof(UniformWrapPanel),
        new FrameworkPropertyMetadata(140d, FrameworkPropertyMetadataOptions.AffectsMeasure), IsPositive);

    public static readonly DependencyProperty HorizontalSpacingProperty = DependencyProperty.Register(
        nameof(HorizontalSpacing), typeof(double), typeof(UniformWrapPanel),
        new FrameworkPropertyMetadata(8d, FrameworkPropertyMetadataOptions.AffectsMeasure), IsNonNegative);

    public static readonly DependencyProperty VerticalSpacingProperty = DependencyProperty.Register(
        nameof(VerticalSpacing), typeof(double), typeof(UniformWrapPanel),
        new FrameworkPropertyMetadata(8d, FrameworkPropertyMetadataOptions.AffectsMeasure), IsNonNegative);

    public double MinItemWidth
    {
        get => (double)GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    public double HorizontalSpacing
    {
        get => (double)GetValue(HorizontalSpacingProperty);
        set => SetValue(HorizontalSpacingProperty, value);
    }

    public double VerticalSpacing
    {
        get => (double)GetValue(VerticalSpacingProperty);
        set => SetValue(VerticalSpacingProperty, value);
    }

    /// <summary>Число колонок последней раскладки.</summary>
    public int Columns { get; private set; } = 1;

    protected override Size MeasureOverride(Size availableSize)
    {
        int count = InternalChildren.Count;
        double width = double.IsInfinity(availableSize.Width)
            ? Math.Max(0, count * (MinItemWidth + HorizontalSpacing) - HorizontalSpacing)
            : availableSize.Width;
        (int columns, double itemWidth) = Layout(width);
        Columns = columns;

        double height = 0;
        double rowHeight = 0;
        for (int index = 0; index < count; index++)
        {
            UIElement child = InternalChildren[index];
            child.Measure(new Size(itemWidth, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            if (index % columns == columns - 1 || index == count - 1)
            {
                height += rowHeight + (index >= columns ? VerticalSpacing : 0);
                rowHeight = 0;
            }
        }
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int count = InternalChildren.Count;
        (int columns, double itemWidth) = Layout(finalSize.Width);
        Columns = columns;

        double y = 0;
        for (int rowStart = 0; rowStart < count; rowStart += columns)
        {
            int rowEnd = Math.Min(rowStart + columns, count);
            double rowHeight = 0;
            for (int index = rowStart; index < rowEnd; index++)
                rowHeight = Math.Max(rowHeight, InternalChildren[index].DesiredSize.Height);

            for (int index = rowStart; index < rowEnd; index++)
            {
                double x = (index - rowStart) * (itemWidth + HorizontalSpacing);
                InternalChildren[index].Arrange(new Rect(x, y, itemWidth, rowHeight));
            }
            y += rowHeight + VerticalSpacing;
        }
        return finalSize;
    }

    // Число колонок не зависит от числа плиток: в группе из одной плитки она той же ширины,
    // что и в соседних группах, а не растянута на всю строку.
    private (int Columns, double ItemWidth) Layout(double width)
    {
        double spacing = HorizontalSpacing;
        int columns = Math.Max(1, (int)Math.Floor((width + spacing) / (MinItemWidth + spacing)));
        double itemWidth = Math.Max(0, (width - spacing * (columns - 1)) / columns);
        return (columns, itemWidth);
    }

    private static bool IsPositive(object value) => value is double number && double.IsFinite(number) && number > 0;
    private static bool IsNonNegative(object value) => value is double number && double.IsFinite(number) && number >= 0;
}
