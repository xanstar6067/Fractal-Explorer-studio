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

    public static List<string> Load()
    {
        try
        {
            string path = AppPaths.GetSettingsFile(FileName);
            return File.Exists(path) ? Normalize(File.ReadAllLines(path)) : [];
        }
        catch { return []; }
    }

    public static void Save(IEnumerable<string> recent)
    {
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
