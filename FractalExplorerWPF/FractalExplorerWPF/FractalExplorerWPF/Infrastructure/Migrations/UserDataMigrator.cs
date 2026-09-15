using System.IO;
using System.Text.Json;

namespace FractalExplorerWPF.Infrastructure.Migrations;

/// <summary>
/// Шаг миграции каталога данных: переводит его из версии <c>Version − 1</c> в <see cref="Version"/>.
/// <para>
/// Шаг обязан быть повторяемым: если приложение закроется посреди шага, при следующем запуске
/// он выполнится снова. Существующие файлы перед изменением сохраняются через
/// <see cref="UserDataMigrationContext.BackUp"/> или <see cref="UserDataMigrationContext.RewriteFile"/>,
/// а удаляемые — отправляются в Корзину (<see cref="RecycleBin"/>).
/// </para>
/// </summary>
public interface IUserDataMigration
{
    int Version { get; }
    string Description { get; }
    void Apply(UserDataMigrationContext context);
}

public sealed class UserDataMigrationContext(string dataRoot, string backupDirectory)
{
    public string DataRoot { get; } = dataRoot;

    /// <summary>Каталог резервной копии этого шага; создаётся при первой копии.</summary>
    public string BackupDirectory { get; } = backupDirectory;

    /// <summary>Сообщения для пользователя: что перенесено, что не удалось.</summary>
    public List<string> Notes { get; } = [];

    /// <summary>Копирует файл из каталога данных в резервную копию шага с тем же относительным путём.</summary>
    public void BackUp(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) return;
        string relative = Path.GetRelativePath(DataRoot, fullPath);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new InvalidOperationException($"Резервная копия возможна только для файлов каталога данных: {fullPath}");
        string destination = AppPaths.EnsureDirectoryFor(Path.Combine(BackupDirectory, relative));
        if (!File.Exists(destination)) File.Copy(fullPath, destination);
    }

    /// <summary>Атомарно переписывает файл, предварительно сохранив прежнее содержимое в резервную копию.</summary>
    public void RewriteFile(string path, string contents)
    {
        BackUp(path);
        string temporaryPath = $"{AppPaths.EnsureDirectoryFor(path)}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, contents);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}

public sealed record UserDataMigrationResult(
    int StartVersion,
    int FinalVersion,
    IReadOnlyList<string> Applied,
    IReadOnlyList<string> Notes,
    string? Error)
{
    public bool HasMessageForUser => Notes.Count > 0 || Error is not null;
}

/// <summary>
/// Выполняет недостающие шаги миграции каталога данных при запуске приложения. Достигнутая
/// версия хранится в <c>data-version.json</c> и записывается после каждого успешного шага,
/// поэтому сбой одного шага не откатывает предыдущие и не мешает работе приложения.
/// <para>
/// Как добавить миграцию: реализовать <see cref="IUserDataMigration"/> с версией на единицу
/// больше последней и добавить в конец <see cref="BuiltInMigrations"/>. Изменения формата
/// отдельного сохранения удобнее описывать через <see cref="SaveFormat"/>.
/// </para>
/// </summary>
public static class UserDataMigrator
{
    public const string VersionFileName = "data-version.json";

    private static readonly IReadOnlyList<IUserDataMigration> BuiltInMigrations =
    [
        new Migration001ImportLegacyExeFolder()
    ];

    /// <summary>Версия каталога данных, которую создаёт эта сборка.</summary>
    public static int CurrentVersion => BuiltInMigrations[^1].Version;

    public static string VersionFilePath => Path.Combine(AppPaths.DataRoot, VersionFileName);

    public static UserDataMigrationResult Run() => Run(BuiltInMigrations);

    internal static UserDataMigrationResult Run(IReadOnlyList<IUserDataMigration> migrations)
    {
        for (int index = 0; index < migrations.Count; index++)
        {
            if (migrations[index].Version != index + 1)
                throw new InvalidOperationException(
                    $"Миграции должны идти подряд с версии 1: «{migrations[index].Description}» имеет версию {migrations[index].Version}.");
        }

        var applied = new List<string>();
        var notes = new List<string>();
        int startVersion = 0;
        int version = 0;
        try
        {
            string root = AppPaths.EnsureDataRoot();
            DataVersionFile versionFile = DataVersionFile.Load(VersionFilePath);
            startVersion = version = versionFile.Version;
            if (version > migrations.Count)
            {
                notes.Add($"Данные в «{root}» созданы более новой версией программы (формат {version}, " +
                          $"эта версия понимает до {migrations.Count}). Миграции не выполнялись.");
                return new(startVersion, version, applied, notes, null);
            }

            foreach (IUserDataMigration migration in migrations.Skip(version))
            {
                string backup = Path.Combine(root, "Backups",
                    $"{DateTime.Now:yyyy-MM-dd HH-mm-ss} v{version}-v{migration.Version}");
                var context = new UserDataMigrationContext(root, backup);
                try
                {
                    migration.Apply(context);
                }
                catch (Exception exception)
                {
                    CrashLogger.Log($"UserDataMigrator: {migration.Description}", exception);
                    notes.AddRange(context.Notes);
                    return new(startVersion, version, applied, notes,
                        $"Миграция «{migration.Description}» не выполнена: {exception.Message}");
                }

                notes.AddRange(context.Notes);
                version = migration.Version;
                versionFile.Version = version;
                versionFile.History.Add(new DataVersionEntry(version, migration.Description, DateTime.Now));
                versionFile.Save(VersionFilePath);
                applied.Add(migration.Description);
            }

            return new(startVersion, version, applied, notes, null);
        }
        catch (Exception exception)
        {
            CrashLogger.Log("UserDataMigrator", exception);
            return new(startVersion, version, applied, notes, $"Не удалось проверить версию данных: {exception.Message}");
        }
    }

    private sealed record DataVersionEntry(int Version, string Description, DateTime AppliedAt);

    private sealed class DataVersionFile
    {
        public int Version { get; set; }
        public List<DataVersionEntry> History { get; set; } = [];

        public static DataVersionFile Load(string path)
        {
            if (!File.Exists(path)) return new DataVersionFile();
            // Повреждённый файл версии — не повод молча начинать миграции заново: это ошибка.
            return JsonSerializer.Deserialize<DataVersionFile>(File.ReadAllText(path))
                   ?? throw new InvalidDataException($"Файл версии данных пуст: {path}");
        }

        public void Save(string path)
        {
            string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temporaryPath, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }
    }
}
