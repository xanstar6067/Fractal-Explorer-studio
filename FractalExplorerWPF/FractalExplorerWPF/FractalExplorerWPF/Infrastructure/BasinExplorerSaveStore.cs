using System.IO;
using System.Text.Json;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure;

/// <summary>Сохранения окна бассейнов — отдельный файл на каждый из пяти режимов.</summary>
public sealed class BasinExplorerSaveStore(BasinExplorerKind kind)
{
    private string FilePath => Path.Combine(AppPaths.SavesDirectory,
        $"{BasinExplorerCatalog.GetDefinition(kind).SaveFilePrefix}_saves.json");

    public List<BasinExplorerState> Load() => !File.Exists(FilePath)
        ? []
        : JsonSerializer.Deserialize<List<BasinExplorerState>>(File.ReadAllText(FilePath), JsonOptionsFactory.Create()) ?? [];

    public void Save(IEnumerable<BasinExplorerState> states)
    {
        AppPaths.EnsureSavesDirectory();
        string temporaryPath = FilePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(states, JsonOptionsFactory.Create()));
        File.Move(temporaryPath, FilePath, true);
    }
}
