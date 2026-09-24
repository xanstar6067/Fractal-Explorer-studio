using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure;

public static class TerrainHeightMapExport
{
    /// <summary>Linear Gray16: black = 0, white = configured Height. No lighting or gamma.</summary>
    public static BitmapSource Create(TerrainSettings settings, CancellationToken token)
    {
        float[] heights = TerrainHeightField.Build(settings, token);
        var pixels = new ushort[heights.Length];
        for (int i = 0; i < pixels.Length; i++)
        {
            if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
            pixels[i] = (ushort)Math.Clamp(Math.Round(heights[i] / settings.Height * 65535), 0, 65535);
        }
        var bitmap = BitmapSource.Create(settings.Resolution, settings.Resolution, 96, 96,
            PixelFormats.Gray16, null, pixels, settings.Resolution * 2);
        bitmap.Freeze();
        return bitmap;
    }

    public static void Save(string path, BitmapSource bitmap)
    {
        // Encode completely before replacing a destination, so a failed encode preserves it.
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var memory = new MemoryStream();
        encoder.Save(memory);
        File.WriteAllBytes(path, memory.ToArray());
    }
}
