using System.IO;

namespace FractalExplorerWPF.Infrastructure;

/// <summary>
/// Недавно запущенные режимы каталога — по названию, последний запуск первым
/// (<c>Settings\recent_fractals.txt</c>).
/// </summary>
public static class RecentFractalsStore
{
    public const int Capacity = 8;
    private const string FileName = "recent_fractals.txt";

    // Последнее чтение не удалось (файл занят, нет доступа): список не перезаписывается, иначе
    // первый же запуск режима стёр бы всю историю. Главное окно читает его один раз.
    private static bool _loadFailed;

    public static List<string> Load()
    {
        try
        {
            string path = AppPaths.GetSettingsFile(FileName);
            List<string> recent = File.Exists(path) ? Normalize(File.ReadAllLines(path)) : [];
            _loadFailed = false;
            return recent;
        }
        catch (Exception exception)
        {
            _loadFailed = true;
            CrashLogger.Log($"Загрузка недавних режимов: {AppPaths.GetSettingsFile(FileName)}", exception);
            return [];
        }
    }

    public static void Save(IEnumerable<string> recent)
    {
        if (_loadFailed) return;
        try
        {
            string path = AppPaths.EnsureDirectoryFor(AppPaths.GetSettingsFile(FileName));
            File.WriteAllLines(path, Normalize(recent));
        }
        catch
        {
            // A read-only installation must not prevent launching fractals.
        }
    }

    /// <summary>Переносит название в начало списка без повторов и обрезает список до <see cref="Capacity"/>.</summary>
    public static List<string> Push(IEnumerable<string> recent, string displayName) =>
        Normalize(recent.Prepend(displayName));

    private static List<string> Normalize(IEnumerable<string> names)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return names
            .Select(name => name.Trim())
            .Where(name => name.Length > 0 && seen.Add(name))
            .Take(Capacity)
            .ToList();
    }
}
