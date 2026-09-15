using System.IO;
using System.Text.Json;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure;

public static class FlameRandomizationSettingsStore
{
    private const string FileName = "flame_randomizer_settings.json";

    public static FlameRandomizationSettings Load()
    {
        string path = AppPaths.GetSettingsFile(FileName);
        if (!File.Exists(path))
            return new FlameRandomizationSettings();

        try
        {
            return (JsonSerializer.Deserialize<FlameRandomizationSettings>(
                File.ReadAllText(path), JsonOptionsFactory.Create()) ?? new FlameRandomizationSettings()).Normalize();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new FlameRandomizationSettings();
        }
    }

    public static void Save(FlameRandomizationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string path = AppPaths.EnsureDirectoryFor(AppPaths.GetSettingsFile(FileName));
        string temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath,
            JsonSerializer.Serialize(settings.Clone().Normalize(), JsonOptionsFactory.Create()));
        File.Move(temporaryPath, path, true);
    }
}
