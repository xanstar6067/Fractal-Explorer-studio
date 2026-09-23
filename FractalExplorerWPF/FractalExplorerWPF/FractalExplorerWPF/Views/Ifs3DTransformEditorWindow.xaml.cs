using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace FractalExplorerWPF.Views;

public partial class Ifs3DTransformEditorWindow : Window
{
    private static readonly NamedOption<Ifs3DPlacementMode>[] PlacementOptions =
    [
        new(Ifs3DPlacementMode.Free, "Свободное"),
        new(Ifs3DPlacementMode.Spherical, "По сфере"),
        new(Ifs3DPlacementMode.Helix, "Объёмная спираль"),
        new(Ifs3DPlacementMode.Bilateral, "Зеркальное")
    ];

    private static readonly NamedOption<Ifs3DProbabilityMode>[] ProbabilityOptions =
    [
        new(Ifs3DProbabilityMode.VolumeWeighted, "По объёму преобразования"),
        new(Ifs3DProbabilityMode.Uniform, "Равномерные"),
        new(Ifs3DProbabilityMode.Random, "Случайные")
    ];

    private readonly List<Ifs3DTransform> _transforms = [];
    private List<Ifs3DTransform> _applied = [];
    private readonly Stack<Snapshot> _undo = new();
    private readonly Ifs3DRandomizationSettings _randomSettings = Ifs3DRandomizationSettingsStore.Load();
    private readonly DispatcherTimer _previewTimer;
    private int _selectedIndex = -1;
    private bool _syncing;
    private bool _randomSettingsSyncing;
    private bool _initialized;
    private bool _accepted;

    public event Action<IReadOnlyList<Ifs3DTransform>>? TransformsPreviewed;
    public event Action? Randomized;

    public Ifs3DTransformEditorWindow(IEnumerable<Ifs3DTransform> source)
    {
        InitializeComponent();
        _transforms.AddRange(source.Select(transform => transform.Clone()));
        _applied = CloneTransforms(_transforms);
        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(320) };
        _previewTimer.Tick += (_, _) => { _previewTimer.Stop(); Preview(); };
        _initialized = true;
        InitializeRandomSettings();
        Rebind(0);
        EnableEditor(_transforms.Count > 0);
    }

    private static List<Ifs3DTransform> CloneTransforms(IEnumerable<Ifs3DTransform> source) =>
        source.Select(transform => transform.Clone()).ToList();

    private void Rebind(int selected)
    {
        _syncing = true;
        TransformList.ItemsSource = null;
        TransformList.ItemsSource = _transforms;
        TransformList.SelectedIndex = _transforms.Count == 0 ? -1 : Math.Clamp(selected, 0, _transforms.Count - 1);
        _selectedIndex = TransformList.SelectedIndex;
        _syncing = false;
        if (_selectedIndex >= 0) LoadEditor(_transforms[_selectedIndex]);
        else EnableEditor(false);
        UpdateTotal();
    }

    private void TransformList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _syncing || TransformList.SelectedIndex < 0) return;
        _selectedIndex = TransformList.SelectedIndex;
        LoadEditor(_transforms[_selectedIndex]);
    }

    private void LoadEditor(Ifs3DTransform transform)
    {
        _syncing = true;
        try
        {
            double[] values = Values(transform);
            TextBox[] boxes = MatrixBoxes();
            for (int i = 0; i < boxes.Length; i++) boxes[i].Text = F(values[i]);
            ProbabilitySlider.Value = Math.Clamp(transform.Probability, 0, 10);
            EditorTitle.Text = $"Преобразование {_selectedIndex + 1}";
            UpdateProbability(transform.Probability);
            UpdateMatrix();
            EnableEditor(true);
            ValidationText.Text = string.Empty;
        }
        finally { _syncing = false; }
    }

    private void EnableEditor(bool enabled)
    {
        EditorPanel.IsEnabled = enabled;
        if (!enabled) EditorTitle.Text = "Нет преобразований — нажмите «Добавить»";
    }

    private void Matrix_OnChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initialized || _syncing || _selectedIndex < 0 || sender is not TextBox box) return;
        if (!Read(box.Text, out double value) || !double.IsFinite(value) || Math.Abs(value) > 1000)
        {
            ValidationText.Text = "Введите конечное число не больше 1000 по модулю.";
            _previewTimer.Stop();
            return;
        }
        int index = Array.IndexOf(MatrixBoxes(), box);
        if (index < 0) return;
        PushUndo();
        SetValue(_transforms[_selectedIndex], index, value);
        Refresh();
        SchedulePreview();
    }

    private void Probability_OnChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized) return;
        UpdateProbability(e.NewValue);
        if (_syncing || _selectedIndex < 0) return;
        PushUndo();
        _transforms[_selectedIndex].Probability = e.NewValue;
        Refresh();
        SchedulePreview();
    }

    private void Refresh()
    {
        TransformList.Items.Refresh();
        UpdateTotal();
        UpdateProbability(_selectedIndex < 0 ? 0 : _transforms[_selectedIndex].Probability);
        UpdateMatrix();
        ValidationText.Text = string.Empty;
    }

    private void UpdateTotal()
    {
        double total = _transforms.Sum(transform => Math.Max(0, transform.Probability));
        TotalText.Text = $"Σ {total:F4}";
        TotalText.Foreground = Math.Abs(total - 1) < .0001 || _transforms.Count == 0
            ? (Brush)FindResource("Theme.SecondaryTextBrush") : Brushes.OrangeRed;
    }

    private void UpdateProbability(double value)
    {
        ProbabilityText.Text = value.ToString("F3", CultureInfo.InvariantCulture);
        double total = _transforms.Sum(transform => Math.Max(0, transform.Probability));
        ProbabilityPercentText.Text = total > 0 ? $"{Math.Round(value / total * 100):0}%" : "—";
    }

    private void UpdateMatrix()
    {
        if (_selectedIndex < 0) { MatrixText.Text = string.Empty; return; }
        double[] v = Values(_transforms[_selectedIndex]);
        MatrixText.Text = $"┌ {v[0],9:F5} {v[1],9:F5} {v[2],9:F5} │ {v[3],9:F5} ┐\n" +
                          $"│ {v[4],9:F5} {v[5],9:F5} {v[6],9:F5} │ {v[7],9:F5} │\n" +
                          $"└ {v[8],9:F5} {v[9],9:F5} {v[10],9:F5} │ {v[11],9:F5} ┘";
    }

    private void Add_OnClick(object sender, RoutedEventArgs e)
    {
        PushUndo();
        _transforms.Add(Ifs3DTransform.Contract(.5, .25, .25, .25));
        Rebind(_transforms.Count - 1);
        SchedulePreview();
    }

    private void Duplicate_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedIndex < 0) return;
        PushUndo();
        _transforms.Insert(_selectedIndex + 1, _transforms[_selectedIndex].Clone());
        Rebind(_selectedIndex + 1);
        SchedulePreview();
    }

    private void Delete_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Ifs3DTransform transform) return;
        int index = _transforms.IndexOf(transform);
        if (index < 0) return;
        PushUndo();
        _transforms.RemoveAt(index);
        Rebind(index);
        SchedulePreview();
    }

    private void Normalize_OnClick(object sender, RoutedEventArgs e)
    {
        if (_transforms.Count == 0) return;
        PushUndo();
        Ifs3DRandomizer.NormalizeProbabilities(_transforms);
        Rebind(_selectedIndex);
        SchedulePreview();
    }

    private void Randomize_OnClick(object sender, RoutedEventArgs e)
    {
        CaptureRandomSettings();
        if (_randomSettings.Families.Count == 0)
        {
            RandomSettingsButton.IsChecked = true;
            return;
        }
        SaveRandomSettings();
        PushUndo();
        int selected = _selectedIndex;
        _transforms.Clear();
        _transforms.AddRange(Ifs3DRandomizer.Create(_randomSettings));
        Rebind(selected);
        if (Commit()) Randomized?.Invoke(); // The 2D IFS/Flame random button also updates the parent immediately.
    }

    private void Undo_OnClick(object sender, RoutedEventArgs e)
    {
        if (_undo.Count == 0) return;
        Snapshot snapshot = _undo.Pop();
        _transforms.Clear();
        _transforms.AddRange(CloneTransforms(snapshot.Transforms));
        Rebind(snapshot.SelectedIndex);
        UndoButton.IsEnabled = _undo.Count > 0;
        SchedulePreview();
    }

    private void PushUndo()
    {
        var snapshot = new Snapshot(CloneTransforms(_transforms), _selectedIndex);
        if (_undo.TryPeek(out Snapshot? previous) && previous.Same(snapshot)) return;
        _undo.Push(snapshot);
        UndoButton.IsEnabled = true;
    }

    private void SchedulePreview()
    {
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void Preview()
    {
        if (!Validate(out _)) return;
        TransformsPreviewed?.Invoke(CloneTransforms(_transforms));
    }

    private void Apply_OnClick(object sender, RoutedEventArgs e) => Commit();

    private void Done_OnClick(object sender, RoutedEventArgs e)
    {
        if (!Commit()) return;
        _accepted = true;
        DialogResult = true;
    }

    private bool Commit()
    {
        _previewTimer.Stop();
        if (!Validate(out string error))
        {
            MessageBox.Show(this, error, "Объёмный IFS", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        _applied = CloneTransforms(_transforms);
        TransformsPreviewed?.Invoke(CloneTransforms(_applied));
        return true;
    }

    private bool Validate(out string error)
    {
        if (_transforms.Count == 0) { error = "Добавьте хотя бы одно преобразование."; return false; }
        if (_transforms.Sum(transform => transform.Probability) <= 0)
        { error = "Сумма весов должна быть положительной."; return false; }
        foreach (Ifs3DTransform transform in _transforms)
        {
            if (!double.IsFinite(transform.Probability) || transform.Probability < 0 ||
                Values(transform).Any(value => !double.IsFinite(value) || Math.Abs(value) > 1000))
            { error = "Матрица содержит недопустимое значение."; return false; }
        }
        if (_selectedIndex >= 0 && MatrixBoxes().Any(box =>
            !Read(box.Text, out double value) || !double.IsFinite(value) || Math.Abs(value) > 1000))
        { error = "Завершите ввод чисел в матрице."; return false; }
        error = string.Empty;
        return true;
    }

    private void Window_OnClosing(object? sender, CancelEventArgs e)
    {
        _previewTimer.Stop();
        if (!_accepted) TransformsPreviewed?.Invoke(CloneTransforms(_applied));
    }

    private void InitializeRandomSettings()
    {
        _randomSettings.Normalize();
        _randomSettingsSyncing = true;
        try
        {
            int[] counts = Enumerable.Range(Ifs3DRandomizationSettings.MinimumAllowedTransforms,
                Ifs3DRandomizationSettings.MaximumAllowedTransforms).ToArray();
            MinimumTransformCountBox.ItemsSource = counts;
            MaximumTransformCountBox.ItemsSource = counts;
            MinimumTransformCountBox.SelectedItem = _randomSettings.MinimumTransforms;
            MaximumTransformCountBox.SelectedItem = _randomSettings.MaximumTransforms;
            PlacementModeBox.ItemsSource = PlacementOptions;
            PlacementModeBox.SelectedItem = PlacementOptions.First(option => option.Value == _randomSettings.PlacementMode);
            ProbabilityModeBox.ItemsSource = ProbabilityOptions;
            ProbabilityModeBox.SelectedItem = ProbabilityOptions.First(option => option.Value == _randomSettings.ProbabilityMode);
            foreach (Ifs3DTransformFamily family in Enum.GetValues<Ifs3DTransformFamily>())
            {
                var checkBox = new CheckBox
                {
                    Content = FamilyName(family), Tag = family,
                    IsChecked = _randomSettings.Families.Contains(family),
                    Margin = new Thickness(0, 3, 8, 3)
                };
                checkBox.Checked += RandomFamily_OnChanged;
                checkBox.Unchecked += RandomFamily_OnChanged;
                RandomFamilyPanel.Children.Add(checkBox);
            }
        }
        finally { _randomSettingsSyncing = false; }
        CaptureRandomSettings();
        UpdateRandomSettingsSummary();
    }

    private void RandomCount_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_randomSettingsSyncing || MinimumTransformCountBox.SelectedItem is not int minimum ||
            MaximumTransformCountBox.SelectedItem is not int maximum) return;
        _randomSettingsSyncing = true;
        try
        {
            if (minimum > maximum)
            {
                if (sender == MinimumTransformCountBox) MaximumTransformCountBox.SelectedItem = minimum;
                else MinimumTransformCountBox.SelectedItem = maximum;
            }
        }
        finally { _randomSettingsSyncing = false; }
        CaptureRandomSettings();
        UpdateRandomSettingsSummary();
    }

    private void RandomOption_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_randomSettingsSyncing) return;
        CaptureRandomSettings();
        UpdateRandomSettingsSummary();
    }

    private void RandomFamily_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_randomSettingsSyncing) return;
        CaptureRandomSettings();
        UpdateRandomSettingsSummary();
    }

    private void SelectAllFamilies_OnClick(object sender, RoutedEventArgs e) => SetAllFamilies(true);
    private void ClearFamilies_OnClick(object sender, RoutedEventArgs e) => SetAllFamilies(false);

    private void SetAllFamilies(bool selected)
    {
        _randomSettingsSyncing = true;
        try
        {
            foreach (CheckBox checkBox in RandomFamilyPanel.Children.OfType<CheckBox>()) checkBox.IsChecked = selected;
        }
        finally { _randomSettingsSyncing = false; }
        CaptureRandomSettings();
        UpdateRandomSettingsSummary();
    }

    private void RandomSettingsPopup_OnClosed(object? sender, EventArgs e)
    {
        RandomSettingsButton.IsChecked = false;
        CaptureRandomSettings();
        SaveRandomSettings();
    }

    private void CaptureRandomSettings()
    {
        if (MinimumTransformCountBox.SelectedItem is int minimum) _randomSettings.MinimumTransforms = minimum;
        if (MaximumTransformCountBox.SelectedItem is int maximum) _randomSettings.MaximumTransforms = maximum;
        if (PlacementModeBox.SelectedItem is NamedOption<Ifs3DPlacementMode> placement)
            _randomSettings.PlacementMode = placement.Value;
        if (ProbabilityModeBox.SelectedItem is NamedOption<Ifs3DProbabilityMode> probability)
            _randomSettings.ProbabilityMode = probability.Value;
        _randomSettings.Families = RandomFamilyPanel.Children.OfType<CheckBox>()
            .Where(checkBox => checkBox.IsChecked == true)
            .Select(checkBox => (Ifs3DTransformFamily)checkBox.Tag).ToList();
        _randomSettings.Normalize();
    }

    private void UpdateRandomSettingsSummary()
    {
        bool enabled = _randomSettings.Families.Count > 0;
        string count = _randomSettings.MinimumTransforms == _randomSettings.MaximumTransforms
            ? _randomSettings.MinimumTransforms.ToString(CultureInfo.InvariantCulture)
            : $"{_randomSettings.MinimumTransforms}–{_randomSettings.MaximumTransforms}";
        RandomizeButton.IsEnabled = enabled;
        RandomizeButton.Content = $"Случайно · {count}";
        RandomCountHintText.Text = _randomSettings.MinimumTransforms == _randomSettings.MaximumTransforms
            ? $"ровно {_randomSettings.MinimumTransforms}"
            : $"от {_randomSettings.MinimumTransforms} до {_randomSettings.MaximumTransforms}";
        RandomSettingsValidationText.Text = enabled ? string.Empty : "Выберите хотя бы одно семейство преобразований.";
        RandomSettingsSummaryText.Text = enabled
            ? $"Количество: {count}. Семейств: {_randomSettings.Families.Count}. Расположение: {PlacementOptions.First(option => option.Value == _randomSettings.PlacementMode).Name}."
            : "Случайная генерация отключена, пока список семейств пуст.";
    }

    private void SaveRandomSettings()
    {
        try { Ifs3DRandomizationSettingsStore.Save(_randomSettings); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { RandomSettingsButton.ToolTip = $"Настройки действуют до закрытия приложения: {exception.Message}"; }
    }

    private TextBox[] MatrixBoxes() =>
        [M11Box, M12Box, M13Box, TxBox, M21Box, M22Box, M23Box, TyBox,
            M31Box, M32Box, M33Box, TzBox];

    private static double[] Values(Ifs3DTransform t) =>
        [t.M11, t.M12, t.M13, t.Tx, t.M21, t.M22, t.M23, t.Ty,
            t.M31, t.M32, t.M33, t.Tz];

    private static void SetValue(Ifs3DTransform t, int index, double value)
    {
        switch (index)
        {
            case 0: t.M11 = value; break; case 1: t.M12 = value; break;
            case 2: t.M13 = value; break; case 3: t.Tx = value; break;
            case 4: t.M21 = value; break; case 5: t.M22 = value; break;
            case 6: t.M23 = value; break; case 7: t.Ty = value; break;
            case 8: t.M31 = value; break; case 9: t.M32 = value; break;
            case 10: t.M33 = value; break; case 11: t.Tz = value; break;
        }
    }

    private static string FamilyName(Ifs3DTransformFamily family) => family switch
    {
        Ifs3DTransformFamily.Similarity => "Поворот и масштаб",
        Ifs3DTransformFamily.Anisotropic => "Анизотропное сжатие",
        Ifs3DTransformFamily.Shear => "Сдвиг по осям",
        Ifs3DTransformFamily.Reflection => "Отражение",
        Ifs3DTransformFamily.Stem => "Тонкое / стволовое",
        Ifs3DTransformFamily.Sheet => "Плоское / листовое",
        _ => family.ToString()
    };

    private static string F(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);
    private static bool Read(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    private sealed record NamedOption<T>(T Value, string Name)
    {
        public override string ToString() => Name;
    }

    private sealed record Snapshot(List<Ifs3DTransform> Transforms, int SelectedIndex)
    {
        public bool Same(Snapshot other) => SelectedIndex == other.SelectedIndex &&
            Transforms.Count == other.Transforms.Count &&
            Transforms.Zip(other.Transforms).All(pair =>
                Values(pair.First).SequenceEqual(Values(pair.Second)) &&
                pair.First.Probability == pair.Second.Probability);
    }
}
