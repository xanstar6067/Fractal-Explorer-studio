using System.Windows;
using System.Windows.Controls;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class SerpinskyPaletteWindow : Window
{
    private readonly SerpinskyPaletteManager _manager;
    private SerpinskyPalette? _selected;

    public SerpinskyPaletteWindow(SerpinskyPaletteManager manager)
    {
        InitializeComponent();
        _manager = manager;
        CustomPaletteFile.ShowLoadWarning(this, manager.LoadWarning);
        RefreshList(_manager.ActivePalette);
    }

    private void RefreshList(SerpinskyPalette? select)
    {
        PaletteList.ItemsSource = null;
        PaletteList.ItemsSource = _manager.Palettes;
        PaletteList.SelectedItem = select ?? _manager.Palettes.FirstOrDefault();
    }

    private void PaletteList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PaletteList.SelectedItem is not SerpinskyPalette palette)
        {
            return;
        }

        _selected = palette;
        NameBox.Text = palette.Name;
        FractalColorSelector.SelectedColor = palette.FractalColor;
        BackgroundColorSelector.SelectedColor = palette.BackgroundColor;
        UpdateEditState();
    }

    private void New_OnClick(object sender, RoutedEventArgs e)
    {
        string name = UniqueName("Новая палитра");
        var palette = new SerpinskyPalette { Name = name };
        _manager.Palettes.Add(palette);
        RefreshList(palette);
    }

    private void Copy_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
        {
            return;
        }

        SerpinskyPalette palette = _selected.Clone(UniqueName($"{_selected.Name} копия"));
        _manager.Palettes.Add(palette);
        RefreshList(palette);
    }

    private void Delete_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _selected.IsBuiltIn)
        {
            return;
        }

        if (MessageBox.Show(this, $"Удалить палитру «{_selected.Name}»?", "Палитры",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        bool wasActive = ReferenceEquals(_manager.ActivePalette, _selected);
        _manager.Palettes.Remove(_selected);
        if (wasActive)
        {
            _manager.ActivePalette = _manager.Palettes[0];
        }
        _manager.SaveCustomPalettes();
        RefreshList(_manager.ActivePalette);
    }

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryCommitEditor())
        {
            return;
        }

        _manager.SaveCustomPalettes();
        RefreshList(_selected);
    }

    private void Apply_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryCommitEditor() || _selected is null)
        {
            return;
        }

        _manager.ActivePalette = _selected;
        _manager.SaveCustomPalettes();
        DialogResult = true;
    }

    private bool TryCommitEditor()
    {
        if (_selected is null)
        {
            return false;
        }
        if (_selected.IsBuiltIn)
        {
            return true;
        }
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            MessageBox.Show(this, "Введите название палитры.",
                "Палитра", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        _selected.Name = NameBox.Text.Trim();
        _selected.FractalColor = FractalColorSelector.SelectedColor;
        _selected.BackgroundColor = BackgroundColorSelector.SelectedColor;
        return true;
    }

    private void UpdateEditState()
    {
        bool editable = _selected is { IsBuiltIn: false };
        NameBox.IsEnabled = editable;
        FractalColorSelector.IsEnabled = editable;
        BackgroundColorSelector.IsEnabled = editable;
        EditHint.Text = editable
            ? "Пользовательскую палитру можно редактировать."
            : "Встроенную палитру можно применить или скопировать.";
    }

    private string UniqueName(string basis)
    {
        string candidate = basis;
        int suffix = 1;
        while (_manager.Palettes.Any(p => p.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{basis} {suffix++}";
        }
        return candidate;
    }

}
