using System.Windows;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class CloudConflictWindow : Window
{
    public CloudConflictChoice Choice { get; private set; } = CloudConflictChoice.Cancel;
    public CloudConflictWindow(LocalCloudSave local, CloudSave remote)
    {
        InitializeComponent();
        LocalTitle.Text = $"На этом ПК: {local.Name}\nРежим: {local.Category}";
        RemoteTitle.Text = $"В облаке: {remote.Name}\nВерсия {remote.Revision} · {remote.UpdatedAt.LocalDateTime:g}";
        LocalData.Text = local.JsonData;
        RemoteData.Text = remote.JsonData;
    }
    private void Both_OnClick(object sender, RoutedEventArgs e) => Choose(CloudConflictChoice.KeepBoth);
    private void Cloud_OnClick(object sender, RoutedEventArgs e) => Choose(CloudConflictChoice.UseCloud);
    private void Local_OnClick(object sender, RoutedEventArgs e) => Choose(CloudConflictChoice.UseLocal);
    private void Choose(CloudConflictChoice choice) { Choice = choice; DialogResult = true; }
}
