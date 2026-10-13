using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure;

public static class Fractal3DPreferencesStore
{
    public const string FileName = "fractal3d-preferences.json";
    private static readonly object Gate = new();

    public static Fractal3DPreferences Load()
    {
        lock (Gate)
        {
            string path = AppPaths.GetSettingsFile(FileName);
            try
            {
                return File.Exists(path)
                    ? JsonSerializer.Deserialize<Fractal3DPreferences>(File.ReadAllText(path)) ?? new()
                    : new();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                CrashLogger.Log("Чтение общих настроек 3D", exception);
                return new();
            }
        }
    }

    /// <summary>Меняет только выбранное поле: соседнее открытое окно не перезапишет остальные настройки.</summary>
    public static void Update(Action<Fractal3DPreferences> update)
    {
        lock (Gate)
        {
            Fractal3DPreferences preferences = Load();
            update(preferences);
            string path = AppPaths.GetSettingsFile(FileName);
            string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };
                File.WriteAllText(AppPaths.EnsureDirectoryFor(temporaryPath), JsonSerializer.Serialize(preferences, options));
                File.Move(temporaryPath, path, overwrite: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                CrashLogger.Log("Запись общих настроек 3D", exception);
            }
            finally
            {
                try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    CrashLogger.Log("Удаление временного файла настроек 3D", exception);
                }
            }
        }
    }
}
