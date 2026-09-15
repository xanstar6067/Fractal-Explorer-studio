using System.IO;
using System.Text.Json;

namespace FractalExplorerWPF.Infrastructure;

internal sealed class ImageExportSettings
{
    public int Width { get; set; }
    public int Height { get; set; }
    public ImageExportFormat Format { get; set; } = ImageExportFormat.Png;
    public ImageExportProcessingMode ProcessingMode { get; set; } = ImageExportProcessingMode.Ssaa;
    public int SsaaFactor { get; set; } = 1;
    public double BicubicFactor { get; set; } = 1.5;
    public double LanczosFactor { get; set; } = 2;
    public int JpegQuality { get; set; } = 95;
}

internal static class ImageExportSettingsStore
{
    private static string SettingsPath => AppPaths.GetSettingsFile("image-export-settings.json");

    public static ImageExportSettings Load(int fallbackWidth, int fallbackHeight)
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                ImageExportSettings? settings = JsonSerializer.Deserialize<ImageExportSettings>(
                    File.ReadAllText(SettingsPath), JsonOptionsFactory.Create());
                if (settings is not null)
                    return settings;
            }
        }
        catch
        {
            // Повреждённые пользовательские настройки не должны блокировать экспорт.
        }

        return new ImageExportSettings
        {
            Width = Math.Max(1, fallbackWidth),
            Height = Math.Max(1, fallbackHeight)
        };
    }

    public static void Save(ImageExportSettings settings)
    {
        try
        {
            File.WriteAllText(AppPaths.EnsureDirectoryFor(SettingsPath), JsonSerializer.Serialize(settings, JsonOptionsFactory.Create()));
        }
        catch
        {
            // Экспорт остаётся доступным даже в каталоге только для чтения.
        }
    }
}
