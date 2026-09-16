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

    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    internal static string RelativePath(LocalCloudSave local) => Path.GetRelativePath(AppPaths.SavesRoot, local.FilePath);

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
    public void Set(CloudSave remote, LocalCloudSave local)
    {
        Links.RemoveAll(l => l.Id == remote.Id || l.LocalPath.Equals(CloudSaveRepository.RelativePath(local), StringComparison.OrdinalIgnoreCase));
        Links.Add(new(remote.Id, CloudSaveRepository.RelativePath(local), remote.Revision, local.Hash));
        Save();
    }
    public void Remove(Guid id) { Links.RemoveAll(l => l.Id == id); Save(); }
    private void Save()
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
