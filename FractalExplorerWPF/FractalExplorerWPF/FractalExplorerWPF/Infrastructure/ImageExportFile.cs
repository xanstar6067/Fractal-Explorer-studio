using System.IO;

namespace FractalExplorerWPF.Infrastructure;

internal static class ImageExportFile
{
    public static void Write(string path, Action<Stream> encode)
    {
        // Use the destination directory so publication is an atomic move on the same volume.
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                encode(stream);
                stream.Flush(flushToDisk: true);
            }
            RecycleBin.ReplaceWith(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
