using System.Collections;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using FractalExplorerWPF.Infrastructure.Cloud;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

/// <summary>
/// One table of every save on this PC and in FractalCloud with its sync state. Whole-list actions sit on top,
/// actions for ticked rows below it; sign-in is part of the window instead of a separate dialog.
/// </summary>
public partial class CloudSaveManagerWindow : Window
{
    private readonly FractalCloudClient _client;
    private readonly string? _initialCategory;
    private readonly List<FilterOption> _stateOptions;
    private CloudSyncService? _sync;
    private CancellationTokenSource? _operation;
    private List<EntryRow> _entries = [];
    private readonly HashSet<string> _checked = [];
    private ICollectionView? _view;
    private CloudSyncEntry? _renaming;
    private List<CloudSyncEntry> _deleting = [];
    private bool _deletingLocal;
    private bool _closed, _updatingFilters, _loadedOnce;
    private string? _sortKey;
    private ListSortDirection _sortDirection = ListSortDirection.Ascending;

    public CloudSaveManagerWindow(string? category = null, bool connectOnLoad = true, FractalCloudClient? client = null)
    {
        InitializeComponent();
        _client = client ?? FractalCloudClient.Instance;
        _initialCategory = category;
        _stateOptions =
        [
            new("all", "Все", _ => true),
            new("attention", "Требуют действия", e => e.State is not (CloudEntryState.Synced or CloudEntryState.Unsupported)),
            new("synced", "Синхронизировано", e => e.State == CloudEntryState.Synced),
            new("local", "Только на ПК", e => e.State is CloudEntryState.LocalOnly or CloudEntryState.CloudDeleted),
            new("cloud", "Только в облаке", e => e.State is CloudEntryState.CloudOnly or CloudEntryState.LocalDeleted),
            new("changed", "Изменены", e => e.State is CloudEntryState.LocalChanged or CloudEntryState.CloudChanged),
            new("conflicts", "Совпадения имён и конфликты", e => e.State is CloudEntryState.SameName or CloudEntryState.BothChanged),
            new("unsupported", "Другой формат", e => e.State == CloudEntryState.Unsupported)
        ];
        _updatingFilters = true;
        StateFilter.ItemsSource = _stateOptions;
        StateFilter.SelectedIndex = 0;
        _updatingFilters = false;

        ServerText.Text = LoginServerText.Text = ServerLabel;
        EmailBox.Text = _client.Email ?? "";
        ApplyEntries([], keepSelection: false);
        if (_client.HasSession) ShowMain(); else ShowLogin(null);
        SetStatus(connectOnLoad ? "Подключение к FractalCloud…" : "");

        if (connectOnLoad) ContentRendered += OnFirstContentRendered;
        PreviewKeyDown += OnPreviewKeyDown;
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
        OperationCanceledException => "Операция отменена или время ожидания истекло. Обновите список перед повтором.",
        JsonException => "Получены некорректные данные JSON. Локальные сохранения не изменены.",
        IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException =>
            "Не удалось прочитать или записать локальные данные либо защищённый сеанс Windows.",
        InvalidOperationException => ex.Message,
        _ => "Не удалось выполнить операцию FractalCloud. Обновите список и попробуйте снова."
    };

    /// <summary>Fills the table without a server; for documentation screenshots and verification.</summary>
    internal void ShowEntriesForPreview(string email, IReadOnlyList<CloudSyncEntry> entries, string status)
    {
        AccountText.Text = email;
        ShowMain();
        ApplyEntries(entries, keepSelection: false);
        SetStatus(status, StatusTone.Success);
    }

    internal void ShowLoginForPreview(string? reason) => ShowLogin(reason);

    private string ServerLabel
    {
        get
        {
            try { return new Uri(_client.Server).Authority; }
            catch (UriFormatException) { return _client.Server; }
        }
    }

    private async void OnFirstContentRendered(object? sender, EventArgs e)
    {
        ContentRendered -= OnFirstContentRendered;
        // Network and sign-in start only after the first themed frame is on screen.
        if (_client.HasSession) await RefreshAsync();
        else
        {
            SetStatus("");
            FocusLogin();
        }
    }

    // ───────────────────────────── Sign in ─────────────────────────────

    private void ShowLogin(string? reason)
    {
        _sync = null;
        MainPanel.Visibility = Visibility.Collapsed;
        AccountPanel.Visibility = Visibility.Collapsed;
        LoginPanel.Visibility = Visibility.Visible;
        LoginReasonText.Text = reason ?? "";
        LoginReasonPanel.Visibility = string.IsNullOrEmpty(reason) ? Visibility.Collapsed : Visibility.Visible;
        LoginStatusText.Visibility = Visibility.Collapsed;
        if (_client.Email is { } email) EmailBox.Text = email;
        SubtitleText.Text = "Войдите, чтобы сравнить сохранения на ПК и в облаке";
        if (IsLoaded) FocusLogin();
    }

    private void ShowMain()
    {
        LoginPanel.Visibility = Visibility.Collapsed;
        MainPanel.Visibility = Visibility.Visible;
        AccountPanel.Visibility = Visibility.Visible;
        if (_client.Email is { } email) AccountText.Text = email;
        SubtitleText.Text = "Сохранения всех фракталов на этом ПК и в FractalCloud";
    }

    private void FocusLogin() => Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
    {
        if (LoginPanel.Visibility != Visibility.Visible) return;
        if (EmailBox.Text.Length == 0) EmailBox.Focus(); else PasswordInput.Focus();
    });

    private void EmailBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        PasswordInput.Focus();
    }

    private void PasswordInput_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        Login_OnClick(sender, e);
    }

    private async void Login_OnClick(object sender, RoutedEventArgs e)
    {
        if (_operation is not null) return;
        string email = EmailBox.Text.Trim();
        if (email.Length == 0 || PasswordInput.Password.Length == 0)
        {
            ShowLoginError("Введите email и пароль.");
            return;
        }
        using var cts = new CancellationTokenSource();
        _operation = cts;
        LoginButton.IsEnabled = EmailBox.IsEnabled = PasswordInput.IsEnabled = false;
        LoginButton.Content = "Вход…";
        LoginStatusText.Visibility = Visibility.Collapsed;
        bool success = false;
        try
        {
            await _client.LoginAsync(email, PasswordInput.Password, cts.Token);
            success = true;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception ex) { if (!_closed) ShowLoginError(DescribeError(ex)); }
        finally
        {
            PasswordInput.Clear();
            _operation = null;
            if (!_closed)
            {
                LoginButton.IsEnabled = EmailBox.IsEnabled = PasswordInput.IsEnabled = true;
                LoginButton.Content = "Войти";
            }
        }
        if (!success || _closed) { if (!_closed) FocusLogin(); return; }
        _sync = null;
        ShowMain();
        await RefreshAsync();
    }

    private void ShowLoginError(string message)
    {
        LoginStatusText.Text = message;
        LoginStatusText.Visibility = Visibility.Visible;
    }

    private async void Logout_OnClick(object sender, RoutedEventArgs e) => await RunAsync(async token =>
    {
        await _client.LogoutAsync(token);
        ApplyEntries([], keepSelection: false);
        ShowLogin("Вы вышли. Защищённый сеанс на этом ПК удалён; сохранения на диске не тронуты.");
        SetStatus("");
    });

    // ───────────────────────────── Operations ─────────────────────────────

    private CloudSyncService EnsureSync()
    {
        if (_sync is not null) return _sync;
        string email = _client.Email ?? throw new CloudLoginRequiredException();
        return _sync = new CloudSyncService(_client, new CloudSyncIndex(_client.Server, email), new CloudRemoteCache(_client.Server, email));
    }

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (_operation is not null || _closed) return;
        using var cts = new CancellationTokenSource();
        _operation = cts;
        CloseBars();
        SetBusy(true);
        try { await action(cts.Token); }
        catch (CloudLoginRequiredException)
        {
            if (!_closed) ShowLogin("Сеанс не найден или завершён. Войдите снова — сохранения на диске не тронуты.");
            SetStatus("");
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            if (!_closed) SetStatus("Операция остановлена. Уже выполненные шаги сохранены; обновите список.", StatusTone.Warning);
        }
        catch (Exception ex)
        {
            if (!_closed) SetStatus(DescribeError(ex), StatusTone.Danger);
        }
        finally
        {
            _operation = null;
            if (!_closed) SetBusy(false);
        }
    }

    private Progress<CloudProgress> CreateProgress() => new(p =>
    {
        if (_closed || _operation is null) return;
        StatusIcon.Visibility = Visibility.Collapsed;
        StatusText.Text = p.Message;
        Progress.Visibility = p.Total > 0 ? Visibility.Visible : Visibility.Collapsed;
        Progress.Maximum = Math.Max(1, p.Total);
        Progress.Value = p.Done;
    });

    private Task RefreshAsync() => RunAsync(async token =>
    {
        CloudSnapshot snapshot = await EnsureSync().LoadAsync(CreateProgress(), token);
        if (_closed) return;
        ShowMain();
        ApplyEntries(snapshot.Entries, keepSelection: _loadedOnce);
        _loadedOnce = true;
        SetStatus(DescribeSnapshot(snapshot), snapshot.UnreadableLocal > 0 ? StatusTone.Warning : StatusTone.Success);
    });

    private static string DescribeSnapshot(CloudSnapshot snapshot)
    {
        int local = snapshot.Entries.Count(e => e.HasLocal);
        int remote = snapshot.Entries.Count(e => e.HasRemote);
        string text = $"Обновлено в {snapshot.LoadedAt.LocalDateTime:HH:mm}: на ПК {local}, в облаке {remote}.";
        if (snapshot.UnreadableLocal > 0) text += $" Не удалось прочитать локальных файлов: {snapshot.UnreadableLocal}.";
        return text;
    }

    private Task TransferAsync(IReadOnlyList<CloudSyncEntry> entries, CloudTransferMode mode, bool explicitSelection) => RunAsync(async token =>
    {
        CloudSyncService sync = EnsureSync();
        CloudTransferReport report = await sync.TransferAsync(entries, mode, explicitSelection, ResolveCollision, CreateProgress(), token);
        if (_closed) return;
        string message = report.Describe();
        if (report.Downloaded > 0) message += " Превью не передаются: отрисуйте их в менеджере сохранений фрактала.";
        StatusTone tone = report.Failures.Count > 0 || report.Cancelled ? StatusTone.Warning : StatusTone.Success;
        SetStatus(message, tone);
        CloudSnapshot snapshot = await sync.LoadAsync(CreateProgress(), CancellationToken.None);
        if (_closed) return;
        ApplyEntries(snapshot.Entries, keepSelection: false);
        SetStatus(message, tone);
    });

    private CloudCollisionDecision ResolveCollision(CloudCollision collision)
    {
        if (_closed) return new(CloudCollisionAction.CancelAll);
        var dialog = new CloudConflictWindow(collision) { Owner = this };
        dialog.ShowDialog();
        return dialog.Decision;
    }

    private async void Refresh_OnClick(object sender, RoutedEventArgs e) => await RefreshAsync();
    private async void SyncAll_OnClick(object sender, RoutedEventArgs e) => await TransferAsync(VisibleEntries(), CloudTransferMode.Sync, false);
    private async void UploadAll_OnClick(object sender, RoutedEventArgs e) => await TransferAsync(VisibleEntries(), CloudTransferMode.Upload, false);
    private async void DownloadAll_OnClick(object sender, RoutedEventArgs e) => await TransferAsync(VisibleEntries(), CloudTransferMode.Download, false);
    private async void UploadSelected_OnClick(object sender, RoutedEventArgs e) => await TransferAsync(SelectedEntries(), CloudTransferMode.Upload, true);
    private async void DownloadSelected_OnClick(object sender, RoutedEventArgs e) => await TransferAsync(SelectedEntries(), CloudTransferMode.Download, true);
    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        _operation?.Cancel();
        CancelButton.IsEnabled = false;
        StatusText.Text = "Остановка после текущей записи…";
    }

    private void Rename_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedEntries() is not [var entry] || !CanRename(entry)) return;
        _renaming = entry;
        RenameCaption.Text = entry.State switch
        {
            CloudEntryState.LocalOnly => "Новое имя на ПК:",
            CloudEntryState.Synced => "Новое имя на ПК и в облаке:",
            _ => "Новое имя в облаке:"
        };
        RenameBox.Text = entry.Name;
        SelectionBar.Visibility = DeleteBar.Visibility = Visibility.Collapsed;
        RenameBar.Visibility = Visibility.Visible;
        RenameBox.Focus();
        RenameBox.SelectAll();
    }

    private void RenameBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; RenameConfirm_OnClick(sender, e); }
        else if (e.Key == Key.Escape) { e.Handled = true; CloseBars(); }
    }

    private async void RenameConfirm_OnClick(object sender, RoutedEventArgs e)
    {
        if (_renaming is not { } entry) return;
        string name = RenameBox.Text.Trim();
        if (name.Length == 0) { SetStatus("Введите новое имя.", StatusTone.Warning); return; }
        if (name == entry.Name) { CloseBars(); return; }
        await RunAsync(async token =>
        {
            CloudSyncService sync = EnsureSync();
            await sync.RenameAsync(entry, name, token);
            CloudSnapshot snapshot = await sync.LoadAsync(CreateProgress(), token);
            if (_closed) return;
            ApplyEntries(snapshot.Entries, keepSelection: false);
            SetStatus($"«{entry.Name}» переименовано в «{name}».", StatusTone.Success);
        });
    }

    private void DeleteCloud_OnClick(object sender, RoutedEventArgs e) => ShowDeleteBar(local: false);
    private void DeleteLocal_OnClick(object sender, RoutedEventArgs e) => ShowDeleteBar(local: true);

    private void ShowDeleteBar(bool local)
    {
        _deletingLocal = local;
        _deleting = SelectedEntries().Where(x => local ? x.HasLocal : x.HasRemote).ToList();
        if (_deleting.Count == 0) return;
        string subject = _deleting.Count == 1 ? $"«{_deleting[0].Name}»" : $"{_deleting.Count} {Plural(_deleting.Count, "запись", "записи", "записей")}";
        DeleteQuestion.Text = local
            ? $"Удалить {subject} с этого ПК? Файл уйдёт в Корзину Windows; облачная копия, если есть, останется."
            : $"Удалить из облака {subject}? Файлы на этом ПК останутся, но облачную версию восстановить будет нельзя.";
        DeleteConfirmButton.Content = "Удалить";
        SelectionBar.Visibility = RenameBar.Visibility = Visibility.Collapsed;
        DeleteBar.Visibility = Visibility.Visible;
        DeleteConfirmButton.Focus();
    }

    private async void DeleteConfirm_OnClick(object sender, RoutedEventArgs e)
    {
        List<CloudSyncEntry> targets = _deleting;
        bool local = _deletingLocal;
        if (targets.Count == 0) return;
        await RunAsync(async token =>
        {
            CloudSyncService sync = EnsureSync();
            CloudTransferReport report = local
                ? await sync.DeleteLocalAsync(targets, CreateProgress(), token)
                : await sync.DeleteRemoteAsync(targets, CreateProgress(), token);
            string message = report.Describe(local ? "удалено с ПК" : "удалено из облака") + (report.Deleted > 0
                ? local ? " Чтобы вернуть файл, восстановите его из Корзины Windows или получите снова из облака."
                        : " Чтобы вернуть запись, отметьте её и нажмите «Отправить»."
                : "");
            StatusTone tone = report.Failures.Count > 0 || report.Cancelled ? StatusTone.Warning : StatusTone.Success;
            CloudSnapshot snapshot = await sync.LoadAsync(CreateProgress(), CancellationToken.None);
            if (_closed) return;
            ApplyEntries(snapshot.Entries, keepSelection: false);
            SetStatus(message, tone);
        });
    }

    private void BarCancel_OnClick(object sender, RoutedEventArgs e) => CloseBars();

    private void CloseBars()
    {
        _renaming = null;
        _deleting = [];
        RenameBar.Visibility = DeleteBar.Visibility = Visibility.Collapsed;
        SelectionBar.Visibility = Visibility.Visible;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (MainPanel.Visibility != Visibility.Visible) return;
        if (e.Key == Key.F5 && _operation is null) { e.Handled = true; _ = RefreshAsync(); }
        else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; SearchBox.Focus(); SearchBox.SelectAll(); }
        else if (e.Key == Key.Escape && (RenameBar.Visibility == Visibility.Visible || DeleteBar.Visibility == Visibility.Visible))
        { e.Handled = true; CloseBars(); }
    }

    // ───────────────────────────── Table, filters, selection ─────────────────────────────

    private void ApplyEntries(IReadOnlyList<CloudSyncEntry> entries, bool keepSelection)
    {
        if (!keepSelection) _checked.Clear();
        _entries = entries.Select(e => new EntryRow(e) { IsChecked = _checked.Contains(e.Key) }).ToList();
        _checked.IntersectWith(_entries.Select(r => r.Key));
        RebuildModeFilter();
        _view = CollectionViewSource.GetDefaultView(_entries);
        _view.Filter = item => item is EntryRow row && Matches(row.Entry, includeState: true);
        ApplySort();
        EntriesList.ItemsSource = _view;
        UpdateSummary();
    }

    private void RebuildModeFilter()
    {
        string? current = (ModeFilter.SelectedItem as FilterOption)?.Key ?? (_loadedOnce ? null : _initialCategory);
        var options = new List<FilterOption> { new("", "Все режимы", _ => true) };
        foreach (string category in _entries.Select(e => e.Entry.Category).OfType<string>().Append(_initialCategory).OfType<string>()
                     .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
            options.Add(new(category, category, e => string.Equals(e.Category, category, StringComparison.OrdinalIgnoreCase)));
        _updatingFilters = true;
        ModeFilter.ItemsSource = options;
        ModeFilter.SelectedItem = options.FirstOrDefault(o => o.Key == (current ?? "")) ?? options[0];
        _updatingFilters = false;
    }

    private bool Matches(CloudSyncEntry entry, bool includeState)
    {
        if (ModeFilter.SelectedItem is FilterOption mode && !mode.Match(entry)) return false;
        if (includeState && StateFilter.SelectedItem is FilterOption state && !state.Match(entry)) return false;
        string query = SearchBox.Text.Trim();
        return query.Length == 0 ||
               entry.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
               entry.CategoryText.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    private void Filter_OnChanged(object sender, RoutedEventArgs e)
    {
        SearchPlaceholder.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_updatingFilters || _view is null) return;
        _view.Refresh();
        UpdateSummary();
    }

    private void ColumnHeader_OnClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader { Column: { } column } || column == CheckColumn) return;
        string key = column == NameColumn ? "Name"
            : column == ModeColumn ? "Category"
            : column == LocalColumn ? "Local"
            : column == CloudColumn ? "Cloud"
            : column == StateColumn ? "State"
            : "";
        if (key.Length == 0) return;
        _sortDirection = _sortKey == key && _sortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending : ListSortDirection.Ascending;
        _sortKey = key;
        ApplySort();
    }

    private void ApplySort()
    {
        if (_view is ListCollectionView view)
            view.CustomSort = _sortKey is null ? null : new EntryComparer(_sortKey, _sortDirection);
        UpdateHeaderArrows();
    }

    private void UpdateHeaderArrows()
    {
        SetHeaderLabel(NameColumn, "Название", "Name");
        SetHeaderLabel(ModeColumn, "Режим", "Category");
        SetHeaderLabel(LocalColumn, "На ПК", "Local");
        SetHeaderLabel(CloudColumn, "В облаке", "Cloud");
        SetHeaderLabel(StateColumn, "Состояние", "State");
    }

    private void SetHeaderLabel(GridViewColumn column, string label, string key) =>
        column.Header = _sortKey == key ? label + (_sortDirection == ListSortDirection.Ascending ? " ▲" : " ▼") : label;

    private sealed class EntryComparer(string key, ListSortDirection direction) : IComparer
    {
        public int Compare(object? x, object? y)
        {
            var a = ((EntryRow)x!).Entry;
            var b = ((EntryRow)y!).Entry;
            int result = key switch
            {
                "Name" => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase),
                "Category" => string.Compare(a.CategoryText, b.CategoryText, StringComparison.CurrentCultureIgnoreCase),
                "Local" => a.HasLocal.CompareTo(b.HasLocal),
                "Cloud" => Nullable.Compare(a.Remote?.UpdatedAt, b.Remote?.UpdatedAt),
                "State" => a.State.CompareTo(b.State),
                _ => 0
            };
            if (result == 0) result = string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            return direction == ListSortDirection.Ascending ? result : -result;
        }
    }

    private List<CloudSyncEntry> VisibleEntries() => _view?.Cast<EntryRow>().Select(r => r.Entry).ToList() ?? [];
    private List<CloudSyncEntry> SelectedEntries() => _entries.Where(r => r.IsChecked).Select(r => r.Entry).ToList();

    private void UpdateSummary()
    {
        List<CloudSyncEntry> visible = VisibleEntries();
        foreach (FilterOption option in _stateOptions)
        {
            int count = _entries.Count(r => Matches(r.Entry, includeState: false) && option.Match(r.Entry));
            option.Label = $"{option.Title} ({count})";
        }

        int up = visible.Count(e => e.State is CloudEntryState.LocalOnly or CloudEntryState.LocalChanged);
        int down = visible.Count(e => e.State is CloudEntryState.CloudOnly or CloudEntryState.CloudChanged);
        int decide = visible.Count(e => e.NeedsDecision);
        string decideText = decide > 0 ? $" · выбрать: {decide}" : "";
        SyncAllHint.Text = up + down + decide == 0 ? "Всё синхронизировано" : $"Отправить {up} · получить {down}{decideText}";
        UploadAllHint.Text = up + decide == 0 ? "Нечего отправлять" : $"К отправке: {up}{decideText}";
        DownloadAllHint.Text = down + decide == 0 ? "Нечего получать" : $"К получению: {down}{decideText}";
        SyncAllButton.Tag = up + down + decide;
        UploadAllButton.Tag = up + decide;
        DownloadAllButton.Tag = down + decide;

        ShownText.Text = visible.Count == _entries.Count ? $"Записей: {_entries.Count}" : $"Показано {visible.Count} из {_entries.Count}";
        EmptyState.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyTitle.Text = _entries.Count == 0 ? "Сохранений пока нет" : "Ничего не найдено";
        EmptyHint.Text = _entries.Count == 0
            ? "Сохраните фрактал в его менеджере сохранений или получите записи из облака на другом ПК."
            : "Измените поиск или фильтры режима и состояния.";
        UpdateSelection();
    }

    /// <summary>Ticking a box never touches any other row's check state; only this handles the bars/counts side effects.</summary>
    private void RowCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (RenameBar.Visibility == Visibility.Visible || DeleteBar.Visibility == Visibility.Visible) CloseBars();
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        List<CloudSyncEntry> selected = SelectedEntries();
        List<EntryRow> visible = _view?.Cast<EntryRow>().ToList() ?? [];
        int checkedVisible = visible.Count(r => r.IsChecked);
        SelectAllBox.IsChecked = visible.Count == 0 || checkedVisible == 0 ? false : checkedVisible >= visible.Count ? true : null;
        bool idle = _operation is null;
        int up = selected.Count(e => e.HasLocal && e.State is not CloudEntryState.Synced);
        int down = selected.Count(e => e.HasRemote && e.State is not (CloudEntryState.Synced or CloudEntryState.Unsupported));
        int remote = selected.Count(e => e.HasRemote);
        int local = selected.Count(e => e.HasLocal);
        UploadSelectedText.Text = up > 0 ? $"Отправить ({up})" : "Отправить";
        DownloadSelectedText.Text = down > 0 ? $"Получить ({down})" : "Получить";
        DeleteText.Text = remote > 0 ? $"Удалить из облака ({remote})" : "Удалить из облака";
        DeleteLocalText.Text = local > 0 ? $"Удалить с ПК ({local})" : "Удалить с ПК";
        UploadSelectedButton.IsEnabled = idle && up > 0;
        DownloadSelectedButton.IsEnabled = idle && down > 0;
        RenameButton.IsEnabled = idle && selected is [var single] && CanRename(single);
        DeleteButton.IsEnabled = idle && remote > 0;
        DeleteLocalButton.IsEnabled = idle && local > 0;
        SelectionText.Text = selected.Count == 0
            ? "Отметьте записи галочками, чтобы работать только с ними."
            : $"Отмечено: {selected.Count}";
        SyncAllButton.IsEnabled = idle && SyncAllButton.Tag is > 0;
        UploadAllButton.IsEnabled = idle && UploadAllButton.Tag is > 0;
        DownloadAllButton.IsEnabled = idle && DownloadAllButton.Tag is > 0;
    }

    private static bool CanRename(CloudSyncEntry entry) => entry.State is CloudEntryState.Synced or CloudEntryState.LocalOnly
        or CloudEntryState.CloudOnly or CloudEntryState.Unsupported;

    private void SelectAllBox_OnClick(object sender, RoutedEventArgs e)
    {
        List<EntryRow> visible = _view?.Cast<EntryRow>().ToList() ?? [];
        bool selectAll = visible.Any(r => !r.IsChecked);
        foreach (EntryRow row in visible) row.IsChecked = selectAll;
        CloseBars();
        UpdateSelection();
    }

    private void EntriesList_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        double others = CheckColumn.Width + ModeColumn.Width + LocalColumn.Width + CloudColumn.Width + StateColumn.Width;
        NameColumn.Width = Math.Max(160, EntriesList.ActualWidth - others - 26);
    }

    // ───────────────────────────── Status ─────────────────────────────

    private enum StatusTone { Neutral, Success, Warning, Danger }

    private void SetBusy(bool busy)
    {
        Progress.Visibility = Visibility.Collapsed;
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.IsEnabled = busy;
        RefreshButton.IsEnabled = LogoutButton.IsEnabled = !busy;
        UpdateSelection();
        if (busy) StatusIcon.Visibility = Visibility.Collapsed;
    }

    private void SetStatus(string text, StatusTone tone = StatusTone.Neutral)
    {
        StatusText.Text = text;
        if (tone == StatusTone.Neutral || text.Length == 0)
        {
            StatusIcon.Visibility = Visibility.Collapsed;
            return;
        }
        StatusIcon.Visibility = Visibility.Visible;
        StatusIcon.Text = tone switch { StatusTone.Success => "", StatusTone.Warning => "", _ => "" };
        StatusIcon.SetResourceReference(TextBlock.ForegroundProperty, tone switch
        {
            StatusTone.Success => "Theme.SuccessBrush",
            StatusTone.Warning => "Theme.WarningBrush",
            _ => "Theme.DangerBrush"
        });
    }

    private static string Plural(int count, string one, string few, string many)
    {
        int mod100 = count % 100, mod10 = count % 10;
        if (mod100 is >= 11 and <= 14) return many;
        return mod10 switch { 1 => one, >= 2 and <= 4 => few, _ => many };
    }

    private sealed class FilterOption(string key, string title, Func<CloudSyncEntry, bool> match) : INotifyPropertyChanged
    {
        private string _label = title;
        public string Key { get; } = key;
        public string Title { get; } = title;
        public Func<CloudSyncEntry, bool> Match { get; } = match;
        public string Label
        {
            get => _label;
            set
            {
                if (_label == value) return;
                _label = value;
                PropertyChanged?.Invoke(this, new(nameof(Label)));
            }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        public override string ToString() => _label;
    }

    /// <summary>
    /// One row's bound check state, kept independent of the ListView's own selection: clicking or filtering rows
    /// must never toggle checkmarks the user placed elsewhere in the list.
    /// </summary>
    private sealed class EntryRow(CloudSyncEntry entry) : INotifyPropertyChanged
    {
        private bool _isChecked;
        public CloudSyncEntry Entry { get; } = entry;
        public string Key => Entry.Key;
        public string Name => Entry.Name;
        public string? NameHint => Entry.NameHint;
        public string CategoryText => Entry.CategoryText;
        public bool HasLocal => Entry.HasLocal;
        public bool HasRemote => Entry.HasRemote;
        public string CloudText => Entry.CloudText;
        public string StatusText => Entry.StatusText;
        public string Tone => Entry.Tone;
        public string StatusDescription => Entry.StatusDescription;

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked == value) return;
                _isChecked = value;
                PropertyChanged?.Invoke(this, new(nameof(IsChecked)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
