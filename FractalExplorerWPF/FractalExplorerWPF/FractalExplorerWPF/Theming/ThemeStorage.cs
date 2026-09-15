using System.IO;
using System.Text.Json;
using FractalExplorerWPF.Infrastructure;

namespace FractalExplorerWPF.Theming;

internal static class ThemeStorage
{
    private static string ThemesPath => AppPaths.EnsureDirectoryFor(AppPaths.GetThemeFile("themes.json"));
    private static string PreferencesPath => AppPaths.EnsureDirectoryFor(AppPaths.GetSettingsFile("theme-preferences.json"));

    public static IReadOnlyList<ThemeDefinition> LoadCustomThemes()
    {
        if (!File.Exists(ThemesPath))
            return [];

        string json = File.ReadAllText(ThemesPath);
        return (JsonSerializer.Deserialize<List<ThemeDefinition>>(json, JsonOptionsFactory.Create()) ?? [])
            .Where(theme => !theme.IsBuiltIn && !string.IsNullOrWhiteSpace(theme.Id))
            .Select(theme => theme.CloneWith(theme.Id, theme.DisplayName, false))
            .ToList();
    }

    public static void SaveCustomThemes(IEnumerable<ThemeDefinition> themes)
    {
        List<ThemeDefinition> customThemes = themes
            .Where(theme => !theme.IsBuiltIn)
            .Select(theme => theme.CloneWith(theme.Id, theme.DisplayName, false))
            .OrderBy(theme => theme.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        File.WriteAllText(ThemesPath, JsonSerializer.Serialize(customThemes, JsonOptionsFactory.Create()));
    }

    public static string? LoadSelectedThemeId()
    {
        if (!File.Exists(PreferencesPath))
            return null;

        ThemePreferences? preferences = JsonSerializer.Deserialize<ThemePreferences>(
            File.ReadAllText(PreferencesPath), JsonOptionsFactory.Create());
        return preferences?.SelectedThemeId;
    }

    public static void SaveSelectedThemeId(string themeId)
    {
        var preferences = new ThemePreferences { SelectedThemeId = themeId };
        File.WriteAllText(PreferencesPath, JsonSerializer.Serialize(preferences, JsonOptionsFactory.Create()));
    }

    private sealed class ThemePreferences
    {
        public string SelectedThemeId { get; set; } = ThemeManager.DefaultThemeId;
    }
}
