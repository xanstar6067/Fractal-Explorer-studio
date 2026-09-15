using System.IO;

namespace FractalExplorerWPF.Infrastructure;

public static class RenderPatternPreferenceStore
{
    private const string FileName = "render_pattern.txt";

    public static int Load()
    {
        try
        {
            string path = AppPaths.GetSettingsFile(FileName);
            return File.Exists(path) && int.TryParse(File.ReadAllText(path), out int value)
                ? Math.Clamp(value, 0, 7)
                : 0;
        }
        catch { return 0; }
    }

    public static void Save(int selectedIndex)
    {
        try
        {
            string path = AppPaths.EnsureDirectoryFor(AppPaths.GetSettingsFile(FileName));
            File.WriteAllText(path, Math.Clamp(selectedIndex, 0, 7).ToString());
        }
        catch
        {
            // A read-only installation must not prevent changing the strategy for this session.
        }
    }
}
