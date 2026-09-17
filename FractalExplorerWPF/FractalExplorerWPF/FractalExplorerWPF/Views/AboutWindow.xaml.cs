using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using FractalExplorerWPF.Infrastructure;

namespace FractalExplorerWPF.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        // Image со .ico по умолчанию берёт наименьший кадр и растягивает его —
        // получается размытая иконка. Явно выбираем самый крупный кадр.
        LogoImage.Source = IconResourceLoader.LoadLargestFrame("Assets/Icons/FractalExplorer.ico");
        DataFolderText.Text = AppPaths.DataRoot;
    }

    private void OpenDataFolder_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            string folder = AppPaths.EnsureDataRoot();
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"Не удалось открыть папку с данными:\n{AppPaths.DataRoot}\n\n{exception.Message}",
                "О программе",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
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
