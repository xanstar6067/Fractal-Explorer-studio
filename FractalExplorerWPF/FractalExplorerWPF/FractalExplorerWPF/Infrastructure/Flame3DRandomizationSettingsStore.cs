using System.IO;
using System.Text.Json;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure;

public static class Flame3DRandomizationSettingsStore
{
    private const string FileName = "flame3d_randomizer_settings.json";

    public static Flame3DRandomizationSettings Load()
    {
        string path = AppPaths.GetSettingsFile(FileName);
        if (!File.Exists(path))
            return new Flame3DRandomizationSettings();

        try
        {
            return (JsonSerializer.Deserialize<Flame3DRandomizationSettings>(
                File.ReadAllText(path), JsonOptionsFactory.Create()) ?? new Flame3DRandomizationSettings()).Normalize();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new Flame3DRandomizationSettings();
        }
    }

    public static void Save(Flame3DRandomizationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string path = AppPaths.EnsureDirectoryFor(AppPaths.GetSettingsFile(FileName));
        string temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath,
            JsonSerializer.Serialize(settings.Clone().Normalize(), JsonOptionsFactory.Create()));
        File.Move(temporaryPath, path, true);
    }
}
