using System.IO;
using System.Net;
using System.Text.Json.Nodes;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure.Cloud;

public sealed class CloudSyncService(FractalCloudClient client, CloudSyncIndex index,
    Func<LocalCloudSave, CloudSave, CloudConflictChoice> resolve)
{
    public CloudSyncIndex Index => index;

    public async Task UploadAsync(LocalCloudSave selected, CancellationToken token)
    {
        LocalCloudSave local = CloudSaveRepository.ReadLocal(selected.FilePath);
        CloudLink? link = index.Find(local);
        if (link is null)
        {
            CloudSave created = await client.CreateAsync(local.Name, local.JsonData, token);
            index.Set(created, local);
        }
        else await SynchronizePairAsync(local, await client.GetAsync(link.Id, token), link, token);
    }

    public async Task DownloadAsync(CloudSave selected, CancellationToken token)
    {
        CloudSave remote = await client.GetAsync(selected.Id, token);
        CloudLink? link = index.Links.FirstOrDefault(l => l.Id == selected.Id);
        string? path = link is null ? null : CloudSaveRepository.ResolvePath(link.LocalPath);
        if (path is not null && File.Exists(path))
            await SynchronizePairAsync(CloudSaveRepository.ReadLocal(path), remote, link!, token);
        else index.Set(remote, CloudSaveRepository.Import(remote));
    }

    private async Task SynchronizePairAsync(LocalCloudSave local, CloudSave remote, CloudLink link, CancellationToken token)
    {
        bool localChanged = local.Hash != link.Hash;
        bool remoteChanged = remote.Revision != link.Revision;
        if (!localChanged && !remoteChanged) return;
        if (localChanged && !remoteChanged)
        {
            try
            {
                CloudSave updated = await client.UpdateAsync(remote.Id, local.Name, local.JsonData, link.Revision, token);
                index.Set(updated, local);
                return;
            }
            catch (CloudApiException e) when (e.Status == HttpStatusCode.Conflict)
            {
                remote = await client.GetAsync(remote.Id, token);
            }
        }
        if (!localChanged)
        {
            EnsureUnchanged(local);
            index.Set(remote, CloudSaveRepository.Import(remote, local));
            return;
        }
        await ResolveAsync(local, remote, token);
    }

    private async Task ResolveAsync(LocalCloudSave local, CloudSave remote, CancellationToken token)
    {
        // Fetch has already supplied the current data, not just currentRevision from a 409.
        CloudSaveRepository.Decode(remote);
        switch (resolve(local, remote))
        {
            case CloudConflictChoice.Cancel:
                throw new OperationCanceledException("Разрешение конфликта отменено.");
            case CloudConflictChoice.UseLocal:
                EnsureUnchanged(local);
                // Still conditional: another concurrent edit produces 409 and requires a new user choice.
                CloudSave updated = await client.UpdateAsync(remote.Id, local.Name, local.JsonData, remote.Revision, token);
                index.Set(updated, local);
                break;
            case CloudConflictChoice.UseCloud:
                EnsureUnchanged(local);
                index.Set(remote, CloudSaveRepository.Import(remote, local));
                break;
            case CloudConflictChoice.KeepBoth:
                EnsureUnchanged(local);
                string copyName = local.Name[..Math.Min(70, local.Name.Length)] + " (копия " + DateTime.Now.ToString("MMdd-HHmmss") + ")";
                // Persist the local alternative first, so a network failure cannot destroy either version.
                LocalCloudSave copy = CloudSaveRepository.Import(new(Guid.NewGuid(), copyName, 1, default, default, local.JsonData));
                CloudSave uploaded = await client.CreateAsync(copy.Name, copy.JsonData, token);
                index.Set(uploaded, copy);
                EnsureUnchanged(local);
                index.Set(remote, CloudSaveRepository.Import(remote, local));
                break;
        }
    }

    private static void EnsureUnchanged(LocalCloudSave local)
    {
        if (CloudSaveRepository.ReadLocal(local.FilePath).Hash != local.Hash)
            throw new InvalidOperationException("Локальное сохранение изменилось во время синхронизации. Повторите действие.");
    }

    public async Task<string> SynchronizeAllAsync(string? category, IProgress<string> progress, CancellationToken token)
    {
        var locals = CloudSaveRepository.ListLocal(out int unreadable).Where(l => category is null || l.Category == category).ToList();
        IReadOnlyList<CloudSave> remotes = await client.ListAsync(token);
        int skipped = unreadable;
        int completed = 0;
        foreach (CloudSave metadata in remotes)
        {
            token.ThrowIfCancellationRequested();
            CloudLink? known = index.Links.FirstOrDefault(l => l.Id == metadata.Id);
            if (known is not null)
            {
                string path = CloudSaveRepository.ResolvePath(known.LocalPath);
                if (category is not null && Path.GetFileName(Path.GetDirectoryName(path)) != category) continue;
                if (!File.Exists(path)) { skipped++; continue; }
                if (metadata.Revision == known.Revision && CloudSaveRepository.ReadLocal(path).Hash == known.Hash)
                { completed++; continue; } // Metadata is enough for unchanged linked records.
            }
            progress.Report($"Синхронизация: {metadata.Name}");
            CloudSave remote;
            try { remote = await client.GetAsync(metadata.Id, token); }
            catch (CloudApiException e) when (e.Status == HttpStatusCode.NotFound) { skipped++; continue; }
            string remoteCategory;
            try { (remoteCategory, _) = CloudSaveRepository.Decode(remote); }
            catch (Exception e) when (e is InvalidOperationException or System.Text.Json.JsonException) { skipped++; continue; }
            if (category is not null && remoteCategory != category) continue;
            CloudLink? link = index.Links.FirstOrDefault(l => l.Id == remote.Id);
            if (link is not null)
            {
                string path = CloudSaveRepository.ResolvePath(link.LocalPath);
                if (File.Exists(path)) await SynchronizePairAsync(CloudSaveRepository.ReadLocal(path), remote, link, token);
                else { skipped++; continue; } // Local deletion is never silently mirrored or undone.
            }
            else
            {
                string hash = CloudSaveRepository.Hash(JsonNode.Parse(remote.JsonData!)!.ToJsonString());
                LocalCloudSave? identical = locals.FirstOrDefault(l => l.Hash == hash && index.Find(l) is null);
                index.Set(remote, identical ?? CloudSaveRepository.Import(remote));
            }
            completed++;
        }
        foreach (LocalCloudSave original in locals)
        {
            token.ThrowIfCancellationRequested();
            CloudLink? link = index.Find(original);
            if (link is not null)
            {
                if (!remotes.Any(r => r.Id == link.Id)) skipped++; // Remote deletions require explicit handling.
                continue;
            }
            progress.Report($"Отправка: {original.Name}");
            await UploadAsync(original, token);
            completed++;
        }
        return $"Синхронизация завершена: {completed}. Пропущено: {skipped} (удалённые, повреждённые или чужой формат).";
    }
}
