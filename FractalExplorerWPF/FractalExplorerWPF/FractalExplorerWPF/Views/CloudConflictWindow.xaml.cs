using System.IO;
using System.Windows;
using System.Windows.Controls;
using FractalExplorerWPF.Infrastructure.Cloud;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

/// <summary>Asks what to do when a transfer would overwrite, duplicate or resurrect a save.</summary>
public partial class CloudConflictWindow : Window
{
    private readonly CloudCollision _collision;

    /// <summary>Closing the dialog without a choice stops the whole operation.</summary>
    public CloudCollisionDecision Decision { get; private set; } = new(CloudCollisionAction.CancelAll);

    public CloudConflictWindow(CloudCollision collision)
    {
        InitializeComponent();
        _collision = collision;
        LocalCloudSave? local = collision.Local;
        CloudSave? remote = collision.Remote;
        string name = local?.Name ?? remote?.Name ?? "";

        (TitleText.Text, SubtitleText.Text) = collision.Kind switch
        {
            CloudCollisionKind.SameName => ($"«{name}» уже есть в облаке",
                $"В облаке есть сохранение режима {local?.Category} с тем же именем, но с другими параметрами."),
            CloudCollisionKind.BothChanged => ($"«{name}» изменено и на этом ПК, и в облаке",
                "После последней синхронизации обе версии редактировались независимо."),
            CloudCollisionKind.CloudNewer => ($"В облаке более новая версия «{name}»",
                "Версия на этом ПК не менялась, а облачная обновлена на другом компьютере."),
            CloudCollisionKind.LocalNewer => ($"На этом ПК более новая версия «{name}»",
                "Локальная версия изменена после синхронизации, облачная — нет."),
            CloudCollisionKind.CloudDeleted => ($"«{name}» удалено из облака",
                "Связанная облачная запись была удалена. Копия на этом ПК на месте."),
            _ => ($"Копия «{name}» на этом ПК удалена", "Файл на этом ПК удалён, облачная запись на месте.")
        };
        bool deletion = collision.Kind is CloudCollisionKind.CloudDeleted or CloudCollisionKind.LocalDeleted;
        if (deletion)
        {
            KindIcon.Text = "";
            KindBadge.SetResourceReference(Border.BackgroundProperty, "Theme.DangerSoftBrush");
            KindIcon.SetResourceReference(TextBlock.ForegroundProperty, "Theme.DangerBrush");
        }

        LocalNameText.Text = local?.Name ?? "—";
        LocalMetaText.Text = local is null
            ? "Файл удалён"
            : File.Exists(local.FilePath) ? $"{local.Category} · изменено {File.GetLastWriteTime(local.FilePath):dd.MM.yyyy HH:mm}" : local.Category;
        RemoteNameText.Text = remote?.Name ?? "—";
        RemoteMetaText.Text = remote is null
            ? "Запись удалена"
            : $"Версия {remote.Revision} · изменено {remote.UpdatedAt.LocalDateTime:dd.MM.yyyy HH:mm}";

        IReadOnlyList<CloudParameterDifference> differences = CloudSaveRepository.CompareParameters(local?.JsonData, remote?.JsonData);
        DifferencesPanel.Visibility = differences.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        DifferencesTitle.Text = differences.Count == 1 ? "Отличается 1 параметр" : $"Отличающиеся параметры: {differences.Count}";
        DifferencesList.ItemsSource = differences;

        KeepBothOption.Visibility = UseLocalOption.Visibility = UseCloudOption.Visibility =
            deletion ? Visibility.Collapsed : Visibility.Visible;
        RestoreOption.Visibility = deletion ? Visibility.Visible : Visibility.Collapsed;
        if (collision.Kind == CloudCollisionKind.CloudDeleted)
        {
            RestoreTitle.Text = "Отправить в облако снова";
            RestoreText.Text = "Из копии на этом ПК будет создана новая облачная запись.";
        }
        else
        {
            RestoreTitle.Text = "Скачать на этот ПК снова";
            RestoreText.Text = "Облачная запись будет сохранена как новый локальный файл.";
        }
        SkipText.Text = collision.Remaining > 0 ? "Ничего не менять и перейти к следующей записи." : "Ничего не менять.";
        (deletion ? RestoreOption : KeepBothOption).IsChecked = true;

        PrefixBox.Text = CloudSyncService.DefaultPrefix;
        UpdateKeepBothText();

        ApplyToAllBox.Visibility = collision.Remaining > 0 ? Visibility.Visible : Visibility.Collapsed;
        ApplyToAllText.Text = $"Применить к остальным таким же случаям в этой операции (ещё {collision.Remaining})";
    }

    private void UpdateKeepBothText()
    {
        string original = _collision.Local?.Name ?? _collision.Remote?.Name ?? "";
        string renamed = PrefixBox.Text + original;
        KeepBothText.Text = _collision.Mode switch
        {
            CloudTransferMode.Upload =>
                $"Версия с ПК получит имя «{renamed}» и уйдёт в облако новой записью. Облачная «{original}» не изменится.",
            CloudTransferMode.Download =>
                $"Версия с ПК получит имя «{renamed}», облачная будет скачана как «{original}». В облаке ничего не изменится.",
            _ => $"Версия с ПК получит имя «{renamed}». Обе версии окажутся и на этом ПК, и в облаке."
        };
    }

    private void PrefixBox_OnTextChanged(object sender, TextChangedEventArgs e) { if (IsInitialized) UpdateKeepBothText(); }
    private void PrefixBox_OnGotKeyboardFocus(object sender, RoutedEventArgs e) => KeepBothOption.IsChecked = true;

    private void Continue_OnClick(object sender, RoutedEventArgs e)
    {
        RadioButton? chosen = new[] { KeepBothOption, UseLocalOption, UseCloudOption, RestoreOption, SkipOption }
            .FirstOrDefault(option => option.IsChecked == true && option.Visibility == Visibility.Visible);
        if (chosen is null) return;
        var action = Enum.Parse<CloudCollisionAction>((string)chosen.Tag);
        Decision = new(action, PrefixBox.Text, ApplyToAllBox.Visibility == Visibility.Visible && ApplyToAllBox.IsChecked == true);
        DialogResult = true;
    }
}
