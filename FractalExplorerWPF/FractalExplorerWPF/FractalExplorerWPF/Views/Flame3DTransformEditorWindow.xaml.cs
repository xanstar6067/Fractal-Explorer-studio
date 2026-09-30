using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Infrastructure.ColorPicking;
using FractalExplorerWPF.Models;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Views;

public partial class Flame3DTransformEditorWindow : Window
{
    private readonly List<Flame3DTransform> _transforms;
    private readonly Stack<Snapshot> _undo = new();
    private readonly ColorSelectionService _picker = ColorSelectionService.Default;
    private readonly Flame3DRandomizationSettings _randomSettings = Flame3DRandomizationSettingsStore.Load();
    private readonly System.Windows.Threading.DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private List<Flame3DTransform> _applied = [];
    private bool _accepted;
    public event Action? Randomized;
    private int _selectedIndex = -1; private bool _syncing;
    private bool _randomSettingsSyncing;
    public event Action<IReadOnlyList<Flame3DTransform>>? TransformsApplied;
    public List<Flame3DTransform> ResultTransforms { get; private set; }

    public Flame3DTransformEditorWindow(IEnumerable<Flame3DTransform> transforms)
    {
        InitializeComponent(); _transforms = transforms.Select(t => t.Clone()).ToList(); ResultTransforms = _transforms;
        _applied = _transforms.Select(t => t.Clone()).ToList();
        _previewTimer.Tick += (_, _) => { _previewTimer.Stop(); if (Validate()) TransformsApplied?.Invoke(_transforms.Select(t => t.Clone()).ToList()); };
        VariationBox.ItemsSource = Enum.GetValues<Flame3DVariation>().Select(v => new VariationOption(v, Flame3DPresets.Name(v))).ToList(); InitializeRandomSettings(); Rebind();
        if (_transforms.Count > 0) TransformList.SelectedIndex = 0; else EnableEditor(false);
    }

    private void Rebind(int? selected = null)
    {
        int index = selected ?? _selectedIndex; TransformList.ItemsSource = null; TransformList.ItemsSource = _transforms;
        TransformList.SelectedIndex = _transforms.Count == 0 ? -1 : Math.Clamp(index, 0, _transforms.Count - 1); UpdateTotal();
    }
    private void RefreshCard() { TransformList.Items.Refresh(); UpdateTotal(); UpdateWeightLabels(WeightSlider.Value); UpdateMatrix(); SchedulePreview(); }
    private void TransformList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TransformList.SelectedIndex < 0) return; _selectedIndex = TransformList.SelectedIndex; LoadEditor(_transforms[_selectedIndex]);
    }
    private void LoadEditor(Flame3DTransform t)
    {
        _syncing = true;
        try
        {
            VariationBox.SelectedValue = t.Variation;
            VariationHint.Text = Flame3DPresets.Hint(t.Variation);
            AmountSlider.Value = t.Amount;
            ColorSpeedSlider.Value = t.ColorSpeed;
            ColorPreview.Background = new SolidColorBrush(t.Color);
            WeightSlider.Maximum = Math.Max(10, t.Weight);
            WeightSlider.Value = t.Weight;
            double[] values = Flame3DSettings.Values(t.Map);
            var boxes = MatrixBoxes();
            for (int i = 0; i < boxes.Length; i++) boxes[i].Text = F(values[i]);
            EditorTitle.Text = $"Трансформация {_selectedIndex + 1}";
            UpdateWeightLabels(t.Weight);
            UpdateMatrix();
            EnableEditor(true);
        }
        finally { _syncing = false; }
    }
    private void EnableEditor(bool value) { EditorPanel.IsEnabled = value; if (!value) EditorTitle.Text = "Нет трансформаций — нажмите «Добавить»"; }
    private void Editor_OnChanged(object sender, EventArgs e)
    {
        if (_syncing || _selectedIndex < 0) return; Flame3DTransform t = _transforms[_selectedIndex];
        PushUndo(); if (sender == VariationBox && VariationBox.SelectedValue is Flame3DVariation v) t.Variation = v;
        else if (sender is TextBox box)
        {
            if (!TryRead(box.Text, out double value) || !double.IsFinite(value) || Math.Abs(value) > 1000)
            { ValidationText.Text = "Введите конечное число не больше 1000 по модулю."; _previewTimer.Stop(); return; }
            SetValue(t.Map, Array.IndexOf(MatrixBoxes(), box), value);
        }
        VariationHint.Text = Flame3DPresets.Hint(t.Variation);
        RefreshCard();
    }
    private void Weight_OnChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateWeightLabels(e.NewValue); if (_syncing || _selectedIndex < 0) return; PushUndo(); _transforms[_selectedIndex].Weight = e.NewValue; RefreshCard();
    }
    private void Color_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedIndex < 0) return; Color initial = _transforms[_selectedIndex].Color; if (!_picker.TrySelectColor(this, initial, out Color selected)) return;
        PushUndo(); _transforms[_selectedIndex].Color = selected; ColorPreview.Background = new SolidColorBrush(selected); RefreshCard();
    }
    private void Add_OnClick(object sender, RoutedEventArgs e)
    {
        if (_transforms.Count >= 25) return; PushUndo(); _transforms.Add(new Flame3DTransform { Weight=1, Map=Ifs3DTransform.Contract(.6, .25, .1, -.2), Color=Colors.White }); EnableEditor(true); Rebind(_transforms.Count - 1); SchedulePreview();
    }
    private void Delete_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Flame3DTransform t) return; int index = _transforms.IndexOf(t); if (index < 0) return;
        PushUndo(); _transforms.RemoveAt(index); _selectedIndex = _transforms.Count == 0 ? -1 : Math.Min(index, _transforms.Count - 1); Rebind(); if (_selectedIndex < 0) EnableEditor(false); SchedulePreview();
    }
    private void Randomize_OnClick(object sender, RoutedEventArgs e)
    {
        CaptureRandomSettings();
        if (_randomSettings.Variations.Count == 0) { RandomSettingsPopup.IsOpen = true; return; }
        PersistRandomSettings(); PushUndo(); int selected = _selectedIndex; _transforms.Clear();
        _transforms.AddRange(Flame3DRandomizer.Create(_randomSettings));
        Rebind(Math.Clamp(selected, 0, _transforms.Count - 1)); if (Commit()) Randomized?.Invoke();
    }
    private void Undo_OnClick(object sender, RoutedEventArgs e)
    {
        if (_undo.Count == 0) return; Snapshot s = _undo.Pop(); _transforms.Clear(); _transforms.AddRange(s.Transforms.Select(t => t.Clone())); _selectedIndex = s.SelectedIndex; Rebind(); EnableEditor(_transforms.Count > 0); UndoButton.IsEnabled = _undo.Count > 0; SchedulePreview();
    }
    private void PushUndo()
    {
        var snapshot = new Snapshot(_transforms.Select(t => t.Clone()).ToList(), _selectedIndex); if (_undo.TryPeek(out Snapshot? last) && last.Same(snapshot)) return; _undo.Push(snapshot); UndoButton.IsEnabled = true;
    }
    private void Apply_OnClick(object sender, RoutedEventArgs e) => Commit();
    private void Done_OnClick(object sender, RoutedEventArgs e) { if (!Commit()) return; _accepted = true; DialogResult = true; }
    private bool Commit()
    {
        _previewTimer.Stop();
        if (!Validate()) return false;
        ResultTransforms = _transforms.Select(t => t.Clone()).ToList();
        _applied = ResultTransforms.Select(t => t.Clone()).ToList();
        TransformsApplied?.Invoke(ResultTransforms);
        return true;
    }
    private bool Validate()
    {
        try
        {
            if (_selectedIndex >= 0 && MatrixBoxes().Any(b => !TryRead(b.Text, out double v) || !double.IsFinite(v) || Math.Abs(v) > 1000))
                throw new InvalidOperationException("Завершите ввод чисел в матрице.");
            new Flame3DSettings { Transforms = _transforms }.Validate();
            ValidationText.Text = string.Empty;
            return true;
        }
        catch (InvalidOperationException ex) { ValidationText.Text = ex.Message; return false; }
    }
    private void SchedulePreview() { _previewTimer.Stop(); _previewTimer.Start(); }
    private void Window_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _previewTimer.Stop();
        if (!_accepted) TransformsApplied?.Invoke(_applied.Select(t => t.Clone()).ToList());
    }
    private void Amount_OnChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing || _selectedIndex < 0) return;
        PushUndo(); _transforms[_selectedIndex].Amount = e.NewValue; RefreshCard();
    }
    private void ColorSpeed_OnChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing || _selectedIndex < 0) return;
        PushUndo(); _transforms[_selectedIndex].ColorSpeed = e.NewValue; RefreshCard();
    }
    private void Duplicate_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedIndex < 0 || _transforms.Count >= 25) return;
        PushUndo(); _transforms.Insert(_selectedIndex + 1, _transforms[_selectedIndex].Clone());
        Rebind(_selectedIndex + 1); SchedulePreview();
    }
    private void Normalize_OnClick(object sender, RoutedEventArgs e)
    {
        double sum = _transforms.Sum(t => t.Weight);
        if (sum <= 0) return;
        PushUndo(); foreach (var t in _transforms) t.Weight /= sum;
        Rebind(); SchedulePreview();
    }
    private void UpdateTotal()
    {
        double total = _transforms.Sum(t => t.Weight);
        TotalWeightText.Text = $"Σ {total:F2}";
        TotalWeightText.ToolTip = "Веса относительные: любая положительная сумма допустима.";
        TotalWeightText.Foreground = total > 0 || _transforms.Count == 0
            ? (Brush)FindResource("Theme.SecondaryTextBrush") : Brushes.OrangeRed;
    }
    private void UpdateWeightLabels(double weight) { WeightText.Text = weight.ToString("F3"); double total = _transforms.Sum(t => t.Weight); if (_selectedIndex >= 0) total = total - _transforms[_selectedIndex].Weight + weight; WeightPercentText.Text = total > 0 ? $"{Math.Round(weight / total * 100):0}%" : "—"; }
    private void UpdateMatrix()
    {
        if (_selectedIndex < 0) return;
        var t = _transforms[_selectedIndex]; var m = t.Map;
        MatrixText.Text = $"{m.M11,9:F4} {m.M12,9:F4} {m.M13,9:F4} | {m.Tx,9:F4}\n" +
            $"{m.M21,9:F4} {m.M22,9:F4} {m.M23,9:F4} | {m.Ty,9:F4}\n" +
            $"{m.M31,9:F4} {m.M32,9:F4} {m.M33,9:F4} | {m.Tz,9:F4}\n\n{Flame3DPresets.Name(t.Variation)}";
    }
    private TextBox[] MatrixBoxes() => [M11Box, M12Box, M13Box, TxBox, M21Box, M22Box, M23Box, TyBox, M31Box, M32Box, M33Box, TzBox];
    private static void SetValue(Ifs3DTransform t, int index, double value)
    {
        switch (index)
        {
            case 0: t.M11 = value; break; case 1: t.M12 = value; break; case 2: t.M13 = value; break; case 3: t.Tx = value; break;
            case 4: t.M21 = value; break; case 5: t.M22 = value; break; case 6: t.M23 = value; break; case 7: t.Ty = value; break;
            case 8: t.M31 = value; break; case 9: t.M32 = value; break; case 10: t.M33 = value; break; case 11: t.Tz = value; break;
        }
    }
    private sealed record VariationOption(Flame3DVariation Value, string Name);
    private static string F(double v) => v.ToString("0.########", CultureInfo.InvariantCulture);
    private static bool TryRead(string text, out double value) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    private void InitializeRandomSettings()
    {
        _randomSettings.Normalize(); _randomSettingsSyncing = true;
        try
        {
            int[] counts = [.. Enumerable.Range(
                Flame3DRandomizationSettings.MinimumAllowedTransforms,
                Flame3DRandomizationSettings.MaximumAllowedTransforms - Flame3DRandomizationSettings.MinimumAllowedTransforms + 1)];
            MinimumTransformCountBox.ItemsSource = counts;
            MaximumTransformCountBox.ItemsSource = counts;
            MinimumTransformCountBox.SelectedItem = _randomSettings.MinimumTransforms;
            MaximumTransformCountBox.SelectedItem = _randomSettings.MaximumTransforms;

            foreach (Flame3DVariation variation in Enum.GetValues<Flame3DVariation>())
            {
                var checkBox = new CheckBox
                {
                    Content = Flame3DPresets.Name(variation), Tag = variation,
                    IsChecked = _randomSettings.Variations.Contains(variation),
                    Margin = new Thickness(0, 3, 12, 3)
                };
                checkBox.Checked += RandomVariation_OnChanged;
                checkBox.Unchecked += RandomVariation_OnChanged;
                RandomVariationPanel.Children.Add(checkBox);
            }
        }
        finally { _randomSettingsSyncing = false; }
        CaptureRandomSettings(); UpdateRandomSettingsSummary();
    }

    private void RandomCount_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_randomSettingsSyncing || MinimumTransformCountBox.SelectedItem is not int minimum || MaximumTransformCountBox.SelectedItem is not int maximum) return;
        _randomSettingsSyncing = true;
        try
        {
            if (sender == MinimumTransformCountBox && minimum > maximum) MaximumTransformCountBox.SelectedItem = minimum;
            else if (sender == MaximumTransformCountBox && maximum < minimum) MinimumTransformCountBox.SelectedItem = maximum;
        }
        finally { _randomSettingsSyncing = false; }
        CaptureRandomSettings(); UpdateRandomSettingsSummary();
    }

    private void RandomVariation_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_randomSettingsSyncing) return;
        CaptureRandomSettings(); UpdateRandomSettingsSummary();
    }

    private void SelectAllVariations_OnClick(object sender, RoutedEventArgs e) => SetAllVariationChecks(true);
    private void ClearVariations_OnClick(object sender, RoutedEventArgs e) => SetAllVariationChecks(false);
    private void SetAllVariationChecks(bool isChecked)
    {
        _randomSettingsSyncing = true;
        try
        {
            foreach (CheckBox checkBox in RandomVariationPanel.Children.OfType<CheckBox>())
                checkBox.IsChecked = isChecked;
        }
        finally { _randomSettingsSyncing = false; }
        CaptureRandomSettings(); UpdateRandomSettingsSummary();
    }

    private void CaptureRandomSettings()
    {
        if (MinimumTransformCountBox.SelectedItem is int minimum) _randomSettings.MinimumTransforms = minimum;
        if (MaximumTransformCountBox.SelectedItem is int maximum) _randomSettings.MaximumTransforms = maximum;
        _randomSettings.Variations = [.. RandomVariationPanel.Children.OfType<CheckBox>()
            .Where(checkBox => checkBox.IsChecked == true)
            .Select(checkBox => checkBox.Tag)
            .OfType<Flame3DVariation>()];
        _randomSettings.Normalize();
    }

    private void UpdateRandomSettingsSummary()
    {
        int variationCount = _randomSettings.Variations.Count;
        bool exactCount = _randomSettings.MinimumTransforms == _randomSettings.MaximumTransforms;
        RandomCountHintText.Text = exactCount ? $"ровно {_randomSettings.MinimumTransforms}" : string.Empty;
        RandomSettingsValidationText.Text = variationCount == 0 ? "Выберите хотя бы одну допустимую вариацию." : string.Empty;
        RandomSettingsSummaryText.Text = exactCount
            ? $"Будет создано слоёв: {_randomSettings.MinimumTransforms}. Вариаций в наборе: {variationCount}."
            : $"Будет создано слоёв: {_randomSettings.MinimumTransforms}–{_randomSettings.MaximumTransforms}. Вариаций в наборе: {variationCount}.";
        RandomizeButton.IsEnabled = variationCount > 0;
        RandomizeButton.Content = exactCount
            ? $"Случайно · {_randomSettings.MinimumTransforms}"
            : $"Случайно · {_randomSettings.MinimumTransforms}–{_randomSettings.MaximumTransforms}";
        RandomizeButton.ToolTip = exactCount
            ? $"Создать ровно {_randomSettings.MinimumTransforms} случайных трансформаций"
            : $"Создать от {_randomSettings.MinimumTransforms} до {_randomSettings.MaximumTransforms} случайных трансформаций";
    }

    private void RandomSettingsPopup_OnClosed(object sender, EventArgs e)
    {
        CaptureRandomSettings(); PersistRandomSettings();
    }

    private void PersistRandomSettings()
    {
        try
        {
            Flame3DRandomizationSettingsStore.Save(_randomSettings);
            RandomSettingsButton.ToolTip = "Настроить случайную генерацию";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            RandomSettingsButton.ToolTip = $"Настройки действуют в этом сеансе, но не сохранены: {exception.Message}";
        }
    }

    private sealed record Snapshot(List<Flame3DTransform> Transforms, int SelectedIndex)
    {
        public bool Same(Snapshot other) => SelectedIndex == other.SelectedIndex && Transforms.Count == other.Transforms.Count &&
            Transforms.Zip(other.Transforms).All(pair => pair.First.Weight == pair.Second.Weight &&
                pair.First.Variation == pair.Second.Variation && pair.First.Color == pair.Second.Color &&
                pair.First.Amount == pair.Second.Amount && pair.First.ColorSpeed == pair.Second.ColorSpeed &&
                Flame3DSettings.Values(pair.First.Map).SequenceEqual(Flame3DSettings.Values(pair.Second.Map)));
    }
}
