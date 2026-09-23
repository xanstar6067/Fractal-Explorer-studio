using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        // Image со .ico по умолчанию берёт наименьший кадр и растягивает его —
        // получается размытая иконка. Явно выбираем самый крупный кадр.
        LogoImage.Source = IconResourceLoader.LoadLargestFrame("Assets/Icons/FractalExplorer.ico");

        IReadOnlyList<FractalCatalogItem> catalog = FractalCatalog.Create();
        int threeDimensional = catalog.Count(item => item.IsThreeDimensional);
        CatalogSummaryText.Text = $"{CatalogSearch.CountModes(catalog.Count)}, из них трёхмерных — {threeDimensional}";
    }

    private void RepositoryLink_OnRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri)
            {
                UseShellExecute = true
            });
        }
        catch
        {
            MessageBox.Show(
                this,
                "Не удалось открыть ссылку в браузере.",
                "О программе",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        e.Handled = true;
    }
}
