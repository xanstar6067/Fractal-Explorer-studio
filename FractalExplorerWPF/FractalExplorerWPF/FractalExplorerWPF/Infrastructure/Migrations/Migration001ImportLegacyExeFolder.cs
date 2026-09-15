using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FractalExplorerWPF.Infrastructure.Migrations;

/// <summary>
/// Версия 1: перенос данных версий до 2.0 включительно из каталога <c>Saves</c> рядом с exe.
/// <list type="bullet">
/// <item>список <c>&lt;режим&gt;_saves.json</c> раскладывается по файлу на сохранение в <c>Saves\&lt;режим&gt;</c>;</item>
/// <item>PNG из <c>SavePrevData\&lt;режим&gt;\&lt;имя&gt;_&lt;время&gt;.png</c> встаёт рядом с сохранением;</item>
/// <item>палитры, темы и настройки копируются в свои каталоги, если там ещё нет файла.</item>
/// </list>
/// Старый каталог только читается. Превью встроенных точек интереса не переносятся — это
/// кэш, который пересчитывается кнопкой в менеджере сохранений.
/// </summary>
internal sealed class Migration001ImportLegacyExeFolder(string? legacyDirectory = null) : IUserDataMigration
{
    private const string SavesSuffix = "_saves.json";
    private const string LegacyTimestampFormat = "yyyyMMdd_HHmmss_fffffff";

    private static readonly Regex LegacyTimestampSuffix = new(@"_\d{8}_\d{6}_\d{7}$", RegexOptions.CultureInvariant);

    // Каталог превью совпадал с префиксом файла сохранений у всех режимов, кроме Аполлоновой прокладки.
    private static readonly Dictionary<string, string> LegacyPreviewFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Apollonian"] = "ApollonianGasket"
    };

    public int Version => 1;

    public string Description => "Перенос данных из папки Saves рядом с программой";

    public void Apply(UserDataMigrationContext context)
    {
        string source = Path.GetFullPath(legacyDirectory ?? AppPaths.LegacySavesDirectory);
        if (!Directory.Exists(source) || Overlaps(source, context.DataRoot)) return;

        var totals = new Totals();
        foreach (string file in Directory.EnumerateFiles(source).Order(StringComparer.OrdinalIgnoreCase))
        {
            string name = Path.GetFileName(file);
            if (name.Contains(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                if (name.EndsWith(SavesSuffix, StringComparison.OrdinalIgnoreCase))
                    ImportSaveList(source, file, name[..^SavesSuffix.Length], totals);
                else
                    ImportAuxiliaryFile(file, name, totals);
            }
            catch (Exception exception)
            {
                // Остальные файлы переносятся: старый каталог не тронут, файл можно перенести вручную.
                CrashLogger.Log($"Migration001ImportLegacyExeFolder: {file}", exception);
                totals.Failures.Add($"{name}: {exception.Message}");
            }
        }

        if (totals.Saves + totals.Previews + totals.Files > 0)
        {
            context.Notes.Add($"Данные прежней версии перенесены из «{source}»: сохранений — {totals.Saves}, " +
                              $"превью — {totals.Previews}, файлов палитр, тем и настроек — {totals.Files}. " +
                              "Старая папка не изменялась, её можно удалить вручную.");
        }
        if (totals.Failures.Count > 0)
        {
            context.Notes.Add("Не удалось перенести: " + string.Join("; ", totals.Failures) +
                              ". Подробности — в журнале ошибок.");
        }
    }

    private static void ImportSaveList(string source, string file, string category, Totals totals)
    {
        if (category.Length == 0) throw new InvalidDataException("В имени файла нет названия режима.");
        if (JsonNode.Parse(File.ReadAllText(file)) is not JsonArray items)
            throw new InvalidDataException("Ожидался JSON-массив сохранений.");

        string targetDirectory = Directory.CreateDirectory(AppPaths.GetSavesDirectory(category)).FullName;
        string previewDirectory = Path.Combine(source, "SavePrevData",
            LegacyPreviewFolders.GetValueOrDefault(category, category));
        ExistingSaves existing = ExistingSaves.Read(targetDirectory);
        var options = new JsonSerializerOptions { WriteIndented = true };

        foreach (JsonObject save in items.OfType<JsonObject>())
        {
            string originalName = ReadString(save, "SaveName") ?? "Save";
            DateTime? timestamp = ReadTimestamp(save);
            string timestampKey = save["Timestamp"]?.ToJsonString() ?? string.Empty;

            // Метка времени с точностью 100 нс опознаёт уже перенесённое сохранение, если шаг
            // прервался и выполняется повторно.
            if (timestampKey.Length == 0 || !existing.PathsByTimestamp.TryGetValue(timestampKey, out string? path))
            {
                string uniqueName = originalName;
                for (int index = 2; !existing.Names.Add(uniqueName); index++) uniqueName = $"{originalName} ({index})";
                if (uniqueName != originalName) save["SaveName"] = uniqueName;
                SaveFormat.Stamp(save);

                path = AppPaths.GetFreeSaveFilePath(targetDirectory, uniqueName);
                WriteNewFile(path, save.ToJsonString(options));
                if (timestampKey.Length > 0) existing.PathsByTimestamp[timestampKey] = path;
                totals.Saves++;
            }

            string previewPath = Path.ChangeExtension(path, ".png");
            if (!File.Exists(previewPath) && FindLegacyPreview(previewDirectory, originalName, timestamp) is { } preview)
            {
                CopyNewFile(preview, previewPath);
                totals.Previews++;
            }
        }
    }

    private static void ImportAuxiliaryFile(string file, string name, Totals totals)
    {
        string extension = Path.GetExtension(name);
        string? destination =
            name.Equals("themes.json", StringComparison.OrdinalIgnoreCase) ? AppPaths.GetThemeFile(name)
            : name.Contains("palettes", StringComparison.OrdinalIgnoreCase) && extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
                ? AppPaths.GetPaletteFile(name)
            : extension.Equals(".json", StringComparison.OrdinalIgnoreCase) || extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)
                ? AppPaths.GetSettingsFile(name)
            : null;
        if (destination is null || File.Exists(destination)) return;
        CopyNewFile(file, destination);
        totals.Files++;
    }

    /// <summary>Старое имя превью: безопасное имя сохранения + метка времени сохранения.</summary>
    private static string? FindLegacyPreview(string directory, string saveName, DateTime? timestamp)
    {
        if (!Directory.Exists(directory)) return null;
        string stem = LegacySafeFileName(saveName);
        if (timestamp is { } value)
        {
            string exact = Path.Combine(directory,
                $"{stem}_{value.ToString(LegacyTimestampFormat, CultureInfo.InvariantCulture)}.png");
            if (File.Exists(exact)) return exact;
        }

        // Метка могла сместиться со сменой часового пояса — берём единственное превью с этим именем.
        string[] candidates = Directory.EnumerateFiles(directory, "*.png")
            .Where(path =>
            {
                string fileName = Path.GetFileNameWithoutExtension(path);
                Match match = LegacyTimestampSuffix.Match(fileName);
                return match.Success && fileName[..match.Index].Equals(stem, StringComparison.OrdinalIgnoreCase);
            })
            .Take(2)
            .ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }

    private static string LegacySafeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Save";
        char[] invalid = Path.GetInvalidFileNameChars();
        string safe = new(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "Save" : safe.Trim();
    }

    private static string? ReadString(JsonObject save, string property) =>
        save[property] is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    private static DateTime? ReadTimestamp(JsonObject save)
    {
        try
        {
            // Тот же разбор, что и у модели: время с поясом приводится к локальному, как при записи превью.
            return save["Timestamp"]?.Deserialize<DateTime>();
        }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException)
        {
            return null;
        }
    }

    private static void WriteNewFile(string path, string contents)
    {
        string temporaryPath = $"{AppPaths.EnsureDirectoryFor(path)}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, contents);
            File.Move(temporaryPath, path, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static void CopyNewFile(string sourcePath, string destination)
    {
        string temporaryPath = $"{AppPaths.EnsureDirectoryFor(destination)}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.Copy(sourcePath, temporaryPath);
            File.Move(temporaryPath, destination, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static bool Overlaps(string first, string second)
    {
        static string Normalize(string path) =>
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) + Path.DirectorySeparatorChar;
        string a = Normalize(first), b = Normalize(second);
        return a.StartsWith(b, StringComparison.OrdinalIgnoreCase) || b.StartsWith(a, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ExistingSaves
    {
        public HashSet<string> Names { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> PathsByTimestamp { get; } = new(StringComparer.Ordinal);

        public static ExistingSaves Read(string directory)
        {
            var existing = new ExistingSaves();
            foreach (string path in Directory.EnumerateFiles(directory, "*.json"))
            {
                try
                {
                    if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject save) continue;
                    if (ReadString(save, "SaveName") is { } name) existing.Names.Add(name);
                    if (save["Timestamp"]?.ToJsonString() is { } timestamp) existing.PathsByTimestamp.TryAdd(timestamp, path);
                }
                catch (Exception exception) when (exception is JsonException or IOException)
                {
                    // Нечитаемый файл ни имени, ни метки не занимает.
                }
            }
            return existing;
        }
    }

    private sealed class Totals
    {
        public int Saves;
        public int Previews;
        public int Files;
        public List<string> Failures { get; } = [];
    }
}
