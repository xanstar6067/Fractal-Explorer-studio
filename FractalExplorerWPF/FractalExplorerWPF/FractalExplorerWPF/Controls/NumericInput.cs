using System.Globalization;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Automation;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Infrastructure.Serialization;

namespace FractalExplorerWPF.Controls;

/// <summary>
/// Completion of numeric drafts. Register after filling initial values; keep exact valid text.
/// Background captures call Prepare, explicit commands call CommitAll before reading fields.
/// </summary>
public static class NumericInput
{
    public static readonly DependencyProperty MessageProperty = DependencyProperty.RegisterAttached(
        "Message", typeof(string), typeof(NumericInput), new PropertyMetadata(""));
    public static readonly DependencyProperty HasNoticeProperty = DependencyProperty.RegisterAttached(
        "HasNotice", typeof(bool), typeof(NumericInput), new PropertyMetadata(false));
    public static readonly DependencyProperty UseSliderBoundsProperty = DependencyProperty.RegisterAttached(
        "UseSliderBounds", typeof(bool), typeof(NumericInput), new PropertyMetadata(false, OnSliderBoundsChanged));
    private static readonly DependencyProperty RuleProperty = DependencyProperty.RegisterAttached(
        "Rule", typeof(Rule), typeof(NumericInput));
    private static readonly DependencyProperty FieldsProperty = DependencyProperty.RegisterAttached(
        "Fields", typeof(List<TextBox>), typeof(NumericInput));
    private static readonly DependencyProperty EditorsProperty = DependencyProperty.RegisterAttached(
        "Editors", typeof(List<FrameworkElement>), typeof(NumericInput));
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static string GetMessage(DependencyObject obj) => (string)obj.GetValue(MessageProperty);
    public static bool GetHasNotice(DependencyObject obj) => (bool)obj.GetValue(HasNoticeProperty);
    /// <summary>Field feedback for expressions and model constraints that cannot be corrected numerically.</summary>
    public static void ShowIssue(TextBox box, string message) => Notice(box, message);
    public static bool GetUseSliderBounds(DependencyObject obj) => (bool)obj.GetValue(UseSliderBoundsProperty);
    public static void SetUseSliderBounds(DependencyObject obj, bool value) => obj.SetValue(UseSliderBoundsProperty, value);

    private static void OnSliderBoundsChanged(DependencyObject obj, DependencyPropertyChangedEventArgs e)
    {
        if (obj is not TextBox box) return;
        if ((bool)e.NewValue) box.Loaded += OnSliderBoxLoaded;
        else box.Loaded -= OnSliderBoxLoaded;
    }

    private static void OnSliderBoxLoaded(object sender, RoutedEventArgs e)
    {
        var box = (TextBox)sender;
        if (IsRegistered(box) || box.IsReadOnly ||
            box.GetBindingExpression(TextBox.TextProperty) is not { ResolvedSource: Slider slider, ResolvedSourcePropertyName: "Value" }) return;
        // A direct slider binding already declares its numeric domain. Share that contract.
        string name = AutomationProperties.GetName(box);
        FrameworkElement scope = box;
        for (DependencyObject? parent = box.Parent; parent is not null; parent = LogicalTreeHelper.GetParent(parent))
            if (parent is UserControl editor) { scope = editor; break; }
        Real(scope, box, string.IsNullOrWhiteSpace(name) ? "Значение" : name,
            () => slider.Minimum, () => slider.Maximum, null, false, () => slider.Value.ToString("R", Invariant));
    }
    public static bool IsRegistered(TextBox box) => box.GetValue(RuleProperty) is Rule;
    public static bool IsValid(TextBox box) => box.GetValue(RuleProperty) is not Rule rule ||
        rule.Normalize(box.Text) is string normalized && Canonical(normalized) == Canonical(box.Text);

    public static void Integer(FrameworkElement scope, TextBox box, string name, int minimum, int maximum,
        int step = 1, Func<bool>? active = null) => Integer(scope, box, name, () => minimum, () => maximum, step, active);

    public static void Integer(FrameworkElement scope, TextBox box, string name, Func<int> getMinimum, Func<int> getMaximum,
        int step = 1, Func<bool>? active = null)
    {
        if (step <= 0) throw new ArgumentOutOfRangeException(nameof(step));
        NumericSpinner.SetIsInteger(box, true);
        NumericSpinner.SetIntegerStep(box, step);
        Register(scope, box, name, text =>
        {
            int minimum = getMinimum(), maximum = getMaximum();
            if (!BigInteger.TryParse(text, NumberStyles.Integer, Invariant, out var value)) return null;
            int bounded = (int)BigInteger.Clamp(value, minimum, maximum);
            if (step > 1)
                bounded = (int)Math.Clamp(minimum + (long)(Math.Round((bounded - (double)minimum) / step,
                    MidpointRounding.AwayFromZero) * step), minimum, maximum);
            return value == bounded ? text.Trim() : bounded.ToString(Invariant);
        }, () => $"целое число {getMinimum()}–{getMaximum()}" + (step == 1 ? "" : $", шаг {step}"), active);
    }

    public static void Real(FrameworkElement scope, TextBox box, string name, double minimum, double maximum,
        Func<bool>? active = null, bool absolute = false, Func<double>? appliedValue = null) =>
        Real(scope, box, name, () => minimum, () => maximum, active, absolute,
            appliedValue is null ? null : () => appliedValue().ToString("R", Invariant));

    private static void Real(FrameworkElement scope, TextBox box, string name, Func<double> minimum, Func<double> maximum,
        Func<bool>? active, bool absolute, Func<string>? appliedText) => Register(scope, box, name, text =>
        {
            if (!PanelFields.TryReadDouble(Canonical(text), out double value) || !double.IsFinite(value)) return null;
            double bounded = Math.Clamp(absolute ? Math.Abs(value) : value, minimum(), maximum());
            return bounded == value ? Canonical(text) : bounded.ToString("R", Invariant);
        }, () => $"конечное число {minimum():G8}–{maximum():G8}", active, appliedText);

    public static void Angle(FrameworkElement scope, TextBox box, string name, Func<double>? appliedValue = null) => Register(scope, box, name, text =>
    {
        if (!PanelFields.TryReadDouble(Canonical(text), out double value) || !double.IsFinite(value)) return null;
        double wrapped = value % 360;
        if (wrapped > 180) wrapped -= 360;
        else if (wrapped < -180) wrapped += 360;
        return value == wrapped ? Canonical(text) : wrapped.ToString("R", Invariant);
    }, () => "конечный угол; полные обороты приводятся к −180…180°", null,
        appliedValue is null ? null : () => appliedValue().ToString("R", Invariant));

    public static void Decimal(FrameworkElement scope, TextBox box, string name, decimal minimum, decimal maximum,
        Func<bool>? active = null, decimal minimumMagnitude = 0, Func<decimal>? appliedValue = null) =>
        Decimal(scope, box, name, () => minimum, () => maximum, active, minimumMagnitude, appliedValue);

    public static void Decimal(FrameworkElement scope, TextBox box, string name, Func<decimal> minimum, Func<decimal> maximum,
        Func<bool>? active = null, decimal minimumMagnitude = 0, Func<decimal>? appliedValue = null,
        Func<bool>? integer = null) => Register(scope, box, name, text =>
        {
            if (!decimal.TryParse(Canonical(text), NumberStyles.Float, Invariant, out decimal value)) return null;
            if (integer?.Invoke() == true && value != decimal.Truncate(value)) return null;
            decimal bounded = Math.Clamp(value, minimum(), maximum());
            if (minimumMagnitude > 0 && bounded > -minimumMagnitude && bounded < minimumMagnitude)
                bounded = bounded < 0 ? -minimumMagnitude : minimumMagnitude;
            return bounded == value ? Canonical(text) : bounded.ToString(Invariant);
        }, () => $"{(integer?.Invoke() == true ? "целое число" : "число")} {minimum():G8}–{maximum():G8}" + (minimumMagnitude > 0 ? $", модуль не меньше {minimumMagnitude}" : ""), active,
            appliedValue is null ? null : () => appliedValue().ToString(Invariant));

    public static void Zoom(FrameworkElement scope, TextBox box, FloatExp minimum, FloatExp maximum, Func<FloatExp>? appliedValue = null) =>
        Register(scope, box, "Масштаб", text =>
        {
            if (!FloatExp.TryParse(Canonical(text), out FloatExp value) || !value.IsFinite) return null;
            return value < minimum ? FloatExpJsonConverter.ToDisplay(minimum) :
                value > maximum ? FloatExpJsonConverter.ToDisplay(maximum) : Canonical(text);
        }, () => $"масштаб {FloatExpJsonConverter.ToDisplay(minimum)}–{FloatExpJsonConverter.ToDisplay(maximum)}", null,
            appliedValue is null ? null : () => appliedValue().ToInvariantString());

    private static string Canonical(string text) => text.Trim().Replace(',', '.');

    private static void Register(FrameworkElement scope, TextBox box, string name,
        Func<string, string?> normalize, Func<string> requirement, Func<bool>? active, Func<string>? appliedText = null)
    {
        if (Window.GetWindow(scope) is Window owner && !ReferenceEquals(owner, scope)) Include(owner, scope);
        // Reconfiguration (e.g. a laboratory mode) must not duplicate event handlers.
        if (box.GetValue(RuleProperty) is not Rule)
        {
            box.TextChanged += OnTextChanged;
            box.LostKeyboardFocus += (_, _) => Commit(box);
            box.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) CommitAll(scope);
                else if (e.Key == Key.Escape && box.GetValue(RuleProperty) is Rule rule)
                {
                    Write(box, rule, rule.Normalize(rule.AppliedText?.Invoke() ?? rule.LastGood) ?? rule.LastGood,
                        "Восстановлено последнее корректное значение.");
                    e.Handled = true;
                }
            };
        }
        if (scope.GetValue(FieldsProperty) is not List<TextBox> fields)
        {
            fields = [];
            scope.SetValue(FieldsProperty, fields);
        }
        if (!fields.Contains(box)) fields.Add(box);
        string initial = (appliedText is null ? null : normalize(appliedText())) ?? normalize(box.Text) ??
            (box.GetValue(RuleProperty) is Rule previous ? normalize(previous.LastGood) : null)
            ?? throw new InvalidOperationException(
            $"Начальное значение поля «{name}» некорректно: {box.Text}");
        box.SetValue(RuleProperty, new Rule(name, normalize, requirement, initial, active, appliedText));
    }

    /// <summary>Do not rewrite a focused draft during an automatic preview.</summary>
    public static void Prepare(FrameworkElement scope)
    {
        if (scope.GetValue(EditorsProperty) is List<FrameworkElement> editors)
            foreach (var editor in editors) Prepare(editor);
        if (scope.GetValue(FieldsProperty) is not List<TextBox> fields) return;
        foreach (TextBox box in fields)
        {
            if (!box.IsKeyboardFocusWithin || box.GetValue(RuleProperty) is Rule { Active: not null } rule && !rule.Active())
                Commit(box, updateSource: false);
            else if (box.GetValue(RuleProperty) is Rule focused && IsValid(box))
                focused.LastGood = (focused.AppliedText is null ? null : focused.Normalize(focused.AppliedText())) ?? focused.Normalize(box.Text)!;
        }
        ValidateGroups(scope, force: false);
    }

    /// <summary>Complete all drafts before an explicit build/apply/save, even if a field retains focus.</summary>
    public static void CommitAll(FrameworkElement scope)
    {
        if (scope.GetValue(EditorsProperty) is List<FrameworkElement> editors)
            foreach (var editor in editors) CommitAll(editor);
        if (scope.GetValue(FieldsProperty) is not List<TextBox> fields) return;
        foreach (TextBox box in fields) Commit(box);
        ValidateGroups(scope, force: true);
    }

    /// <summary>Include a nested editor in the parent's explicit commands without walking the visual tree each frame.</summary>
    public static void Include(FrameworkElement scope, FrameworkElement editor)
    {
        if (ReferenceEquals(scope, editor)) throw new ArgumentException("An editor cannot include itself.");
        if (scope.GetValue(EditorsProperty) is not List<FrameworkElement> editors)
        {
            editors = [];
            scope.SetValue(EditorsProperty, editors);
        }
        if (!editors.Contains(editor)) editors.Add(editor);
    }

    public static void Commit(TextBox box, bool updateSource = true)
    {
        if (box.GetValue(RuleProperty) is not Rule rule || rule.Writing || box.IsReadOnly) return;
        string original = box.Text;
        string? normalized = rule.Normalize(original);
        string result = normalized ?? (rule.AppliedText is null ? null : rule.Normalize(rule.AppliedText())) ?? rule.Normalize(rule.LastGood) ?? throw new InvalidOperationException(
            $"Нет корректного значения для поля «{rule.Name}».");
        string notice = normalized is null
            ? $"«{rule.Name}»: восстановлено {result}. Нужно {rule.Requirement()}."
            : $"«{rule.Name}»: исправлено на {result}. Допустимо: {rule.Requirement()}.";
        if (result != original) Write(box, rule, result, notice);
        else if (updateSource && box.GetBindingExpression(TextBox.TextProperty) is { IsDirty: true } binding) binding.UpdateSource();
        rule.LastGood = (rule.AppliedText is null ? null : rule.Normalize(rule.AppliedText())) ?? rule.Normalize(box.Text) ?? result;
    }

    private static void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        var box = (TextBox)sender;
        if (box.GetValue(RuleProperty) is not Rule rule || rule.Writing) return;
        string? normalized = rule.Normalize(box.Text);
        bool valid = normalized is not null && Canonical(normalized) == Canonical(box.Text);
        // An earlier TextChanged handler may capture parameters and correct the text
        // synchronously. The original event must not erase that correction's feedback.
        if (valid && box.Text == rule.CorrectedText) return;
        rule.CorrectedText = null;
        Notice(box, valid ? "" : $"«{rule.Name}»: нужно {rule.Requirement()}. Значение будет исправлено при завершении ввода.");
        // Loads, presets and camera navigation establish a new fallback too. Focused drafts do not.
        if (valid && !box.IsKeyboardFocusWithin) rule.LastGood = normalized!;
    }

    private static void Write(TextBox box, Rule rule, string text, string notice)
    {
        rule.Writing = true;
        try
        {
            rule.CorrectedText = text;
            box.SetCurrentValue(TextBox.TextProperty, text);
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        }
        finally { rule.Writing = false; }
        Notice(box, notice);
    }

    private static void Notice(TextBox box, string text)
    {
        box.SetValue(MessageProperty, text);
        box.SetValue(HasNoticeProperty, text.Length != 0);
    }

    private sealed class Rule(string name, Func<string, string?> normalize, Func<string> requirement,
        string lastGood, Func<bool>? active, Func<string>? appliedText)
    {
        public readonly string Name = name;
        public readonly Func<string, string?> Normalize = normalize;
        public readonly Func<string> Requirement = requirement;
        public readonly Func<bool>? Active = active;
        public readonly Func<string>? AppliedText = appliedText;
        public string LastGood = lastGood;
        public string? CorrectedText;
        public bool Writing;
    }

    private static readonly DependencyProperty GroupsProperty = DependencyProperty.RegisterAttached(
        "Groups", typeof(List<Action<bool>>), typeof(NumericInput));

    /// <summary>Keep a valid pair together; do not guess which endpoint the user intended to move.</summary>
    public static void OrderedPair(FrameworkElement scope, TextBox lower, TextBox upper, bool allowEqual = false)
        => Pair(scope, lower, upper, (a, b) =>
            FloatExp.TryParse(Canonical(a), out var x) && x.IsFinite &&
            FloatExp.TryParse(Canonical(b), out var y) && y.IsFinite && (allowEqual ? x <= y : x < y),
            allowEqual ? "Нижняя граница должна быть не больше верхней." : "Нижняя граница должна быть меньше верхней.");

    public static void Pair(FrameworkElement scope, TextBox lower, TextBox upper,
        Func<string, string, bool> validPair, string requirement, Func<bool>? active = null)
    {
        if (lower.GetValue(RuleProperty) is not Rule low || upper.GetValue(RuleProperty) is not Rule high)
            throw new InvalidOperationException("Register both numeric fields before their pair.");
        string lastLower = low.LastGood, lastUpper = high.LastGood;
        bool restoring = false;
        if (scope.GetValue(GroupsProperty) is not List<Action<bool>> groups)
        {
            groups = [];
            scope.SetValue(GroupsProperty, groups);
        }
        groups.Add(force =>
        {
            if (restoring || active is not null && !active() || !force && (lower.IsKeyboardFocusWithin || upper.IsKeyboardFocusWithin)) return;
            if (IsValid(lower) && IsValid(upper) && validPair(lower.Text, upper.Text))
            { lastLower = lower.Text; lastUpper = upper.Text; return; }
            restoring = true;
            try
            {
                Write(lower, low, lastLower, requirement + " Восстановлена последняя корректная пара.");
                Write(upper, high, lastUpper, requirement + " Восстановлена последняя корректная пара.");
                low.LastGood = lastLower; high.LastGood = lastUpper;
            }
            finally { restoring = false; }
        });
        void RememberPair(object sender, TextChangedEventArgs e)
        {
            if (!restoring && (active is null || active()) && !lower.IsKeyboardFocusWithin && !upper.IsKeyboardFocusWithin &&
                IsValid(lower) && IsValid(upper) && validPair(lower.Text, upper.Text))
            { lastLower = lower.Text; lastUpper = upper.Text; }
        }
        lower.TextChanged += RememberPair;
        upper.TextChanged += RememberPair;
        lower.LostKeyboardFocus += (_, _) => ValidateGroups(scope, false);
        upper.LostKeyboardFocus += (_, _) => ValidateGroups(scope, false);
    }

    // Dynamic row controls are discarded, so their old drafts must not remain in the scope.
    internal static void Forget(FrameworkElement scope, TextBox box)
    {
        if (scope.GetValue(FieldsProperty) is List<TextBox> fields) fields.Remove(box);
    }

    private static void ValidateGroups(FrameworkElement scope, bool force)
    {
        if (scope.GetValue(GroupsProperty) is List<Action<bool>> groups)
            foreach (var validate in groups) validate(force);
    }
}
