using System.IO;
using System.Text.Json;
using System.Windows;

namespace FractalExplorerWPF.Infrastructure;

/// <summary>
/// Файл пользовательских палитр режима в <c>Palettes\</c>. Если файл есть, но не прочитался
/// (повреждён, занят, нет доступа), ошибка пишется в журнал, а запись блокируется до
/// перезапуска: иначе первое же сохранение перезаписало бы его списком без пользовательских
/// палитр. Запись атомарная — временный файл рядом и замена.
/// </summary>
public sealed class CustomPaletteFile(string fileName)
{
    private Exception? _loadError;

    public string FilePath => AppPaths.GetPaletteFile(fileName);

    public bool CanSave => _loadError is null;

    /// <summary>Причина блокировки записи для окна палитр; <c>null</c>, если загрузка удалась.</summary>
    public string? LoadWarning { get; private set; }

    /// <summary>JSON-список палитр; <c>null</c> вместо списка — тоже повреждённый файл.</summary>
    public static List<T> ParseList<T>(string json) =>
        JsonSerializer.Deserialize<List<T>>(json, JsonOptionsFactory.Create())
        ?? throw new JsonException("Ожидался список палитр, получен null.");

    /// <summary>
    /// Палитры из файла; нет файла — пустой список. <paramref name="parse"/> разбирает весь
    /// список целиком и бросает исключение на любой ошибке: частично прочитанная библиотека
    /// не возвращается. После ошибки запись остаётся заблокированной, даже если следующее
    /// чтение удастся: окно могло уже взять неполный список.
    /// </summary>
    public List<T> Load<T>(Func<string, IEnumerable<T>> parse)
    {
        try
        {
            string json;
            try { json = File.ReadAllText(FilePath); }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                return [];
            }
            return parse(json).ToList();
        }
        catch (Exception exception)
        {
            _loadError ??= exception;
            LoadWarning ??= $"Не удалось загрузить пользовательские палитры: {FilePath}\n" +
                            "Запись палитр заблокирована до перезапуска, исходный файл не изменён: " +
                            "изменения палитр в этом сеансе не сохранятся. " +
                            "Восстановите файл и перезапустите приложение.\n" + exception.Message;
            CrashLogger.Log($"Загрузка пользовательских палитр: {FilePath}", exception);
            return [];
        }
    }

    /// <summary>Атомарно записывает список; после неудачной загрузки — исключение, файл не трогается.</summary>
    public void Save<T>(IEnumerable<T> palettes)
    {
        if (_loadError is not null)
            throw new InvalidOperationException(LoadWarning, _loadError);

        string path = AppPaths.EnsureDirectoryFor(FilePath);
        string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, palettes, JsonOptionsFactory.Create());
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(path)) File.Replace(temporaryPath, path, null);
            else File.Move(temporaryPath, path);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    /// <summary>
    /// Сохраняет, только если загрузка удалась; иначе ничего не пишет. Для окон, которые при
    /// открытии уже показали <see cref="LoadWarning"/> (<see cref="ShowLoadWarning"/>).
    /// </summary>
    public void SaveIfLoaded<T>(IEnumerable<T> palettes)
    {
        if (CanSave) Save(palettes);
    }

    /// <summary>Окно палитр сообщает о блокировке записи при первом показе.</summary>
    public static void ShowLoadWarning(Window window, string? warning)
    {
        if (warning is null) return;
        void Show(object sender, RoutedEventArgs e)
        {
            window.Loaded -= Show;
            MessageBox.Show(window, warning, "Палитры не загружены", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        window.Loaded += Show;
    }
}
