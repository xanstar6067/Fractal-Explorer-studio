using System.IO;

namespace FractalExplorerWPF.Infrastructure;

public static class FavoriteFractalsStore
{
    private const string FileName = "favorite_fractals.txt";

    // Последнее чтение не удалось (файл занят, нет доступа): список не перезаписывается, иначе
    // первое же нажатие на звёздочку стёрло бы всё избранное. Главное окно читает его один раз.
    private static bool _loadFailed;

    public static HashSet<string> Load()
    {
        try
        {
            string path = AppPaths.GetSettingsFile(FileName);
            HashSet<string> favorites = File.Exists(path)
                ? new HashSet<string>(
                    File.ReadAllLines(path).Where(line => !string.IsNullOrWhiteSpace(line)),
                    StringComparer.OrdinalIgnoreCase)
                : [];
            _loadFailed = false;
            return favorites;
        }
        catch (Exception exception)
        {
            _loadFailed = true;
            CrashLogger.Log($"Загрузка избранного: {AppPaths.GetSettingsFile(FileName)}", exception);
            return [];
        }
    }

    public static void Save(IEnumerable<string> favorites)
    {
        if (_loadFailed) return;
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
