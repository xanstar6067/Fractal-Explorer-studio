using System.IO;
using System.Text.Json;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure;

public static class IfsRandomizationSettingsStore
{
    private const string FileName = "ifs_randomizer_settings.json";

    public static IfsRandomizationSettings Load()
    {
        string path = AppPaths.GetSettingsFile(FileName);
        if (!File.Exists(path)) return new IfsRandomizationSettings();
        try
        {
            return (JsonSerializer.Deserialize<IfsRandomizationSettings>(
                File.ReadAllText(path), JsonOptionsFactory.Create()) ?? new IfsRandomizationSettings()).Normalize();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new IfsRandomizationSettings();
        }
    }

    public static void Save(IfsRandomizationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string path = AppPaths.EnsureDirectoryFor(AppPaths.GetSettingsFile(FileName));
        string temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath,
            JsonSerializer.Serialize(settings.Clone().Normalize(), JsonOptionsFactory.Create()));
        File.Move(temporaryPath, path, true);
    }
}
