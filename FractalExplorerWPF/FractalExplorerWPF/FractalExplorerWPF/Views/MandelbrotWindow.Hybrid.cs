using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class MandelbrotWindow
{
    private HybridFormulaSettings _hybridSettings = new();
    private readonly List<(ComboBox Formula, TextBox Power, TextBox Repeats)> _hybridRows = [];
    private bool _loadingHybrid;
    private sealed record FormulaChoice(MandelbrotVariant Variant, string Name)
    {
        public override string ToString() => Name;
    }
    private void InitializeHybridControls()
    {
        HybridExpander.Visibility = FoldedFormulaCatalog.IsHybrid(_definition.Variant) ? Visibility.Visible : Visibility.Collapsed;
        if (FoldedFormulaCatalog.IsHybrid(_definition.Variant)) LoadHybridSettings(_hybridSettings);
    }
    private void LoadHybridSettings(HybridFormulaSettings settings)
    {
        _hybridSettings = settings.Clone();
        if (!FoldedFormulaCatalog.IsHybrid(_definition.Variant)) return;
        _hybridSettings.Validate();
        _loadingHybrid = true; _hybridRows.Clear(); HybridRowsPanel.Children.Clear();
        for (int i = 0; i < settings.Steps.Count; i++)
        {
            int index = i; var step = settings.Steps[i];
            var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            foreach (var size in new[] { new GridLength(52), new GridLength(52), new GridLength(1, GridUnitType.Star), new GridLength(24), new GridLength(24), new GridLength(24) })
                grid.ColumnDefinitions.Add(new() { Width = size });
            var formula = new ComboBox { ItemsSource = HybridFormulaSettings.HybridFormulaChoices.Select(v => new FormulaChoice(v, MandelbrotVariantDefinition.For(v).DisplayName)).ToArray(), Margin = new Thickness(0, 0, 3, 0), MinWidth = 70, MaxDropDownHeight = 420 };
            formula.SelectedItem = formula.Items.Cast<FormulaChoice>().First(c => c.Variant == step.Formula);
            formula.ToolTip = "Формула шага";
            var power = new TextBox { Text = step.Power.ToString(CultureInfo.InvariantCulture), Margin = new Thickness(0, 0, 3, 0), ToolTip = "Степень 2–5; именованные формулы имеют фиксированную степень" };
            var repeats = new TextBox { Text = step.Repeats.ToString(CultureInfo.InvariantCulture), Margin = new Thickness(0, 0, 3, 0), ToolTip = "Число повторов 1–32" };
            _hybridRows.Add((formula, power, repeats));
            void Add(UIElement element, int column)
            {
                Grid.SetColumn(element, column); Grid.SetRow(element, ReferenceEquals(element, formula) ? 0 : 1);
                if (ReferenceEquals(element, formula)) Grid.SetColumnSpan(element, 6);
                grid.Children.Add(element);
            }
            Add(formula, 0); Add(power, 0); Add(repeats, 1);
            for (int k = 0; k < 3; k++)
            {
                int action = k;
                var button = new Button { Content = k == 0 ? "↑" : k == 1 ? "↓" : "×", Padding = new Thickness(0), ToolTip = k == 0 ? "Выше" : k == 1 ? "Ниже" : "Удалить шаг" };
                button.IsEnabled = k == 0 ? i > 0 : k == 1 ? i < settings.Steps.Count - 1 : settings.Steps.Count > 1;
                button.Click += (_, _) => ChangeHybridRow(index, action); Add(button, 3 + k);
            }
            HybridRowsPanel.Children.Add(grid);
            formula.SelectionChanged += (_, _) => { SetHybridPowerMode(formula, power); HybridControlsChanged(); };
            power.TextChanged += (_, _) => HybridControlsChanged(); repeats.TextChanged += (_, _) => HybridControlsChanged();
            SetHybridPowerMode(formula, power);
        }
        _loadingHybrid = false; HybridAddButton.IsEnabled = settings.Steps.Count < 16;
        HybridValidationText.Text = $"В цикле {_hybridSettings.Steps.Sum(s => s.Repeats)} шагов";
    }
    private void SetHybridPowerMode(ComboBox formula, TextBox power)
    {
        var choice = (FormulaChoice)formula.SelectedItem;
        formula.ToolTip = choice.Name;
        var fixedFormula = FoldedFormulaCatalog.Find(choice.Variant);
        bool fixedPower = fixedFormula is not null || choice.Variant is MandelbrotVariant.CubicQuasiBurningShip or MandelbrotVariant.CubicFlyingSquirrel;
        power.IsEnabled = !fixedPower;
        if (fixedPower) power.Text = (fixedFormula?.Degree ?? 3).ToString(CultureInfo.InvariantCulture);
    }
    private HybridFormulaSettings ReadHybridControls()
    {
        var settings = new HybridFormulaSettings { Steps = [] };
        foreach (var row in _hybridRows)
        {
            if (row.Formula.SelectedItem is not FormulaChoice formula || !int.TryParse(row.Power.Text, out int power) || !int.TryParse(row.Repeats.Text, out int repeats))
                throw new ArgumentException("Укажите целые степень и число повторов.");
            settings.Steps.Add(new() { Formula = formula.Variant, Power = power, Repeats = repeats });
        }
        settings.Validate(); return settings;
    }
    private void HybridControlsChanged()
    {
        if (_loadingHybrid || _updatingControls) return;
        try
        {
            _hybridSettings = ReadHybridControls();
            HybridValidationText.Text = $"В цикле {_hybridSettings.Steps.Sum(s => s.Repeats)} шагов";
            if (!IsLoaded) return;
            _constantPicker?.UpdateFormulaParameters(ReadFormulaPower(), InversionBox.IsChecked == true, _hybridSettings);
            if (_definition.HasJuliaConstant) _ = RenderJuliaMapPreviewAsync();
            ScheduleRender();
        }
        catch (ArgumentException ex) { HybridValidationText.Text = ex.Message; }
    }
    private void ChangeHybridRow(int index, int action)
    {
        try
        {
            var settings = ReadHybridControls();
            if (action == 2) settings.Steps.RemoveAt(index);
            else
            {
                int next = index + (action == 0 ? -1 : 1);
                (settings.Steps[index], settings.Steps[next]) = (settings.Steps[next], settings.Steps[index]);
            }
            LoadHybridSettings(settings); HybridControlsChanged();
        }
        catch (ArgumentException ex) { HybridValidationText.Text = ex.Message; }
    }
    private void HybridAdd_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = ReadHybridControls(); if (settings.Steps.Count >= 16) return;
            settings.Steps.Add(new()); LoadHybridSettings(settings); HybridControlsChanged();
        }
        catch (ArgumentException ex) { HybridValidationText.Text = ex.Message; }
    }
}
