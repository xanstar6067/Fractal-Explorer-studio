namespace FractalExplorerWPF.Models;

public sealed record CloudSave(Guid Id, string Name, long Revision, DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt, string? JsonData = null);

// Deliberately not a record: generated ToString must never print credentials.
internal sealed class CloudTokens
{
    public string TokenType { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public long ExpiresIn { get; set; }
    public string RefreshToken { get; set; } = "";
}

internal sealed class CloudCredential
{
    public string Email { get; set; } = "";
    public string RefreshToken { get; set; } = "";
}

public sealed record LocalCloudSave(string FilePath, string Category, string Name, string JsonData, string Hash);
public sealed record CloudLink(Guid Id, string LocalPath, long Revision, string Hash);

/// <summary>Cached facts about one cloud revision, so listing does not download every record again.</summary>
public sealed record CloudRemoteInfo(Guid Id, long Revision, string? Category, string? Hash, string? Problem);

/// <summary>Where one save lives and how its copies relate. Computed on every refresh.</summary>
public enum CloudEntryState
{
    Synced,
    LocalOnly,
    CloudOnly,
    LocalChanged,
    CloudChanged,
    BothChanged,
    /// <summary>Unlinked local and cloud saves of the same mode and name with different parameters.</summary>
    SameName,
    CloudDeleted,
    LocalDeleted,
    Unsupported
}

/// <summary>One row of the cloud manager: a local save, a cloud save, or a linked pair.</summary>
public sealed record CloudSyncEntry(CloudEntryState State, LocalCloudSave? Local, CloudSave? Remote,
    CloudLink? Link, string? Category, string? Problem = null)
{
    public string Key => Local is not null ? "L:" + Local.FilePath.ToUpperInvariant() : "R:" + Remote!.Id.ToString("N");
    public string Name => Local?.Name ?? Remote?.Name ?? "";
    public string CategoryText => Category ?? "—";
    public bool HasLocal => Local is not null;
    public bool HasRemote => Remote is not null;

    public string? NameHint => Local is not null && Remote is not null && !string.Equals(Local.Name, Remote.Name, StringComparison.Ordinal)
        ? $"в облаке: «{Remote.Name}»" : null;

    public string CloudText => Remote is null ? "—" : $"v{Remote.Revision} · {Remote.UpdatedAt.LocalDateTime:dd.MM.yy HH:mm}";

    public string StatusText => State switch
    {
        CloudEntryState.Synced => "Синхронизировано",
        CloudEntryState.LocalOnly => "Только на ПК",
        CloudEntryState.CloudOnly => "Только в облаке",
        CloudEntryState.LocalChanged => "Изменено на ПК",
        CloudEntryState.CloudChanged => "Изменено в облаке",
        CloudEntryState.BothChanged => "Конфликт версий",
        CloudEntryState.SameName => "Уже есть в облаке",
        CloudEntryState.CloudDeleted => "Удалено из облака",
        CloudEntryState.LocalDeleted => "Удалено на ПК",
        _ => "Не поддерживается"
    };

    /// <summary>Colour group of the status badge: Success, Info, Warning, Danger or Muted.</summary>
    public string Tone => State switch
    {
        CloudEntryState.Synced => "Success",
        CloudEntryState.LocalOnly or CloudEntryState.CloudOnly => "Info",
        CloudEntryState.LocalChanged or CloudEntryState.CloudChanged => "Info",
        CloudEntryState.BothChanged or CloudEntryState.SameName => "Warning",
        CloudEntryState.CloudDeleted or CloudEntryState.LocalDeleted => "Danger",
        _ => "Muted"
    };

    public string StatusDescription => State switch
    {
        CloudEntryState.Synced => "Версии на этом ПК и в облаке совпадают.",
        CloudEntryState.LocalOnly => "Сохранения нет в облаке. «Отправить» создаст облачную копию.",
        CloudEntryState.CloudOnly => "Сохранения нет на этом ПК. «Получить» скачает его.",
        CloudEntryState.LocalChanged => "Версия на ПК новее. «Отправить» обновит облако.",
        CloudEntryState.CloudChanged => "Версия в облаке новее. «Получить» обновит ПК, прежний файл уйдёт в Корзину.",
        CloudEntryState.BothChanged => "Сохранение изменено и на ПК, и в облаке. При передаче будет предложен выбор.",
        CloudEntryState.SameName => "В облаке уже есть сохранение этого режима с тем же именем, но с другими параметрами. При передаче будет предложено: сохранить с префиксом, заменить или пропустить.",
        CloudEntryState.CloudDeleted => "Облачная копия удалена. Синхронизация её не восстанавливает; «Отправить» предложит загрузить снова.",
        CloudEntryState.LocalDeleted => "Локальная копия удалена. Синхронизация её не восстанавливает; «Получить» предложит скачать снова.",
        _ => Problem ?? "Запись другого формата или более новой версии приложения. Её можно только переименовать или удалить."
    };

    public bool CanUpload => State is CloudEntryState.LocalOnly or CloudEntryState.LocalChanged or CloudEntryState.BothChanged
        or CloudEntryState.SameName or CloudEntryState.CloudDeleted;
    public bool CanDownload => State is CloudEntryState.CloudOnly or CloudEntryState.CloudChanged or CloudEntryState.BothChanged
        or CloudEntryState.SameName or CloudEntryState.LocalDeleted;
    public bool NeedsSync => State is CloudEntryState.LocalOnly or CloudEntryState.CloudOnly or CloudEntryState.LocalChanged
        or CloudEntryState.CloudChanged or CloudEntryState.BothChanged or CloudEntryState.SameName;
    public bool NeedsDecision => State is CloudEntryState.BothChanged or CloudEntryState.SameName;
}

public enum CloudTransferMode { Upload, Download, Sync }

public enum CloudCollisionKind
{
    /// <summary>Same mode and name on both sides, different parameters, not linked.</summary>
    SameName,
    BothChanged,
    /// <summary>Explicit upload of a save whose cloud copy is newer.</summary>
    CloudNewer,
    /// <summary>Explicit download of a save whose local copy is newer.</summary>
    LocalNewer,
    CloudDeleted,
    LocalDeleted
}

public enum CloudCollisionAction { Skip, KeepBoth, UseLocal, UseCloud, Restore, CancelAll }

public sealed record CloudCollision(CloudCollisionKind Kind, CloudTransferMode Mode, LocalCloudSave? Local,
    CloudSave? Remote, int Remaining);

public sealed record CloudCollisionDecision(CloudCollisionAction Action, string Prefix = "", bool ApplyToAll = false);

public sealed record CloudProgress(int Done, int Total, string Message);

public sealed class CloudTransferReport
{
    public int Uploaded { get; set; }
    public int Downloaded { get; set; }
    public int Linked { get; set; }
    public int Renamed { get; set; }
    public int Deleted { get; set; }
    public int Skipped { get; set; }
    /// <summary>Nothing to transfer in the chosen direction: the other side already has a newer version.</summary>
    public int NewerElsewhere { get; set; }
    /// <summary>Deletions are neither propagated nor undone by synchronization.</summary>
    public int DeletedElsewhere { get; set; }
    public int Unsupported { get; set; }
    public List<string> Failures { get; } = [];
    public bool Cancelled { get; set; }

    public string Describe(string deletedLabel = "удалено из облака")
    {
        var parts = new List<string>();
        if (Uploaded > 0) parts.Add($"отправлено: {Uploaded}");
        if (Downloaded > 0) parts.Add($"получено: {Downloaded}");
        if (Linked > 0) parts.Add($"совпали и связаны: {Linked}");
        if (Renamed > 0) parts.Add($"переименовано: {Renamed}");
        if (Deleted > 0) parts.Add($"{deletedLabel}: {Deleted}");
        if (Skipped > 0) parts.Add($"пропущено: {Skipped}");
        if (NewerElsewhere > 0) parts.Add($"новее на другой стороне: {NewerElsewhere}");
        if (DeletedElsewhere > 0) parts.Add($"удалены с одной стороны: {DeletedElsewhere}");
        if (Unsupported > 0) parts.Add($"другой формат: {Unsupported}");
        if (Failures.Count > 0) parts.Add($"ошибок: {Failures.Count}");
        string head = Cancelled ? "Операция остановлена" : "Готово";
        string body = parts.Count == 0 ? "изменений не потребовалось" : string.Join(", ", parts);
        string tail = Failures.Count > 0 ? " Первая ошибка: " + Failures[0] : "";
        return $"{head}: {body}.{tail}";
    }
}
