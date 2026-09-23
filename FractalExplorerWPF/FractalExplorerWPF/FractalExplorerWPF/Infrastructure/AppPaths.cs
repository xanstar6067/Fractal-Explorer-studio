using System.IO;

namespace FractalExplorerWPF.Infrastructure;

/// <summary>
/// Единое место, где лежат пользовательские данные приложения:
/// <c>%LOCALAPPDATA%\Fractal Explorer Studio</c>.
/// <code>
/// Saves\&lt;режим&gt;\&lt;имя&gt;.json + .png   — сохранения, по файлу на запись, превью рядом
/// PointsOfInterest\&lt;режим&gt;\&lt;имя&gt;.png  — превью встроенных точек интереса
/// Palettes\                             — пользовательские палитры
/// Themes\                               — пользовательские темы оформления
/// Settings\                             — настройки и предпочтения
/// Logs\                                 — журнал ошибок
/// shadercache\                          — скомпилированные шейдеры Direct3D
/// Backups\                              — резервные копии файлов, изменённых миграциями
/// data-version.json                     — достигнутая версия каталога данных
/// </code>
/// Раньше всё это писалось в каталог <c>Saves</c> рядом с exe; перенос и дальнейшие изменения
/// раскладки выполняет <see cref="Migrations.UserDataMigrator"/>.
/// </summary>
public static class AppPaths
{
    public const string ApplicationFolderName = "Fractal Explorer Studio";

    private static string? _dataRootOverride;

    public static string DataRoot => _dataRootOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify),
        ApplicationFolderName);

    public static string SavesRoot => Path.Combine(DataRoot, "Saves");
    public static string PointsOfInterestRoot => Path.Combine(DataRoot, "PointsOfInterest");
    public static string PalettesDirectory => Path.Combine(DataRoot, "Palettes");
    public static string ThemesDirectory => Path.Combine(DataRoot, "Themes");
    public static string SettingsDirectory => Path.Combine(DataRoot, "Settings");
    public static string LogsDirectory => Path.Combine(DataRoot, "Logs");
    public static string ShaderCacheDirectory => Path.Combine(DataRoot, "shadercache");

    /// <summary>Каталог сохранений прежних версий — рядом с exe.</summary>
    public static string LegacySavesDirectory => Path.Combine(AppContext.BaseDirectory, "Saves");

    /// <summary>
    /// Перенаправляет все данные в другой каталог. Только для инструментов и проверок,
    /// которым нельзя трогать настоящие данные пользователя; вызывать до первого обращения.
    /// </summary>
    public static void OverrideDataRoot(string? path) =>
        _dataRootOverride = string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);

    public static string GetSavesDirectory(string category) =>
        Path.Combine(SavesRoot, ToSafeFileName(category));

    public static string GetPointsOfInterestDirectory(string category) =>
        Path.Combine(PointsOfInterestRoot, ToSafeFileName(category));

    public static string GetPaletteFile(string fileName) => Path.Combine(PalettesDirectory, fileName);
    public static string GetThemeFile(string fileName) => Path.Combine(ThemesDirectory, fileName);
    public static string GetSettingsFile(string fileName) => Path.Combine(SettingsDirectory, fileName);
    public static string GetShaderCacheFile(string fileName) =>
        Path.Combine(ShaderCacheDirectory, ToSafeFileName(fileName) + ".cso");

    /// <summary>
    /// Свободный путь «Имя.json», «Имя (2).json», … — разные имена сохранений могут дать одно
    /// безопасное имя файла.
    /// </summary>
    public static string GetFreeSaveFilePath(string directory, string saveName)
    {
        string stem = ToSafeFileName(saveName);
        string path = Path.Combine(directory, stem + ".json");
        for (int index = 2; File.Exists(path); index++)
            path = Path.Combine(directory, $"{stem} ({index}).json");
        return path;
    }

    /// <summary>Создаёт каталог файла и возвращает тот же путь — для записи.</summary>
    public static string EnsureDirectoryFor(string filePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        return filePath;
    }

    public static string EnsureDataRoot()
    {
        Directory.CreateDirectory(DataRoot);
        return DataRoot;
    }

    public static string EnsureLogsDirectory()
    {
        Directory.CreateDirectory(LogsDirectory);
        return LogsDirectory;
    }

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>
    /// Имя, пригодное как имя файла Windows: недопустимые символы заменены на «_», без
    /// концевых точек и пробелов, не имя устройства и не длиннее 96 символов.
    /// </summary>
    public static string ToSafeFileName(string? value, string fallback = "Save")
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        char[] invalid = Path.GetInvalidFileNameChars();
        string safe = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray())
            .Trim().TrimEnd('.', ' ');
        if (safe.Length > 96) safe = safe[..96].TrimEnd('.', ' ');
        if (safe.Length == 0) return fallback;
        string stem = safe.Split('.')[0];
        return ReservedDeviceNames.Contains(stem) ? "_" + safe : safe;
    }
}
