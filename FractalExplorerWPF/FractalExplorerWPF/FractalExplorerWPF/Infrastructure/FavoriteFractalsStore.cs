using System.IO;

namespace FractalExplorerWPF.Infrastructure;

public static class FavoriteFractalsStore
{
    private const string FileName = "favorite_fractals.txt";

    public static HashSet<string> Load()
    {
        try
        {
            string path = AppPaths.GetSettingsFile(FileName);
            return File.Exists(path)
                ? new HashSet<string>(
                    File.ReadAllLines(path).Where(line => !string.IsNullOrWhiteSpace(line)),
                    StringComparer.OrdinalIgnoreCase)
                : [];
        }
        catch { return []; }
    }

    public static void Save(IEnumerable<string> favorites)
    {
        try
        {
            string path = AppPaths.EnsureDirectoryFor(AppPaths.GetSettingsFile(FileName));
            File.WriteAllLines(path, favorites);
        }
        catch
        {
            // A read-only installation must not prevent toggling favorites for this session.
        }
    }
}
