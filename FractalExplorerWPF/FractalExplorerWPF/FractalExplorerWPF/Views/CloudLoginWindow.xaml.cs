using System.Windows;
using FractalExplorerWPF.Infrastructure.Cloud;

namespace FractalExplorerWPF.Views;

public partial class CloudLoginWindow : Window
{
    private readonly FractalCloudClient _client;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _closed;
    public CloudLoginWindow(FractalCloudClient client)
    {
        InitializeComponent();
        _client = client;
        ServerText.Text = client.Server;
        EmailBox.Text = client.Email ?? "";
        Closed += (_, _) => { _closed = true; _lifetime.Cancel(); PasswordInput.Clear(); };
        Loaded += (_, _) => { if (EmailBox.Text.Length == 0) EmailBox.Focus(); else PasswordInput.Focus(); };
    }

    private async void Login_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(EmailBox.Text) || PasswordInput.Password.Length == 0)
        { StatusText.Text = "Введите email и пароль."; return; }
        LoginButton.IsEnabled = EmailBox.IsEnabled = PasswordInput.IsEnabled = false;
        StatusText.Text = "Вход…";
        try
        {
            await _client.LoginAsync(EmailBox.Text.Trim(), PasswordInput.Password, _lifetime.Token);
            if (!_closed) DialogResult = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_closed) StatusText.Text = CloudSaveManagerWindow.DescribeError(ex); }
        finally
        {
            PasswordInput.Clear();
            LoginButton.IsEnabled = EmailBox.IsEnabled = PasswordInput.IsEnabled = true;
        }
    }
}
