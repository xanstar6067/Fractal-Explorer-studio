using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Resources;

namespace FractalExplorerWPF.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        // Image со .ico по умолчанию берёт наименьший кадр и растягивает его —
        // получается размытая иконка. Явно выбираем самый крупный кадр.
        LogoImage.Source = LoadLargestIconFrame("Assets/Icons/FractalExplorer.ico");
    }

    private static BitmapSource? LoadLargestIconFrame(string relativePath)
    {
        try
        {
            StreamResourceInfo? info = Application.GetResourceStream(new Uri(relativePath, UriKind.Relative));
            if (info is null) return null;
            using Stream stream = info.Stream;
            var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            return decoder.Frames.OrderByDescending(frame => frame.PixelWidth).FirstOrDefault();
        }
        catch
        {
            return null;
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
