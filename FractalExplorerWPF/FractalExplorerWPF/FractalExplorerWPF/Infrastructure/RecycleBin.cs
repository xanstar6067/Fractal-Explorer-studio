using System.IO;
using Microsoft.VisualBasic.FileIO;

namespace FractalExplorerWPF.Infrastructure;

/// <summary>
/// Правило приложения: удаляемые и заменяемые сохранения и превью не стираются бесследно,
/// а уходят в Корзину Windows. Заменённая версия хранится там под именем резервного файла.
/// </summary>
public static class RecycleBin
{
    /// <summary>
    /// Шов для проверок: вместо Корзины получает полные пути существующих файлов. Проверочный
    /// проект перенаправляет их во временный каталог, чтобы не засорять настоящую Корзину.
    /// </summary>
    internal static Action<string>? SendOverrideForTests { get; set; }

    /// <summary>
    /// Отправляет существующие файлы в Корзину; отсутствующие пропускает. Если файл отправить
    /// не удалось, бросает исключение — вызывающий код не должен тогда перезаписывать файл.
    /// </summary>
    public static void Send(params string?[] paths)
    {
        foreach (string? path in paths)
        {
            if (string.IsNullOrEmpty(path)) continue;
            string fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath)) continue;

            if (SendOverrideForTests is { } send) send(fullPath);
            else FileSystem.DeleteFile(fullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        }
    }

    /// <summary>Как <see cref="Send"/>, но сбой только журналируется.</summary>
    public static bool TrySend(params string?[] paths)
    {
        try
        {
            Send(paths);
            return true;
        }
        catch (Exception exception)
        {
            CrashLogger.Log("RecycleBin.TrySend", exception);
            return false;
        }
    }

    /// <summary>
    /// Атомарно заменяет файл, оставляя прежнюю версию рядом в .bak до отправки в Корзину.
    /// При отказе Корзины возвращает прежний файл. Если откат тоже не удался, .bak сохраняется
    /// для восстановления, а исключение и журнал содержат его путь.
    /// </summary>
    public static void ReplaceWith(string temporaryPath, string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                File.Move(temporaryPath, path, overwrite: false);
                return;
            }

            string backupPath = $"{path}.{Guid.NewGuid():N}.bak";
            File.Replace(temporaryPath, path, backupPath);
            try { Send(backupPath); }
            catch (Exception recycleError)
            {
                try
                {
                    if (File.Exists(path)) File.Replace(backupPath, path, temporaryPath);
                    else File.Move(backupPath, path, overwrite: false);
                }
                catch (Exception rollbackError)
                {
                    var failure = new IOException(
                        $"Не удалось отправить прежнюю версию в Корзину и вернуть её на место. " +
                        $"Резервный файл для восстановления: {backupPath}",
                        new AggregateException(recycleError, rollbackError));
                    CrashLogger.Log("RecycleBin.ReplaceWith rollback", failure);
                    throw failure;
                }
                throw;
            }
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
