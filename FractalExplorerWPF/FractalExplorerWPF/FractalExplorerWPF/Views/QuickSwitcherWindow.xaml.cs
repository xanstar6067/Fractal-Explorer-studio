using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class QuickSwitcherWindow : Window
{
    private readonly List<FractalCatalogItem> _all;

    public FractalCatalogItem? SelectedItem { get; private set; }

    public QuickSwitcherWindow(IEnumerable<FractalCatalogItem> catalog)
    {
        InitializeComponent();
        _all = catalog.Where(item => item.LaunchKey is not null).ToList();
        ResultList.ItemsSource = _all;
        if (_all.Count > 0) ResultList.SelectedIndex = 0;
        Loaded += (_, _) => SearchBox.Focus();
    }

    private void SearchBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        // Тот же поиск, что в каталоге главного окна: название, описание и разделы.
        string query = SearchBox.Text.Trim();
        List<FractalCatalogItem> filtered = string.IsNullOrEmpty(query)
            ? _all
            : _all.Where(item => CatalogSearch.Matches(item, query)).ToList();

        ResultList.ItemsSource = filtered;
        if (filtered.Count > 0) ResultList.SelectedIndex = 0;
        EmptyResultText.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SearchBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                if (ResultList.Items.Count > 0)
                    ResultList.SelectedIndex = Math.Min(ResultList.SelectedIndex + 1, ResultList.Items.Count - 1);
                ResultList.ScrollIntoView(ResultList.SelectedItem);
                e.Handled = true;
                break;
            case Key.Up:
                if (ResultList.Items.Count > 0)
                    ResultList.SelectedIndex = Math.Max(ResultList.SelectedIndex - 1, 0);
                ResultList.ScrollIntoView(ResultList.SelectedItem);
                e.Handled = true;
                break;
            case Key.Enter:
                Confirm();
                e.Handled = true;
                break;
            case Key.Escape:
                DialogResult = false;
                e.Handled = true;
                break;
        }
    }

    private void ResultList_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => Confirm();

    private void Confirm()
    {
        if (ResultList.SelectedItem is FractalCatalogItem item)
        {
            SelectedItem = item;
            DialogResult = true;
        }
    }
}
