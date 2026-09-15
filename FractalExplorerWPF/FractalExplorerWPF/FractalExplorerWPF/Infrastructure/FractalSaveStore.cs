using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using FractalExplorerWPF.Infrastructure.Migrations;

namespace FractalExplorerWPF.Infrastructure;

/// <summary>Одно сохранение на диске: состояние, его JSON и PNG-превью с тем же именем.</summary>
public sealed record SaveSlot<TState>(TState State, string FilePath) where TState : class
{
    public string PreviewPath => Path.ChangeExtension(FilePath, ".png");
}

/// <summary>Прочитанные сохранения и файлы, которые прочитать не удалось (они не трогаются).</summary>
public sealed record SaveLoadResult<TState>(IReadOnlyList<SaveSlot<TState>> Slots, IReadOnlyList<string> DamagedFiles)
    where TState : class;

/// <summary>
/// Сохранения одного режима: каталог <c>Saves\&lt;категория&gt;</c>, в нём по файлу
/// <c>&lt;имя&gt;.json</c> на запись и необязательное превью <c>&lt;имя&gt;.png</c> рядом.
/// Удаление и перезапись отправляют прежние файлы в Корзину (<see cref="RecycleBin"/>).
/// Файл несёт версию формата (<see cref="SaveFormat"/>); устаревший формат доводится при чтении.
/// </summary>
public class FractalSaveStore<TState> where TState : class
{
    private readonly Func<TState, string> _getName;
    private readonly Func<TState, bool>? _prepareLoaded;

    /// <param name="category">Имя каталога режима; совпадает с префиксом старого <c>*_saves.json</c>.</param>
    /// <param name="getName">Имя сохранения — из него строится имя файла.</param>
    /// <param name="prepareLoaded">Доводит прочитанное состояние; <c>false</c> пропускает файл.</param>
    public FractalSaveStore(string category, Func<TState, string> getName, Func<TState, bool>? prepareLoaded = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        Category = category;
        _getName = getName;
        _prepareLoaded = prepareLoaded;
    }

    public string Category { get; }

    public string DirectoryPath => AppPaths.GetSavesDirectory(Category);

    public SaveLoadResult<TState> LoadSlots()
    {
        string directory = DirectoryPath;
        if (!Directory.Exists(directory)) return new([], []);

        var slots = new List<SaveSlot<TState>>();
        var damaged = new List<string>();
        JsonSerializerOptions options = JsonOptionsFactory.Create();
        foreach (string path in Directory.EnumerateFiles(directory, "*.json")
                     .Where(path => Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
                     .Order(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject save)
                {
                    damaged.Add(path);
                    continue;
                }
                SaveFormat.UpgradeInPlace(Category, save);
                TState? state = save.Deserialize<TState>(options);
                if (state is null) damaged.Add(path);
                else if (_prepareLoaded?.Invoke(state) ?? true) slots.Add(new SaveSlot<TState>(state, path));
            }
            catch (Exception exception)
            {
                // Один повреждённый файл не должен скрывать остальные сохранения. Сбой не
                // разбора JSON, а апгрейда или модели — скорее ошибка кода, её стоит видеть в журнале.
                if (exception is not (JsonException or IOException or UnauthorizedAccessException))
                    CrashLogger.Log($"FractalSaveStore.LoadSlots: {path}", exception);
                damaged.Add(path);
            }
        }
        return new(slots, damaged);
    }

    public List<TState> Load() => LoadSlots().Slots.Select(slot => slot.State).ToList();

    /// <summary>
    /// Записывает состояние в собственный файл. При перезаписи <paramref name="replacing"/>
    /// прежние JSON и превью уходят в Корзину; новое превью записывает вызывающий код.
    /// </summary>
    public SaveSlot<TState> Save(TState state, SaveSlot<TState>? replacing = null)
    {
        string directory = Directory.CreateDirectory(DirectoryPath).FullName;
        string path = replacing?.FilePath ?? AppPaths.GetFreeSaveFilePath(directory, _getName(state));
        string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, SaveFormat.Serialize(state, JsonOptionsFactory.Create()));
            RecycleBin.ReplaceWith(temporaryPath, path);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }

        var slot = new SaveSlot<TState>(state, path);
        if (replacing is not null) RecycleBin.TrySend(replacing.PreviewPath);
        return slot;
    }

    /// <summary>Отправляет сохранение и его превью в Корзину.</summary>
    public void Delete(SaveSlot<TState> slot) => RecycleBin.Send(slot.FilePath, slot.PreviewPath);

    public string GetPointOfInterestPreviewPath(string name) =>
        Path.Combine(AppPaths.GetPointsOfInterestDirectory(Category), AppPaths.ToSafeFileName(name) + ".png");
}
