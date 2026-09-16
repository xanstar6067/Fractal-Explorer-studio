using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using FractalExplorerWPF.Infrastructure.Cloud;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class CloudSaveManagerWindow : Window
{
    private readonly FractalCloudClient _client;
    private readonly string? _category;
    private CloudSyncService? _sync;
    private CancellationTokenSource? _operation;
    private bool _closed;

    public CloudSaveManagerWindow(string? category = null, bool connectOnLoad = true, FractalCloudClient? client = null)
    {
        InitializeComponent();
        _client = client ?? FractalCloudClient.Instance;
        _category = category;
        if (category is not null)
        {
            CategoryFilter.Content = $"Синхронизация режима: {category}";
            CategoryFilter.Visibility = Visibility.Visible;
            CategoryFilter.IsChecked = true;
        }
        AccountText.Text = _client.Server;
        RefreshLocal();
        if (connectOnLoad) Loaded += async (_, _) => await RunAsync(RefreshAsync);
        Closed += (_, _) => { _closed = true; _operation?.Cancel(); };
    }

    public static void Open(Window owner, string? category = null)
    {
        try
        {
            var existing = Application.Current.Windows.OfType<CloudSaveManagerWindow>().FirstOrDefault();
            if (existing is not null) { existing.Activate(); return; }
            new CloudSaveManagerWindow(category) { Owner = owner }.ShowDialog();
        }
        catch (Exception ex) { MessageBox.Show(owner, DescribeError(ex), "FractalCloud", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    internal static string DescribeError(Exception ex) => ex switch
    {
        CloudApiException or CloudLoginRequiredException => ex.Message,
        HttpRequestException => "Нет защищённого соединения с FractalCloud. Проверьте сеть, адрес сервера, время Windows и сертификат. Проверка TLS не отключается.",
        OperationCanceledException => "Операция отменена или время ожидания истекло. Обновите списки перед повтором.",
        JsonException => "Получены некорректные данные JSON. Локальные сохранения сохранены.",
        IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException =>
            "Не удалось прочитать или записать локальные данные либо защищённый сеанс Windows.",
        InvalidOperationException => ex.Message,
        _ => "Не удалось выполнить операцию FractalCloud. Обновите списки и попробуйте снова."
    };

    private string? SelectedCategory => CategoryFilter.IsChecked == true ? _category : null;
    private void RefreshLocal()
    {
        IReadOnlyList<LocalCloudSave> local = CloudSaveRepository.ListLocal(out int unreadable);
        LocalList.ItemsSource = local.Where(l => SelectedCategory is null || l.Category == SelectedCategory).ToList();
        if (unreadable > 0) StatusText.Text = $"Пропущено локальных файлов: {unreadable}.";
    }

    private async Task RefreshAsync(CancellationToken token)
    {
        IReadOnlyList<CloudSave> saves = await _client.ListAsync(token);
        if (_closed) return;
        CloudList.ItemsSource = saves.OrderByDescending(s => s.UpdatedAt).ToList();
        AccountText.Text = $"{_client.Email} · {_client.Server}";
        _sync = new CloudSyncService(_client, new CloudSyncIndex(_client.Server, _client.Email!), ResolveConflict);
        RefreshLocal();
        StatusText.Text = $"Облако: {saves.Count} сохранений. Выберите запись или синхронизируйте списки.";
    }

    private CloudConflictChoice ResolveConflict(LocalCloudSave local, CloudSave remote)
    {
        if (_closed) return CloudConflictChoice.Cancel;
        var dialog = new CloudConflictWindow(local, remote) { Owner = this };
        dialog.ShowDialog();
        return dialog.Choice;
    }

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (_operation is not null || _closed) return;
        using var cts = new CancellationTokenSource();
        _operation = cts;
        Toolbar.IsEnabled = ListsPanel.IsEnabled = EditPanel.IsEnabled = LogoutButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        StatusText.Text = "Связь с FractalCloud…";
        try { await action(cts.Token); }
        catch (CloudLoginRequiredException)
        {
            _sync = null;
            CloudList.ItemsSource = null;
            AccountText.Text = _client.Server;
            if (!_closed && !cts.IsCancellationRequested)
            {
                var login = new CloudLoginWindow(_client) { Owner = this };
                if (login.ShowDialog() == true)
                {
                    try
                    {
                        await RefreshAsync(cts.Token);
                        StatusText.Text = "Вход выполнен. Выберите действие для синхронизации.";
                    }
                    catch (Exception ex) { StatusText.Text = DescribeError(ex); }
                }
                else StatusText.Text = "Вход отменён. Локальные сохранения доступны без облака.";
            }
        }
        catch (Exception ex) { if (!_closed) StatusText.Text = DescribeError(ex); }
        finally
        {
            _operation = null;
            if (!_closed)
            {
                Toolbar.IsEnabled = ListsPanel.IsEnabled = EditPanel.IsEnabled = LogoutButton.IsEnabled = true;
                CancelButton.IsEnabled = false;
                // Partial progress is committed per save; always show the local result after cancellation/failure.
                try { RefreshLocal(); } catch { }
            }
        }
    }

    private async Task<CloudSyncService> GetSyncAsync(CancellationToken token)
    {
        if (_sync is null) await RefreshAsync(token);
        return _sync ?? throw new CloudLoginRequiredException();
    }

    private async void Refresh_OnClick(object sender, RoutedEventArgs e) => await RunAsync(RefreshAsync);
    private async void Sync_OnClick(object sender, RoutedEventArgs e) => await RunAsync(async token =>
    {
        CloudSyncService sync = await GetSyncAsync(token);
        var progress = new Progress<string>(s => { if (!_closed) StatusText.Text = s; });
        string result = await sync.SynchronizeAllAsync(SelectedCategory, progress, token);
        await RefreshAsync(token);
        StatusText.Text = result;
    });

    private async void Upload_OnClick(object sender, RoutedEventArgs e)
    {
        if (LocalList.SelectedItem is not LocalCloudSave local) { StatusText.Text = "Выберите локальное сохранение."; return; }
        await RunAsync(async token =>
        {
            CloudSyncService sync = await GetSyncAsync(token);
            try { await sync.UploadAsync(local, token); }
            catch (CloudApiException ex) when (ex.Status == HttpStatusCode.NotFound)
            {
                if (MessageBox.Show(this, "Связанная запись удалена из облака. Загрузить локальное сохранение как новую облачную запись?",
                    "Сохранение удалено", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                LocalCloudSave current = CloudSaveRepository.ReadLocal(local.FilePath);
                CloudSave created = await _client.CreateAsync(current.Name, current.JsonData, token);
                sync.Index.Set(created, current);
            }
            await RefreshAsync(token);
            StatusText.Text = "Выбранное сохранение синхронизировано. Превью осталось на ПК.";
        });
    }

    private async void Download_OnClick(object sender, RoutedEventArgs e)
    {
        if (CloudList.SelectedItem is not CloudSave remote) { StatusText.Text = "Выберите облачное сохранение."; return; }
        await RunAsync(async token =>
        {
            await (await GetSyncAsync(token)).DownloadAsync(remote, token);
            await RefreshAsync(token);
            StatusText.Text = "Сохранение доступно в менеджере своего фрактала. Превью можно отрисовать вручную.";
        });
    }

    private async void Rename_OnClick(object sender, RoutedEventArgs e)
    {
        if (CloudList.SelectedItem is not CloudSave selected) return;
        string name = NameBox.Text.Trim();
        await RunAsync(async token =>
        {
            CloudSave current = await _client.GetAsync(selected.Id, token);
            string json = current.JsonData!;
            // Keep the embedded desktop name consistent; opaque data of other clients is left intact.
            if (JsonNode.Parse(json) is JsonObject envelope && envelope["format"]?.GetValue<string>() == "FractalExplorerWPF" &&
                envelope["state"] is JsonObject state)
            { state["SaveName"] = name; json = envelope.ToJsonString(); }
            try { await _client.UpdateAsync(selected.Id, name, json, selected.Revision, token); }
            catch (CloudApiException ex) when (ex.Status == HttpStatusCode.Conflict)
            {
                await ShowChangedAsync(selected.Id, token);
                return;
            }
            await RefreshAsync(token);
            StatusText.Text = "Название обновлено. Синхронизация перенесёт его на этот ПК.";
        });
    }

    private async void Delete_OnClick(object sender, RoutedEventArgs e)
    {
        if (CloudList.SelectedItem is not CloudSave selected) return;
        if (MessageBox.Show(this, $"Удалить «{selected.Name}» из облака? Локальные копии останутся на компьютерах.",
            "Удаление из облака", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await RunAsync(async token =>
        {
            try { await _client.DeleteAsync(selected.Id, selected.Revision, token); }
            catch (CloudApiException ex) when (ex.Status == HttpStatusCode.Conflict)
            { await ShowChangedAsync(selected.Id, token); return; }
            // Retain the link as a tombstone so Sync All does not silently upload the deleted record again.
            await RefreshAsync(token);
            StatusText.Text = "Запись удалена из облака. Для повторной загрузки выберите локальную копию и «В облако».";
        });
    }

    private async Task ShowChangedAsync(Guid id, CancellationToken token)
    {
        CloudSave current = await _client.GetAsync(id, token);
        await RefreshAsync(token);
        MessageBox.Show(this, $"Сохранение изменилось: «{current.Name}», версия {current.Revision}, {current.UpdatedAt.LocalDateTime:g}.\n\nДействие не выполнено. Проверьте актуальную запись и повторите действие, если оно всё ещё нужно.",
            "Конфликт версии", MessageBoxButton.OK, MessageBoxImage.Information);
        StatusText.Text = "Список обновлён. Конфликтующее изменение не перезаписано.";
    }

    private async void Logout_OnClick(object sender, RoutedEventArgs e) => await RunAsync(async token =>
    {
        await _client.LogoutAsync(token);
        _sync = null;
        CloudList.ItemsSource = null;
        AccountText.Text = _client.Server;
        StatusText.Text = "Вы вышли из облака. Защищённый refresh token удалён. Для входа нажмите «Обновить списки».";
    });
    private void Cancel_OnClick(object sender, RoutedEventArgs e) { _operation?.Cancel(); CancelButton.IsEnabled = false; }
    private void Filter_OnChanged(object sender, RoutedEventArgs e) { if (LocalList is not null) RefreshLocal(); }
    private void CloudList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    { if (CloudList.SelectedItem is CloudSave save) NameBox.Text = save.Name; }
}
