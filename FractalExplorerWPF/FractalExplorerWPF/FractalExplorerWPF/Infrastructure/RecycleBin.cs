using System.IO;
using Microsoft.VisualBasic.FileIO;

namespace FractalExplorerWPF.Infrastructure;

/// <summary>
/// Правило приложения: удаляемые и заменяемые сохранения и превью не стираются бесследно,
/// а уходят в Корзину Windows, откуда их можно восстановить на прежнее место.
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
    /// Атомарно ставит готовый временный файл на место <paramref name="path"/>: прежний файл,
    /// если он был, сначала уходит в Корзину. Временный файл при любом исходе не остаётся.
    /// </summary>
    public static void ReplaceWith(string temporaryPath, string path)
    {
        try
        {
            Send(path);
            File.Move(temporaryPath, path, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
