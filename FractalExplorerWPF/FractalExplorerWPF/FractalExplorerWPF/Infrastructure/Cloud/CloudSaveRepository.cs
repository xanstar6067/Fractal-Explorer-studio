using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FractalExplorerWPF.Infrastructure.Migrations;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure.Cloud;

/// <summary>Versioned payload inside the API's string JsonData; no paths, previews or credentials.</summary>
public static class CloudSaveRepository
{
    public static IReadOnlyList<LocalCloudSave> ListLocal(out int unreadable)
    {
        var result = new List<LocalCloudSave>();
        unreadable = 0;
        if (!Directory.Exists(AppPaths.SavesRoot)) return result;
        foreach (string directory in Directory.EnumerateDirectories(AppPaths.SavesRoot))
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
            foreach (string path in Directory.EnumerateFiles(directory, "*.json"))
            {
                try { result.Add(ReadLocal(path)); }
                catch (Exception e) when (e is IOException or JsonException or InvalidOperationException or UnauthorizedAccessException)
                { unreadable++; }
            }
        }
        return result.OrderBy(s => s.Category).ThenBy(s => s.Name).ToList();
    }

    public static LocalCloudSave ReadLocal(string path)
    {
        EnsureSavePath(path);
        if (new FileInfo(path).Length > FractalCloudClient.MaxJsonBytes * 8L)
            throw new InvalidOperationException("Локальное сохранение слишком велико для облака.");
        JsonObject state = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
            ?? throw new InvalidOperationException("Сохранение должно содержать JSON-объект.");
        string category = Path.GetFileName(Path.GetDirectoryName(path))!;
        string name = state["SaveName"]?.GetValue<string>() ?? throw new InvalidOperationException("Нет имени сохранения.");
        string payload = new JsonObject
        {
            ["format"] = "FractalExplorerWPF", ["version"] = 1, ["category"] = category, ["state"] = state
        }.ToJsonString();
        return new(path, category, name, payload, Hash(payload));
    }

    /// <summary>The local save as it is now, or null when its file has been removed.</summary>
    public static LocalCloudSave? TryReadLocal(string path) => File.Exists(path) ? ReadLocal(path) : null;

    public static (string Category, JsonObject State) Decode(CloudSave save)
    {
        if (save.JsonData is null) throw new InvalidOperationException("Сервер не прислал данные сохранения.");
        FractalCloudClient.ValidateSave(save.Name, save.JsonData);
        JsonObject envelope = JsonNode.Parse(save.JsonData) as JsonObject
            ?? throw new InvalidOperationException("Неизвестный формат облачного сохранения.");
        if (envelope["format"]?.GetValue<string>() != "FractalExplorerWPF" || envelope["version"]?.GetValue<int>() != 1)
            throw new InvalidOperationException("Это сохранение другого формата. Оно доступно для удаления, но не для импорта.");
        string category = envelope["category"]?.GetValue<string>() ?? "";
        if (category.Length == 0 || category != AppPaths.ToSafeFileName(category) || category.Contains('.') ||
            category.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_' && c != '-'))
            throw new InvalidOperationException("Недопустимая категория сохранения.");
        JsonObject state = envelope["state"]?.DeepClone() as JsonObject
            ?? throw new InvalidOperationException("В облачном сохранении отсутствуют параметры.");
        if (SaveFormat.ReadVersion(state) > SaveFormat.CurrentVersion)
            throw new InvalidOperationException("Сохранение создано более новой версией приложения. Обновите приложение.");
        state["SaveName"] = save.Name;
        SaveFormat.UpgradeInPlace(category, state);
        return (category, state);
    }

    /// <summary>Category and content hash of a downloaded revision; a problem message instead when it cannot be imported.</summary>
    public static CloudRemoteInfo Describe(CloudSave save)
    {
        try
        {
            (string category, _) = Decode(save);
            return new(save.Id, save.Revision, category, RemoteHash(save.JsonData!), null);
        }
        catch (Exception e) when (e is InvalidOperationException or JsonException or FormatException)
        {
            return new(save.Id, save.Revision, null, null, e.Message);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // One odd record must not break the whole list, but a failing upgrade is worth seeing in the log.
            CrashLogger.Log($"CloudSaveRepository.Describe: {save.Id}", e);
            return new(save.Id, save.Revision, null, null, "Не удалось разобрать облачную запись.");
        }
    }

    public static LocalCloudSave Import(CloudSave save, LocalCloudSave? replacing = null)
    {
        var (category, state) = Decode(save);
        if (replacing is not null && replacing.Category != category)
            throw new InvalidOperationException("Облачное сохранение сменило категорию; импортируйте его отдельной копией.");
        string directory = AppPaths.GetSavesDirectory(category);
        string path = replacing?.FilePath ?? AppPaths.GetFreeSaveFilePath(directory, save.Name);
        EnsureSavePath(path);
        Directory.CreateDirectory(directory);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, state.ToJsonString(JsonOptionsFactory.Create()));
            RecycleBin.ReplaceWith(temporary, path);
            if (replacing is not null) RecycleBin.TrySend(Path.ChangeExtension(path, ".png"));
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return ReadLocal(path);
    }

    /// <summary>
    /// Gives a local save a new name. The file follows the name when possible; the preview stays valid and moves
    /// with it; the previous JSON goes to the Recycle Bin like any replaced save.
    /// </summary>
    public static LocalCloudSave RenameLocal(LocalCloudSave local, string newName)
    {
        FractalCloudClient.ValidateName(newName);
        EnsureSavePath(local.FilePath);
        JsonObject state = JsonNode.Parse(File.ReadAllText(local.FilePath)) as JsonObject
            ?? throw new InvalidOperationException("Сохранение должно содержать JSON-объект.");
        state["SaveName"] = newName;
        string directory = Path.GetDirectoryName(local.FilePath)!;
        bool keepPath = string.Equals(AppPaths.ToSafeFileName(newName), Path.GetFileNameWithoutExtension(local.FilePath),
            StringComparison.OrdinalIgnoreCase);
        string path = keepPath ? local.FilePath : AppPaths.GetFreeSaveFilePath(directory, newName);
        EnsureSavePath(path);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, state.ToJsonString(JsonOptionsFactory.Create()));
            if (keepPath) RecycleBin.ReplaceWith(temporary, path);
            else
            {
                File.Move(temporary, path, overwrite: false);
                string oldPreview = Path.ChangeExtension(local.FilePath, ".png");
                string newPreview = Path.ChangeExtension(path, ".png");
                if (File.Exists(oldPreview))
                {
                    RecycleBin.Send(newPreview);
                    File.Move(oldPreview, newPreview);
                }
                RecycleBin.Send(local.FilePath);
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return ReadLocal(path);
    }

    /// <summary>Replaces the embedded desktop name; opaque data of other clients is left intact.</summary>
    public static string WithSaveName(string jsonData, string name)
    {
        try
        {
            if (JsonNode.Parse(jsonData) is JsonObject envelope && envelope["format"]?.GetValue<string>() == "FractalExplorerWPF" &&
                envelope["state"] is JsonObject state)
            {
                state["SaveName"] = name;
                return envelope.ToJsonString();
            }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException) { }
        return jsonData;
    }

    /// <summary>Top-level parameters that differ between two payloads, for showing a conflict to the user.</summary>
    public static IReadOnlyList<CloudParameterDifference> CompareParameters(string? localJson, string? remoteJson, int limit = 60)
    {
        JsonObject? local = TryState(localJson), remote = TryState(remoteJson);
        if (local is null || remote is null) return [];
        var result = new List<CloudParameterDifference>();
        foreach (string key in local.Select(p => p.Key).Concat(remote.Select(p => p.Key)).Distinct(StringComparer.Ordinal))
        {
            string? a = local[key]?.ToJsonString(), b = remote[key]?.ToJsonString();
            if (a == b) continue;
            result.Add(new(key, Shorten(a), Shorten(b)));
            if (result.Count == limit) break;
        }
        return result;

        static JsonObject? TryState(string? json)
        {
            if (json is null) return null;
            try { return (JsonNode.Parse(json) as JsonObject)?["state"] as JsonObject; }
            catch (JsonException) { return null; }
        }

        static string Shorten(string? value)
        {
            if (value is null) return "—";
            if (value.StartsWith('{') || value.StartsWith('[')) return value.Length <= 40 ? value : "(составное значение)";
            value = value.Trim('"');
            return value.Length <= 40 ? value : value[..39] + "…";
        }
    }

    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    internal static string RemoteHash(string jsonData) => Hash(JsonNode.Parse(jsonData)!.ToJsonString());
    internal static string RelativePath(LocalCloudSave local) => Path.GetRelativePath(AppPaths.SavesRoot, local.FilePath);
    internal static string CategoryOfLink(CloudLink link) => Path.GetDirectoryName(link.LocalPath) ?? "";

    internal static string ResolvePath(string relative)
    {
        string path = Path.GetFullPath(Path.Combine(AppPaths.SavesRoot, relative));
        EnsureSavePath(path);
        return path;
    }

    private static void EnsureSavePath(string path)
    {
        string root = Path.GetFullPath(AppPaths.SavesRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(path);
        string relative = Path.GetRelativePath(root, full);
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
            relative.Split(Path.DirectorySeparatorChar).Length != 2 || Path.GetExtension(full) != ".json")
            throw new InvalidOperationException("Путь сохранения выходит за пределы каталога Saves.");
        foreach (string candidate in new[] { AppPaths.SavesRoot, Path.GetDirectoryName(full)!, full })
            if ((File.Exists(candidate) || Directory.Exists(candidate)) &&
                (File.GetAttributes(candidate) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Облачный импорт не поддерживает ссылки на другие каталоги.");
    }
}

public sealed record CloudParameterDifference(string Name, string Local, string Remote);

/// <summary>Separate, non-secret sync index for each server/account. Local save formats stay unchanged.</summary>
public sealed class CloudSyncIndex
{
    private readonly string _path;
    public List<CloudLink> Links { get; }
    public CloudSyncIndex(string server, string email)
    {
        _path = AppPaths.GetSettingsFile("cloud-sync-" + CloudSaveRepository.Hash(server + "\n" + email.ToLowerInvariant()) + ".json");
        Links = File.Exists(_path)
            ? JsonSerializer.Deserialize<List<CloudLink>>(File.ReadAllText(_path))
                ?? throw new InvalidOperationException("Индекс синхронизации повреждён.")
            : [];
        foreach (CloudLink link in Links) CloudSaveRepository.ResolvePath(link.LocalPath);
    }

    public CloudLink? Find(LocalCloudSave local) => Links.FirstOrDefault(l =>
        l.LocalPath.Equals(CloudSaveRepository.RelativePath(local), StringComparison.OrdinalIgnoreCase));
    public CloudLink? FindById(Guid id) => Links.FirstOrDefault(l => l.Id == id);
    public void Set(CloudSave remote, LocalCloudSave local, bool persist = true)
    {
        Links.RemoveAll(l => l.Id == remote.Id || l.LocalPath.Equals(CloudSaveRepository.RelativePath(local), StringComparison.OrdinalIgnoreCase));
        Links.Add(new(remote.Id, CloudSaveRepository.RelativePath(local), remote.Revision, local.Hash));
        if (persist) Save();
    }
    public void Remove(Guid id) { Links.RemoveAll(l => l.Id == id); Save(); }
    public void Save()
    {
        string temporary = AppPaths.EnsureDirectoryFor(_path) + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(Links));
            File.Move(temporary, _path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

/// <summary>
/// Disposable per-account cache of cloud revisions already inspected (mode, content hash, import problem).
/// The list endpoint returns metadata only, so without it every refresh would download every unlinked record.
/// </summary>
public sealed class CloudRemoteCache
{
    private readonly string _path;
    private readonly Dictionary<Guid, CloudRemoteInfo> _items;

    public CloudRemoteCache(string server, string email)
    {
        _path = AppPaths.GetSettingsFile("cloud-remote-" + CloudSaveRepository.Hash(server + "\n" + email.ToLowerInvariant()) + ".json");
        try
        {
            _items = File.Exists(_path)
                ? (JsonSerializer.Deserialize<List<CloudRemoteInfo>>(File.ReadAllText(_path)) ?? []).ToDictionary(i => i.Id)
                : [];
        }
        catch (Exception e) when (e is JsonException or IOException or ArgumentException)
        {
            _items = []; // A cache can always be rebuilt from the server.
        }
    }

    public CloudRemoteInfo? Find(CloudSave remote) =>
        _items.TryGetValue(remote.Id, out CloudRemoteInfo? info) && info.Revision == remote.Revision ? info : null;
    public void Set(CloudRemoteInfo info) => _items[info.Id] = info;
    public void Remove(Guid id) => _items.Remove(id);
    public void Retain(IEnumerable<Guid> ids)
    {
        var alive = ids.ToHashSet();
        foreach (Guid id in _items.Keys.Where(id => !alive.Contains(id)).ToList()) _items.Remove(id);
    }

    public void Save()
    {
        string temporary = AppPaths.EnsureDirectoryFor(_path) + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(_items.Values.ToList()));
            File.Move(temporary, _path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
