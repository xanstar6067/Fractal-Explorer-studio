using System.IO;
using System.Net;
using System.Text.Json;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure.Cloud;

public sealed record CloudSnapshot(IReadOnlyList<CloudSyncEntry> Entries, int UnreadableLocal, DateTimeOffset LoadedAt);

/// <summary>
/// Manual synchronization between local saves and FractalCloud. Every operation re-reads both sides of a save
/// right before acting; anything that could lose a version is handed to the user as a <see cref="CloudCollision"/>.
/// </summary>
public sealed class CloudSyncService(FractalCloudClient client, CloudSyncIndex index, CloudRemoteCache cache)
{
    public const string DefaultPrefix = "Копия · ";

    public CloudSyncIndex Index => index;
    public CloudRemoteCache Cache => cache;

    public async Task<CloudSnapshot> LoadAsync(IProgress<CloudProgress>? progress, CancellationToken token)
    {
        progress?.Report(new(0, 0, "Чтение сохранений на этом ПК…"));
        (IReadOnlyList<LocalCloudSave> locals, int unreadable) = await Task.Run(() =>
        {
            IReadOnlyList<LocalCloudSave> list = CloudSaveRepository.ListLocal(out int count);
            return (list, count);
        }, token);
        progress?.Report(new(0, 0, "Получение списка из облака…"));
        List<CloudSave> remotes = (await client.ListAsync(token)).ToList();
        // The list has metadata only. Linked records are described by their local file; others are inspected once per revision.
        var linked = index.Links.Select(l => l.Id).ToHashSet();
        foreach (CloudSave remote in remotes.Where(r => r.JsonData is not null && !linked.Contains(r.Id) && cache.Find(r) is null))
            cache.Set(CloudSaveRepository.Describe(remote));
        List<CloudSave> unknown = remotes.Where(r => !linked.Contains(r.Id) && cache.Find(r) is null).ToList();
        try
        {
            for (int i = 0; i < unknown.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                progress?.Report(new(i, unknown.Count, $"Сведения об облачных записях: {i + 1} из {unknown.Count}"));
                try { cache.Set(CloudSaveRepository.Describe(await client.GetAsync(unknown[i].Id, token))); }
                catch (CloudApiException e) when (e.Status == HttpStatusCode.NotFound) { remotes.Remove(unknown[i]); }
            }
            cache.Retain(remotes.Select(r => r.Id));
        }
        finally { cache.Save(); }
        return new(BuildEntries(locals, remotes, index, cache), unreadable, DateTimeOffset.Now);
    }

    /// <summary>
    /// Pairs local and cloud saves. Unlinked saves of the same mode with identical content are linked silently;
    /// with the same name but different content they form one <see cref="CloudEntryState.SameName"/> row.
    /// </summary>
    public static List<CloudSyncEntry> BuildEntries(IReadOnlyList<LocalCloudSave> locals, IReadOnlyList<CloudSave> remotes,
        CloudSyncIndex index, CloudRemoteCache cache)
    {
        var entries = new List<CloudSyncEntry>();
        var localByPath = new Dictionary<string, LocalCloudSave>(StringComparer.OrdinalIgnoreCase);
        foreach (LocalCloudSave local in locals) localByPath[CloudSaveRepository.RelativePath(local)] = local;
        var remoteById = remotes.ToDictionary(r => r.Id);

        foreach (CloudLink link in index.Links)
        {
            localByPath.Remove(link.LocalPath, out LocalCloudSave? local);
            remoteById.Remove(link.Id, out CloudSave? remote);
            if (local is null && remote is null) continue;
            entries.Add(new(Classify(local, remote, link), local, remote, link, CloudSaveRepository.CategoryOfLink(link)));
        }

        var candidates = new Dictionary<(string Category, string Name), List<CloudSave>>();
        foreach (CloudSave remote in remoteById.Values.OrderByDescending(r => r.UpdatedAt))
        {
            CloudRemoteInfo? info = cache.Find(remote);
            if (info?.Problem is not null) entries.Add(new(CloudEntryState.Unsupported, null, remote, null, null, info.Problem));
            else if (info?.Category is null) entries.Add(new(CloudEntryState.CloudOnly, null, remote, null, null));
            else
            {
                var key = (info.Category, remote.Name.ToUpperInvariant());
                if (!candidates.TryGetValue(key, out List<CloudSave>? list)) candidates[key] = list = [];
                list.Add(remote);
            }
        }

        var unpaired = new List<LocalCloudSave>();
        bool linkedAny = false;
        foreach (LocalCloudSave local in localByPath.Values)
        {
            if (candidates.TryGetValue((local.Category, local.Name.ToUpperInvariant()), out List<CloudSave>? same) &&
                same.FirstOrDefault(r => cache.Find(r)?.Hash == local.Hash) is { } identical)
            {
                same.Remove(identical);
                index.Set(identical, local, persist: false);
                linkedAny = true;
                entries.Add(new(CloudEntryState.Synced, local, identical, index.Find(local), local.Category));
            }
            else unpaired.Add(local);
        }
        if (linkedAny) index.Save();

        foreach (LocalCloudSave local in unpaired)
        {
            if (candidates.TryGetValue((local.Category, local.Name.ToUpperInvariant()), out List<CloudSave>? same) && same.Count > 0)
            {
                entries.Add(new(CloudEntryState.SameName, local, same[0], null, local.Category));
                same.RemoveAt(0);
            }
            else entries.Add(new(CloudEntryState.LocalOnly, local, null, null, local.Category));
        }
        foreach (CloudSave remote in candidates.Values.SelectMany(list => list))
            entries.Add(new(CloudEntryState.CloudOnly, null, remote, null, cache.Find(remote)!.Category));

        return entries
            .OrderBy(e => e.Category ?? "￿", StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static CloudEntryState Classify(LocalCloudSave? local, CloudSave? remote, CloudLink link) => (local, remote) switch
    {
        (null, _) => CloudEntryState.LocalDeleted,
        (_, null) => CloudEntryState.CloudDeleted,
        _ => (local.Hash != link.Hash, remote.Revision != link.Revision) switch
        {
            (false, false) => CloudEntryState.Synced,
            (true, false) => CloudEntryState.LocalChanged,
            (false, true) => CloudEntryState.CloudChanged,
            _ => CloudEntryState.BothChanged
        }
    };

    /// <param name="explicitSelection">
    /// Saves the user ticked. Bulk operations never overwrite a newer version and never undo a deletion;
    /// for ticked saves those cases are asked about instead of skipped.
    /// </param>
    public async Task<CloudTransferReport> TransferAsync(IReadOnlyList<CloudSyncEntry> entries, CloudTransferMode mode,
        bool explicitSelection, Func<CloudCollision, CloudCollisionDecision> resolve,
        IProgress<CloudProgress>? progress, CancellationToken token)
    {
        var report = new CloudTransferReport();
        var batch = new Batch(mode, explicitSelection, resolve, report, token);
        var work = new List<CloudSyncEntry>();
        foreach (CloudSyncEntry entry in entries)
        {
            switch (Plan(entry.State, mode, explicitSelection))
            {
                case Planned.Work: work.Add(entry); batch.Kinds.Add(PredictKind(entry.State, mode)); break;
                case Planned.NewerElsewhere: report.NewerElsewhere++; break;
                case Planned.DeletedElsewhere: report.DeletedElsewhere++; break;
                case Planned.Unsupported: report.Unsupported++; break;
            }
        }
        if (work.Count == 0) return report;

        try
        {
            progress?.Report(new(0, work.Count, "Проверка актуальных списков…"));
            batch.Locals = (await Task.Run(() => CloudSaveRepository.ListLocal(out _), token)).ToList();
            batch.Remotes = (await client.ListAsync(token)).ToList();
            for (int i = 0; i < work.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                batch.Position = i;
                progress?.Report(new(i, work.Count, $"{Verb(mode)} {i + 1} из {work.Count}: {work[i].Name}"));
                try { await ProcessAsync(work[i], batch); }
                catch (Exception e) when (e is CloudApiException or InvalidOperationException or IOException
                                              or UnauthorizedAccessException or JsonException)
                {
                    report.Failures.Add($"«{work[i].Name}»: {e.Message}");
                }
            }
            progress?.Report(new(work.Count, work.Count, "Готово"));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested || batch.StoppedByUser)
        {
            report.Cancelled = true;
        }
        finally { cache.Save(); }
        return report;
    }

    private async Task ProcessAsync(CloudSyncEntry entry, Batch batch)
    {
        for (int attempt = 0; ; attempt++)
        {
            if (attempt == 3)
                throw new InvalidOperationException("Запись несколько раз изменилась во время операции. Обновите список и повторите.");
            // A conditional write that met a concurrent change must be decided by the user again, not by "apply to all".
            batch.ForceAsk = attempt > 0;

            LocalCloudSave? local = entry.Local is null ? null : CloudSaveRepository.TryReadLocal(entry.Local.FilePath);
            CloudLink? link = local is not null ? index.Find(local)
                : entry.Link is not null ? index.FindById(entry.Link.Id)
                : entry.Remote is not null ? index.FindById(entry.Remote.Id) : null;
            if (local is null && link is not null) local = CloudSaveRepository.TryReadLocal(CloudSaveRepository.ResolvePath(link.LocalPath));
            Guid? remoteId = link?.Id ?? entry.Remote?.Id;
            if (link is null && remoteId is Guid claimed && index.FindById(claimed) is not null) remoteId = null;

            CloudSave? remote = null;
            if (remoteId is Guid id)
            {
                try { remote = await client.GetAsync(id, batch.Token); }
                catch (CloudApiException e) when (e.Status == HttpStatusCode.NotFound) { cache.Remove(id); }
            }
            // A save present on one side only may still meet an unlinked namesake on the other side.
            if (link is null && remote is null && local is not null) remote = await FindRemoteNamesakeAsync(local, batch);
            if (link is null && local is null && remote is not null)
            {
                CloudRemoteInfo info = CloudSaveRepository.Describe(remote);
                cache.Set(info);
                if (info.Problem is not null) { batch.Report.Unsupported++; return; }
                local = FindLocalNamesake(info.Category!, remote.Name, batch);
            }
            if (local is null && remote is null) return;

            CloudEntryState state;
            if (link is not null) state = Classify(local, remote, link);
            else if (local is not null && remote is not null)
            {
                if (CloudSaveRepository.RemoteHash(remote.JsonData!) == local.Hash)
                {
                    index.Set(remote, local);
                    cache.Set(new(remote.Id, remote.Revision, local.Category, local.Hash, null));
                    batch.Report.Linked++;
                    return;
                }
                state = CloudEntryState.SameName;
            }
            else state = local is not null ? CloudEntryState.LocalOnly : CloudEntryState.CloudOnly;

            bool explicitSelection = batch.Explicit;
            CloudTransferMode mode = batch.Mode;
            bool completed = (state, mode) switch
            {
                (CloudEntryState.Synced, _) => true,
                (CloudEntryState.LocalOnly, not CloudTransferMode.Download) => await CreateAsync(local!, batch),
                (CloudEntryState.LocalChanged, not CloudTransferMode.Download) => await PutAsync(local!, remote!, batch),
                (CloudEntryState.CloudOnly, not CloudTransferMode.Upload) => Download(remote!, null, batch),
                (CloudEntryState.CloudChanged, not CloudTransferMode.Upload) => Download(remote!, local, batch),
                (CloudEntryState.CloudChanged, _) => explicitSelection
                    ? await ResolveAsync(CloudCollisionKind.CloudNewer, local, remote, link, batch)
                    : Count(() => batch.Report.NewerElsewhere++),
                (CloudEntryState.LocalChanged, _) => explicitSelection
                    ? await ResolveAsync(CloudCollisionKind.LocalNewer, local, remote, link, batch)
                    : Count(() => batch.Report.NewerElsewhere++),
                (CloudEntryState.BothChanged, _) => await ResolveAsync(CloudCollisionKind.BothChanged, local, remote, link, batch),
                (CloudEntryState.SameName, _) => await ResolveAsync(CloudCollisionKind.SameName, local, remote, link, batch),
                (CloudEntryState.CloudDeleted, not CloudTransferMode.Download) => explicitSelection
                    ? await ResolveAsync(CloudCollisionKind.CloudDeleted, local, remote, link, batch)
                    : Count(() => batch.Report.DeletedElsewhere++),
                (CloudEntryState.LocalDeleted, not CloudTransferMode.Upload) => explicitSelection
                    ? await ResolveAsync(CloudCollisionKind.LocalDeleted, local, remote, link, batch)
                    : Count(() => batch.Report.DeletedElsewhere++),
                _ => true // Nothing to move in this direction.
            };
            if (completed) return;
            // A conditional PUT met a concurrent change: re-read both sides and decide again.
        }
    }

    private async Task<bool> ResolveAsync(CloudCollisionKind kind, LocalCloudSave? local, CloudSave? remote, CloudLink? link, Batch batch)
    {
        if (batch.ForceAsk || !batch.Remembered.TryGetValue(kind, out CloudCollisionDecision? decision))
        {
            decision = batch.Resolve(new(kind, batch.Mode, local, remote, batch.RemainingOf(kind)));
            if (decision.ApplyToAll) batch.Remembered[kind] = decision;
        }
        switch (decision.Action)
        {
            case CloudCollisionAction.CancelAll:
                batch.StoppedByUser = true;
                throw new OperationCanceledException("Операция остановлена пользователем.");
            case CloudCollisionAction.Skip:
                batch.Report.Skipped++;
                return true;
            case CloudCollisionAction.Restore when kind == CloudCollisionKind.CloudDeleted && local is not null:
                return await CreateAsync(local, batch);
            case CloudCollisionAction.Restore when kind == CloudCollisionKind.LocalDeleted && remote is not null:
                return Download(remote, null, batch);
            case CloudCollisionAction.UseLocal when local is not null && remote is not null:
                EnsureUnchanged(local);
                return await PutAsync(local, remote, batch);
            case CloudCollisionAction.UseCloud when local is not null && remote is not null:
                return Download(remote, local, batch);
            case CloudCollisionAction.KeepBoth when local is not null && remote is not null:
                await KeepBothAsync(local, remote, link, decision.Prefix, batch);
                return true;
            default:
                throw new InvalidOperationException("Выбранное действие недоступно для этой записи.");
        }
    }

    /// <summary>
    /// The local version takes the prefixed name (never the cloud record), then goes where the operation goes:
    /// uploading sends the renamed copy, downloading brings the cloud version under the original name, sync does both.
    /// </summary>
    private async Task KeepBothAsync(LocalCloudSave local, CloudSave remote, CloudLink? link, string prefix, Batch batch)
    {
        EnsureUnchanged(local);
        LocalCloudSave renamed = CloudSaveRepository.RenameLocal(local, UniqueName(prefix, local, batch));
        if (link is not null) index.Remove(link.Id);
        batch.Locals.Add(renamed);
        batch.Report.Renamed++;
        if (batch.Mode != CloudTransferMode.Download) await CreateAsync(renamed, batch);
        if (batch.Mode != CloudTransferMode.Upload) Download(remote, null, batch);
    }

    private async Task<bool> CreateAsync(LocalCloudSave local, Batch batch)
    {
        CloudSave created = await client.CreateAsync(local.Name, local.JsonData, batch.Token);
        index.Set(created, local);
        cache.Set(new(created.Id, created.Revision, local.Category, local.Hash, null));
        batch.Remotes.Add(created);
        batch.Report.Uploaded++;
        return true;
    }

    private async Task<bool> PutAsync(LocalCloudSave local, CloudSave remote, Batch batch)
    {
        try
        {
            CloudSave updated = await client.UpdateAsync(remote.Id, local.Name, local.JsonData, remote.Revision, batch.Token);
            index.Set(updated, local);
            cache.Set(new(updated.Id, updated.Revision, local.Category, local.Hash, null));
            batch.Report.Uploaded++;
            return true;
        }
        catch (CloudApiException e) when (e.Status == HttpStatusCode.Conflict) { return false; }
    }

    private bool Download(CloudSave remote, LocalCloudSave? replacing, Batch batch)
    {
        if (replacing is not null) EnsureUnchanged(replacing);
        LocalCloudSave imported = CloudSaveRepository.Import(remote, replacing);
        index.Set(remote, imported);
        cache.Set(CloudSaveRepository.Describe(remote));
        batch.Locals.Add(imported);
        batch.Report.Downloaded++;
        return true;
    }

    private async Task<CloudSave?> FindRemoteNamesakeAsync(LocalCloudSave local, Batch batch)
    {
        foreach (CloudSave candidate in batch.Remotes
                     .Where(r => index.FindById(r.Id) is null && string.Equals(r.Name, local.Name, StringComparison.OrdinalIgnoreCase))
                     .OrderByDescending(r => r.UpdatedAt).ToList())
        {
            CloudSave full;
            try { full = await client.GetAsync(candidate.Id, batch.Token); }
            catch (CloudApiException e) when (e.Status == HttpStatusCode.NotFound) { continue; }
            CloudRemoteInfo info = CloudSaveRepository.Describe(full);
            cache.Set(info);
            if (info.Category == local.Category) return full;
        }
        return null;
    }

    private LocalCloudSave? FindLocalNamesake(string category, string name, Batch batch) => batch.Locals
        .Where(l => l.Category == category && string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase))
        .Select(l => CloudSaveRepository.TryReadLocal(l.FilePath))
        .FirstOrDefault(l => l is not null && l.Category == category &&
                             string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase) && index.Find(l) is null);

    private string UniqueName(string prefix, LocalCloudSave local, Batch batch)
    {
        string baseName = (prefix + local.Name).Trim();
        if (baseName.Length > FractalCloudClient.MaxNameLength) baseName = baseName[..FractalCloudClient.MaxNameLength].TrimEnd();
        string result = baseName;
        for (int number = 2; Taken(result); number++)
        {
            string suffix = $" ({number})";
            result = baseName[..Math.Min(baseName.Length, FractalCloudClient.MaxNameLength - suffix.Length)].TrimEnd() + suffix;
        }
        return result;

        bool Taken(string candidate) =>
            batch.Locals.Any(l => l.Category == local.Category && File.Exists(l.FilePath) &&
                                  string.Equals(l.Name, candidate, StringComparison.OrdinalIgnoreCase)) ||
            batch.Remotes.Any(r => string.Equals(r.Name, candidate, StringComparison.OrdinalIgnoreCase) &&
                                   (cache.Find(r)?.Category ?? local.Category) == local.Category);
    }

    public async Task RenameAsync(CloudSyncEntry entry, string newName, CancellationToken token)
    {
        newName = newName.Trim();
        FractalCloudClient.ValidateName(newName);
        switch (entry.State)
        {
            case CloudEntryState.LocalOnly:
                CloudSaveRepository.RenameLocal(CloudSaveRepository.TryReadLocal(entry.Local!.FilePath)
                    ?? throw new InvalidOperationException("Локальный файл уже удалён. Обновите список."), newName);
                return;
            case CloudEntryState.CloudOnly or CloudEntryState.Unsupported or CloudEntryState.Synced:
                CloudSave current = await client.GetAsync(entry.Remote!.Id, token);
                LocalCloudSave? local = null;
                if (entry.State == CloudEntryState.Synced)
                {
                    local = CloudSaveRepository.TryReadLocal(entry.Local!.FilePath);
                    CloudLink? link = local is null ? null : index.Find(local);
                    if (local is null || link is null || link.Id != current.Id || link.Hash != local.Hash || link.Revision != current.Revision)
                        throw new InvalidOperationException("Запись изменилась после обновления списка. Обновите список и повторите.");
                }
                string json = CloudSaveRepository.WithSaveName(current.JsonData!, newName);
                CloudSave updated = await client.UpdateAsync(current.Id, newName, json, current.Revision, token);
                cache.Set(CloudSaveRepository.Describe(updated with { Name = newName, JsonData = json }));
                cache.Save();
                if (local is not null) index.Set(updated, CloudSaveRepository.RenameLocal(local, newName));
                return;
            default:
                throw new InvalidOperationException("Переименовать можно синхронизированную запись или запись, которая есть только на одной стороне. Сначала синхронизируйте её.");
        }
    }

    /// <summary>Deletes cloud records only. Links stay as tombstones, so bulk operations do not upload them again.</summary>
    public async Task<CloudTransferReport> DeleteRemoteAsync(IReadOnlyList<CloudSyncEntry> entries,
        IProgress<CloudProgress>? progress, CancellationToken token)
    {
        var report = new CloudTransferReport();
        List<CloudSave> targets = entries.Where(e => e.Remote is not null).Select(e => e.Remote!).ToList();
        try
        {
            for (int i = 0; i < targets.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                CloudSave remote = targets[i];
                progress?.Report(new(i, targets.Count, $"Удаление {i + 1} из {targets.Count}: {remote.Name}"));
                try
                {
                    await client.DeleteAsync(remote.Id, remote.Revision, token);
                    report.Deleted++;
                }
                catch (CloudApiException e) when (e.Status == HttpStatusCode.NotFound) { report.Deleted++; }
                catch (CloudApiException e) when (e.Status == HttpStatusCode.Conflict)
                {
                    report.Failures.Add($"«{remote.Name}»: запись изменилась на другом ПК и не удалена. Обновите список.");
                    continue;
                }
                cache.Remove(remote.Id);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { report.Cancelled = true; }
        finally { cache.Save(); }
        return report;
    }

    private static void EnsureUnchanged(LocalCloudSave local)
    {
        if (CloudSaveRepository.TryReadLocal(local.FilePath)?.Hash != local.Hash)
            throw new InvalidOperationException("Локальное сохранение изменилось во время синхронизации. Повторите действие.");
    }

    private static bool Count(Action increment) { increment(); return true; }

    private enum Planned { None, Work, NewerElsewhere, DeletedElsewhere, Unsupported }

    private static Planned Plan(CloudEntryState state, CloudTransferMode mode, bool explicitSelection) => (state, mode) switch
    {
        (CloudEntryState.Synced, _) => Planned.None,
        (CloudEntryState.Unsupported, CloudTransferMode.Upload) => Planned.None,
        (CloudEntryState.Unsupported, _) => Planned.Unsupported,
        (CloudEntryState.BothChanged or CloudEntryState.SameName, _) => Planned.Work,
        (CloudEntryState.LocalOnly or CloudEntryState.LocalChanged, not CloudTransferMode.Download) => Planned.Work,
        (CloudEntryState.CloudOnly or CloudEntryState.CloudChanged, not CloudTransferMode.Upload) => Planned.Work,
        (CloudEntryState.CloudChanged, CloudTransferMode.Upload) or (CloudEntryState.LocalChanged, CloudTransferMode.Download)
            => explicitSelection ? Planned.Work : Planned.NewerElsewhere,
        (CloudEntryState.CloudDeleted, not CloudTransferMode.Download) or (CloudEntryState.LocalDeleted, not CloudTransferMode.Upload)
            => explicitSelection ? Planned.Work : Planned.DeletedElsewhere,
        _ => Planned.None
    };

    private static CloudCollisionKind? PredictKind(CloudEntryState state, CloudTransferMode mode) => state switch
    {
        CloudEntryState.SameName => CloudCollisionKind.SameName,
        CloudEntryState.BothChanged => CloudCollisionKind.BothChanged,
        CloudEntryState.CloudChanged when mode == CloudTransferMode.Upload => CloudCollisionKind.CloudNewer,
        CloudEntryState.LocalChanged when mode == CloudTransferMode.Download => CloudCollisionKind.LocalNewer,
        CloudEntryState.CloudDeleted => CloudCollisionKind.CloudDeleted,
        CloudEntryState.LocalDeleted => CloudCollisionKind.LocalDeleted,
        _ => null
    };

    private static string Verb(CloudTransferMode mode) => mode switch
    {
        CloudTransferMode.Upload => "Отправка",
        CloudTransferMode.Download => "Получение",
        _ => "Синхронизация"
    };

    private sealed class Batch(CloudTransferMode mode, bool explicitSelection,
        Func<CloudCollision, CloudCollisionDecision> resolve, CloudTransferReport report, CancellationToken token)
    {
        public CloudTransferMode Mode => mode;
        public bool Explicit => explicitSelection;
        public Func<CloudCollision, CloudCollisionDecision> Resolve => resolve;
        public CloudTransferReport Report => report;
        public CancellationToken Token => token;
        public List<LocalCloudSave> Locals { get; set; } = [];
        public List<CloudSave> Remotes { get; set; } = [];
        public List<CloudCollisionKind?> Kinds { get; } = [];
        public Dictionary<CloudCollisionKind, CloudCollisionDecision> Remembered { get; } = [];
        public int Position { get; set; }
        public bool ForceAsk { get; set; }
        public bool StoppedByUser { get; set; }
        public int RemainingOf(CloudCollisionKind kind) => Kinds.Skip(Position + 1).Count(k => k == kind);
    }
}
