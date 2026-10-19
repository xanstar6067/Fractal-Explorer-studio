using FractalExplorerWPF.Controls;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace FractalExplorerWPF.Views;

public partial class IfsTransformEditorWindow : Window
{
    private static readonly NamedOption<IfsPlacementMode>[] PlacementOptions =
    [
        new(IfsPlacementMode.Free, "Свободное"),
        new(IfsPlacementMode.Radial, "Радиальное"),
        new(IfsPlacementMode.Bilateral, "Зеркальное")
    ];

    private static readonly NamedOption<IfsProbabilityMode>[] ProbabilityOptions =
    [
        new(IfsProbabilityMode.AreaWeighted, "По площади преобразования"),
        new(IfsProbabilityMode.Uniform, "Равномерные"),
        new(IfsProbabilityMode.Random, "Случайные")
    ];

    private readonly List<IfsAffineTransform> _transforms;
    private readonly Stack<Snapshot> _undo = new();
    private readonly IfsRandomizationSettings _randomSettings = IfsRandomizationSettingsStore.Load();
    private int _selectedIndex = -1;
    private bool _syncing;
    private bool _randomSettingsSyncing;

    public List<IfsAffineTransform> ResultTransforms { get; private set; }
    public event Action<IReadOnlyList<IfsAffineTransform>>? TransformsApplied;

    public IfsTransformEditorWindow(IEnumerable<IfsAffineTransform> source)
    {
        InitializeComponent();
        InitializeRandomSettings();
        _transforms = source.Select(t => t.Clone()).ToList();
        ResultTransforms = _transforms;
        Rebind();
        if (_transforms.Count > 0)
            TransformList.SelectedIndex = 0;
        else
            EnableEditor(false);
        InitializeNumericInput();
    }

    private void Rebind(int? selected = null)
    {
        int index = selected ?? _selectedIndex;
        TransformList.ItemsSource = null;
        TransformList.ItemsSource = _transforms;
        TransformList.SelectedIndex = _transforms.Count == 0 ? -1 : Math.Clamp(index, 0, _transforms.Count - 1);
        UpdateTotal();
    }

    private void TransformList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TransformList.SelectedIndex < 0)
            return;

        _selectedIndex = TransformList.SelectedIndex;
        LoadEditor(_transforms[_selectedIndex]);
    }

    private void LoadEditor(IfsAffineTransform transform)
    {
        _syncing = true;
        try
        {
            ABox.Text = F(transform.A);
            BBox.Text = F(transform.B);
            CBox.Text = F(transform.C);
            DBox.Text = F(transform.D);
            EBox.Text = F(transform.E);
            FBox.Text = F(transform.F);
            ProbabilitySlider.Value = Math.Clamp(transform.Probability, 0, 1);
            EditorTitle.Text = $"Преобразование {_selectedIndex + 1}";
            UpdateProbability(transform.Probability);
            UpdateMatrix();
            EnableEditor(true);
        }
        finally
        {
            _syncing = false;
        }
    }

    private void EnableEditor(bool value)
    {
        EditorPanel.IsEnabled = value;
        if (!value)
            EditorTitle.Text = "Нет преобразований — нажмите «Добавить»";
    }

    private void Editor_OnChanged(object sender, EventArgs e)
    {
        if (sender is TextBox edited && !NumericInput.IsValid(edited)) return;
        if (_syncing || _selectedIndex < 0 || sender is not TextBox box || !Read(box.Text, out double value))
            return;

        PushUndo();
        IfsAffineTransform transform = _transforms[_selectedIndex];
        if (box == ABox) transform.A = value;
        else if (box == BBox) transform.B = value;
        else if (box == CBox) transform.C = value;
        else if (box == DBox) transform.D = value;
        else if (box == EBox) transform.E = value;
        else if (box == FBox) transform.F = value;
        Refresh();
    }

    private void Probability_OnChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateProbability(e.NewValue);
        if (_syncing || _selectedIndex < 0)
            return;

        PushUndo();
        _transforms[_selectedIndex].Probability = e.NewValue;
        Refresh();
    }

    private void Refresh()
    {
        TransformList.Items.Refresh();
        UpdateTotal();
        UpdateProbability(ProbabilitySlider.Value);
        UpdateMatrix();
    }

    private void UpdateProbability(double probability)
    {
        ProbabilityText.Text = probability.ToString("F3");
        double total = _transforms.Sum(t => Math.Max(0, t.Probability));
        if (_selectedIndex >= 0)
            total = total - Math.Max(0, _transforms[_selectedIndex].Probability) + probability;
        ProbabilityPercentText.Text = total > 0 ? $"{Math.Round(probability / total * 100):0}%" : "—";
    }

    private void UpdateTotal()
    {
        double total = _transforms.Sum(t => Math.Max(0, t.Probability));
        TotalText.Text = $"Σ {total:F4}";
        TotalText.Foreground = Math.Abs(total - 1) < .0001 || _transforms.Count == 0
            ? (Brush)FindResource("Theme.SecondaryTextBrush")
            : Brushes.OrangeRed;
    }

    private void UpdateMatrix()
    {
        if (_selectedIndex < 0)
            return;

        IfsAffineTransform transform = _transforms[_selectedIndex];
        MatrixText.Text = $"┌ {transform.A,10:F6}  {transform.B,10:F6}  {transform.E,10:F6} ┐\n" +
                          $"└ {transform.C,10:F6}  {transform.D,10:F6}  {transform.F,10:F6} ┘";
    }

    private void Add_OnClick(object sender, RoutedEventArgs e)
    {
        PushUndo();
        _transforms.Add(new IfsAffineTransform { A = .5, D = .5, Probability = .5 });
        EnableEditor(true);
        Rebind(_transforms.Count - 1);
    }

    private void Delete_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not IfsAffineTransform transform)
            return;

        int index = _transforms.IndexOf(transform);
        if (index < 0)
            return;

        PushUndo();
        _transforms.RemoveAt(index);
        _selectedIndex = _transforms.Count == 0 ? -1 : Math.Min(index, _transforms.Count - 1);
        Rebind();
        if (_selectedIndex < 0)
            EnableEditor(false);
    }

    private void Normalize_OnClick(object sender, RoutedEventArgs e)
    {
        if (_transforms.Count == 0)
            return;

        PushUndo();
        IfsRandomizer.NormalizeProbabilities(_transforms);
        Rebind(_selectedIndex);
    }

    private void Randomize_OnClick(object sender, RoutedEventArgs e)
    {
        CaptureRandomSettings();
        if (_randomSettings.Families.Count == 0)
        {
            UpdateRandomSettingsSummary();
            RandomSettingsButton.IsChecked = true;
            return;
        }

        SaveRandomSettings();
        PushUndo();
        int selected = _selectedIndex;
        _transforms.Clear();
        _transforms.AddRange(IfsRandomizer.Create(_randomSettings));
        Rebind(Math.Clamp(selected, 0, _transforms.Count - 1));
        EnableEditor(true);
        Commit();
    }

    private void Undo_OnClick(object sender, RoutedEventArgs e)
    {
        if (_undo.Count == 0)
            return;

        Snapshot snapshot = _undo.Pop();
        _transforms.Clear();
        _transforms.AddRange(snapshot.Transforms.Select(t => t.Clone()));
        _selectedIndex = snapshot.SelectedIndex;
        Rebind();
        EnableEditor(_transforms.Count > 0);
        UndoButton.IsEnabled = _undo.Count > 0;
    }

    private void PushUndo()
    {
        var snapshot = new Snapshot(_transforms.Select(t => t.Clone()).ToList(), _selectedIndex);
        if (_undo.TryPeek(out Snapshot? old) && old.Same(snapshot))
            return;

        _undo.Push(snapshot);
        UndoButton.IsEnabled = true;
    }

    private void Apply_OnClick(object sender, RoutedEventArgs e) => Commit();

    private void Done_OnClick(object sender, RoutedEventArgs e)
    {
        if (_transforms.Count == 0)
        {
            MessageBox.Show(this, "Добавьте хотя бы одно преобразование.", "IFS", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Commit();
        DialogResult = true;
    }

    private void Commit()
    {
        NumericInput.CommitAll(this);
        if (_transforms.Count == 0)
            return;

        ResultTransforms = _transforms.Select(t => t.Clone()).ToList();
        TransformsApplied?.Invoke(ResultTransforms);
    }

    private void InitializeRandomSettings()
    {
        _randomSettings.Normalize();
        _randomSettingsSyncing = true;
        try
        {
            int[] counts = Enumerable.Range(
                IfsRandomizationSettings.MinimumAllowedTransforms,
                IfsRandomizationSettings.MaximumAllowedTransforms -
                IfsRandomizationSettings.MinimumAllowedTransforms + 1).ToArray();
            MinimumTransformCountBox.ItemsSource = counts;
            MaximumTransformCountBox.ItemsSource = counts;
            MinimumTransformCountBox.SelectedItem = _randomSettings.MinimumTransforms;
            MaximumTransformCountBox.SelectedItem = _randomSettings.MaximumTransforms;

            PlacementModeBox.ItemsSource = PlacementOptions;
            PlacementModeBox.SelectedItem = PlacementOptions.First(option => option.Value == _randomSettings.PlacementMode);
            ProbabilityModeBox.ItemsSource = ProbabilityOptions;
            ProbabilityModeBox.SelectedItem = ProbabilityOptions.First(option => option.Value == _randomSettings.ProbabilityMode);

            RandomFamilyPanel.Children.Clear();
            foreach (IfsTransformFamily family in Enum.GetValues<IfsTransformFamily>())
            {
                var checkBox = new CheckBox
                {
                    Content = GetFamilyName(family),
                    Tag = family,
                    IsChecked = _randomSettings.Families.Contains(family),
                    Margin = new Thickness(0, 3, 8, 3)
                };
                checkBox.Checked += RandomFamily_OnChanged;
                checkBox.Unchecked += RandomFamily_OnChanged;
                RandomFamilyPanel.Children.Add(checkBox);
            }
        }
        finally
        {
            _randomSettingsSyncing = false;
        }

        CaptureRandomSettings();
        UpdateRandomSettingsSummary();
    }

    private void RandomCount_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_randomSettingsSyncing ||
            MinimumTransformCountBox.SelectedItem is not int minimum ||
            MaximumTransformCountBox.SelectedItem is not int maximum)
            return;

        _randomSettingsSyncing = true;
        try
        {
            if (minimum > maximum)
            {
                if (sender == MinimumTransformCountBox)
                    MaximumTransformCountBox.SelectedItem = minimum;
                else
                    MinimumTransformCountBox.SelectedItem = maximum;
            }
        }
        finally
        {
            _randomSettingsSyncing = false;
        }

        CaptureRandomSettings();
        UpdateRandomSettingsSummary();
    }

    private void RandomOption_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_randomSettingsSyncing)
            return;

        CaptureRandomSettings();
        UpdateRandomSettingsSummary();
    }

    private void RandomFamily_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_randomSettingsSyncing)
            return;

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
            foreach (CheckBox checkBox in RandomFamilyPanel.Children.OfType<CheckBox>())
                checkBox.IsChecked = selected;
        }
        finally
        {
            _randomSettingsSyncing = false;
        }

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
        if (MinimumTransformCountBox.SelectedItem is int minimum)
            _randomSettings.MinimumTransforms = minimum;
        if (MaximumTransformCountBox.SelectedItem is int maximum)
            _randomSettings.MaximumTransforms = maximum;
        if (PlacementModeBox.SelectedItem is NamedOption<IfsPlacementMode> placement)
            _randomSettings.PlacementMode = placement.Value;
        if (ProbabilityModeBox.SelectedItem is NamedOption<IfsProbabilityMode> probability)
            _randomSettings.ProbabilityMode = probability.Value;

        _randomSettings.Families = RandomFamilyPanel.Children.OfType<CheckBox>()
            .Where(checkBox => checkBox.IsChecked == true)
            .Select(checkBox => (IfsTransformFamily)checkBox.Tag)
            .ToList();
        _randomSettings.Normalize();
    }

    private void UpdateRandomSettingsSummary()
    {
        bool hasFamilies = _randomSettings.Families.Count > 0;
        string count = _randomSettings.MinimumTransforms == _randomSettings.MaximumTransforms
            ? _randomSettings.MinimumTransforms.ToString(CultureInfo.InvariantCulture)
            : $"{_randomSettings.MinimumTransforms}–{_randomSettings.MaximumTransforms}";
        string placement = PlacementOptions.First(option => option.Value == _randomSettings.PlacementMode).Name;

        RandomizeButton.IsEnabled = hasFamilies;
        RandomizeButton.Content = $"Случайно · {count}";
        RandomCountHintText.Text = _randomSettings.MinimumTransforms == _randomSettings.MaximumTransforms
            ? $"ровно {_randomSettings.MinimumTransforms}"
            : $"от {_randomSettings.MinimumTransforms} до {_randomSettings.MaximumTransforms}";
        RandomSettingsValidationText.Text = hasFamilies
            ? string.Empty
            : "Выберите хотя бы одно семейство преобразований.";
        RandomSettingsValidationText.Visibility = hasFamilies ? Visibility.Collapsed : Visibility.Visible;
        RandomSettingsSummaryText.Text = hasFamilies
            ? $"Количество: {count}. Семейств: {_randomSettings.Families.Count}. Расположение: {placement}."
            : "Случайная генерация отключена, пока список семейств пуст.";
        RandomSettingsButton.ToolTip = hasFamilies
            ? "Настроить случайную генерацию"
            : "Настройте случайную генерацию: список семейств пуст";
    }

    private void SaveRandomSettings()
    {
        try
        {
            IfsRandomizationSettingsStore.Save(_randomSettings);
        }
        catch (IOException)
        {
            RandomSettingsButton.ToolTip = "Не удалось сохранить настройки случайной генерации";
        }
        catch (UnauthorizedAccessException)
        {
            RandomSettingsButton.ToolTip = "Нет доступа для сохранения настроек случайной генерации";
        }
    }

    private static string GetFamilyName(IfsTransformFamily family) => family switch
    {
        IfsTransformFamily.Similarity => "Поворот и масштаб",
        IfsTransformFamily.Anisotropic => "Анизотропное сжатие",
        IfsTransformFamily.Shear => "Сдвиг (shear)",
        IfsTransformFamily.Reflection => "Отражение",
        IfsTransformFamily.Stem => "Тонкое / стволовое",
        _ => family.ToString()
    };

    private static string F(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);

    private static bool Read(string text, out double value) => (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)) && double.IsFinite(value);

    private sealed record NamedOption<T>(T Value, string Name)
    {
        public override string ToString() => Name;
    }

    private sealed record Snapshot(List<IfsAffineTransform> Transforms, int SelectedIndex)
    {
        public bool Same(Snapshot other) =>
            SelectedIndex == other.SelectedIndex &&
            Transforms.Count == other.Transforms.Count &&
            Transforms.Zip(other.Transforms).All(pair => Equal(pair.First, pair.Second));

        private static bool Equal(IfsAffineTransform first, IfsAffineTransform second) =>
            first.A == second.A && first.B == second.B && first.C == second.C &&
            first.D == second.D && first.E == second.E && first.F == second.F &&
            first.Probability == second.Probability;
    }
}
