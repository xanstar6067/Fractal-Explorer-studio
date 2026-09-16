using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace FractalExplorerWPF.Controls;

/// <summary>Opt-in spinner for existing text fields; preserves their validation and bindings.</summary>
public static class NumericSpinner
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(NumericSpinner), new PropertyMetadata(false, OnEnabledChanged));
    public static readonly DependencyProperty IsIntegerProperty = DependencyProperty.RegisterAttached(
        "IsInteger", typeof(bool), typeof(NumericSpinner), new PropertyMetadata(false));
    public static readonly DependencyProperty IntegerStepProperty = DependencyProperty.RegisterAttached(
        "IntegerStep", typeof(int), typeof(NumericSpinner), new PropertyMetadata(1), value => (int)value > 0);

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);
    public static bool GetIsInteger(DependencyObject obj) => (bool)obj.GetValue(IsIntegerProperty);
    public static void SetIsInteger(DependencyObject obj, bool value) => obj.SetValue(IsIntegerProperty, value);
    public static int GetIntegerStep(DependencyObject obj) => (int)obj.GetValue(IntegerStepProperty);
    public static void SetIntegerStep(DependencyObject obj, int value) => obj.SetValue(IntegerStepProperty, value);

    private static void OnEnabledChanged(DependencyObject obj, DependencyPropertyChangedEventArgs args)
    {
        if (obj is not TextBox box) return;
        if ((bool)args.NewValue)
        {
            box.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnClick));
            box.PreviewKeyDown += OnKeyDown;
        }
        else
        {
            box.RemoveHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnClick));
            box.PreviewKeyDown -= OnKeyDown;
        }
    }

    private static void OnClick(object sender, RoutedEventArgs args)
    {
        if (sender is not TextBox box || args.OriginalSource is not RepeatButton button ||
            button.TemplatedParent != box) return;
        int direction = button.Name switch { "PART_Increase" => 1, "PART_Decrease" => -1, _ => 0 };
        if (direction == 0) return;
        Change(box, direction);
        args.Handled = true;
    }

    private static void OnKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key is not (Key.Up or Key.Down) || Keyboard.Modifiers != ModifierKeys.None) return;
        Change((TextBox)sender, args.Key == Key.Up ? 1 : -1);
        args.Handled = true;
    }

    private static void Change(TextBox box, int direction)
    {
        if (!box.IsEnabled || box.IsReadOnly || !GetIsEnabled(box)) return;
        string? next = Step(box.Text, direction, GetIsInteger(box), GetIntegerStep(box));
        if (next is null) return;
        box.SetCurrentValue(TextBox.TextProperty, next);
        box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
    }

    // Decimal string arithmetic avoids losing digits in coordinates or overflowing deep zooms.
    // Real fields step by 0.1, or a smaller decimal order for values below 0.1.
    // Scientific notation steps the mantissa, retaining the original exponent.
    internal static string? Step(string text, int direction, bool integer, int integerStep = 1)
    {
        if (text.Length > 10000 || direction is not (-1 or 1)) return null;
        Match match = Regex.Match(text.Trim(), @"^([+-]?)([0-9]*)([.,]?)([0-9]*)([eE][+-]?[0-9]+)?$");
        if (!match.Success) return null;
        string whole = match.Groups[2].Value, fraction = match.Groups[4].Value;
        if (whole.Length + fraction.Length == 0) return null;
        string exponent = match.Groups[5].Value;
        if (integer && (fraction.Length != 0 || exponent.Length != 0)) return null;
        int stepScale = integer ? 0 : 1;
        if (!integer && whole.TrimStart('0').Length == 0)
        {
            int first = fraction.TakeWhile(c => c == '0').Count();
            if (first > 0 && first < fraction.Length) stepScale = first + 2;
        }
        int scale = Math.Max(fraction.Length, stepScale);
        BigInteger value = BigInteger.Parse("0" + whole + fraction, CultureInfo.InvariantCulture);
        if (match.Groups[1].Value == "-") value = -value;
        value = value * BigInteger.Pow(10, scale - fraction.Length) + direction * BigInteger.Pow(10, scale - stepScale) * (integer ? integerStep : 1);
        string digits = BigInteger.Abs(value).ToString(CultureInfo.InvariantCulture).PadLeft(scale + 1, '0');
        string result = scale == 0 ? digits : digits.Insert(digits.Length - scale, ".").TrimEnd('0').TrimEnd('.');
        if (value.Sign < 0) result = "-" + result;
        if (match.Groups[3].Value == ",") result = result.Replace('.', ',');
        return result + exponent;
    }
}
