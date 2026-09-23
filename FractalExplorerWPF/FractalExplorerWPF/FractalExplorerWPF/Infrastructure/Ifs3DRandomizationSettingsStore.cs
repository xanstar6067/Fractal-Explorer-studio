using System.IO;
using System.Text.Json;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure;

public static class Ifs3DRandomizationSettingsStore
{
    private const string FileName = "ifs3d_randomizer_settings.json";

    public static Ifs3DRandomizationSettings Load()
    {
        string path = AppPaths.GetSettingsFile(FileName);
        if (!File.Exists(path)) return new Ifs3DRandomizationSettings();
        try
        {
            return (JsonSerializer.Deserialize<Ifs3DRandomizationSettings>(
                File.ReadAllText(path), JsonOptionsFactory.Create()) ?? new Ifs3DRandomizationSettings()).Normalize();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new Ifs3DRandomizationSettings();
        }
    }

    public static void Save(Ifs3DRandomizationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string path = AppPaths.EnsureDirectoryFor(AppPaths.GetSettingsFile(FileName));
        string temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath,
            JsonSerializer.Serialize(settings.Clone().Normalize(), JsonOptionsFactory.Create()));
        File.Move(temporaryPath, path, true);
    }
}
