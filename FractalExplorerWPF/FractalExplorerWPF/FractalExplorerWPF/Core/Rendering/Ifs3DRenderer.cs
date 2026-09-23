using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Core.Rendering;

/// <summary>Chaos-game orbit in R³, drawn as a depth-shaded orthographic point cloud.</summary>
public static class Ifs3DRenderer
{
    public static Task<BitmapSource> RenderBitmapAsync(
        Ifs3DState state, int width, int height, CancellationToken token, IProgress<int>? progress = null) =>
        Task.Run(() =>
        {
            byte[] pixels = RenderPixels(state, width, height, token, progress);
            var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
            bitmap.Freeze();
            return bitmap;
        }, token);

    public static byte[] RenderPixels(Ifs3DState state, int width, int height, CancellationToken token, IProgress<int>? progress = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (state.Transforms.Count == 0) throw new ArgumentException("Добавьте хотя бы одно преобразование.");
        int count = Math.Clamp(state.Iterations, 1, 10_000_000);
        var points = new Vector3[count];
        double[] weights = new double[state.Transforms.Count];
        double total = state.Transforms.Sum(t => Math.Max(0, t.Probability));
        if (!double.IsFinite(total) || total <= 0) throw new ArgumentException("Сумма вероятностей должна быть положительной.");
        double cumulative = 0;
        for (int i = 0; i < weights.Length; i++)
        {
            cumulative += Math.Max(0, state.Transforms[i].Probability) / total;
            weights[i] = cumulative;
        }
        weights[^1] = 1;

        var random = new Random(12345);
        double x = 0, y = 0, z = 0;
        for (int i = -100; i < count; i++)
        {
            if ((i & 8191) == 0) token.ThrowIfCancellationRequested();
            double draw = random.NextDouble();
            int selected = Array.BinarySearch(weights, draw);
            if (selected < 0) selected = ~selected;
            Ifs3DTransform t = state.Transforms[Math.Min(selected, weights.Length - 1)];
            double nextX = t.M11 * x + t.M12 * y + t.M13 * z + t.Tx;
            double nextY = t.M21 * x + t.M22 * y + t.M23 * z + t.Ty;
            double nextZ = t.M31 * x + t.M32 * y + t.M33 * z + t.Tz;
            x = double.IsFinite(nextX) && Math.Abs(nextX) < 1e15 ? nextX : 0;
            y = double.IsFinite(nextY) && Math.Abs(nextY) < 1e15 ? nextY : 0;
            z = double.IsFinite(nextZ) && Math.Abs(nextZ) < 1e15 ? nextZ : 0;
            if (i >= 0) points[i] = new Vector3((float)x, (float)y, (float)z);
            if (i > 0 && (i & 65535) == 0) progress?.Report((int)(i * 45L / count));
        }

        double yaw = state.Yaw * Math.PI / 180, pitch = state.Pitch * Math.PI / 180;
        double cy = Math.Cos(yaw), sy = Math.Sin(yaw), cp = Math.Cos(pitch), sp = Math.Sin(pitch);
        double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
        double minY = double.PositiveInfinity, maxY = double.NegativeInfinity;
        double minZ = double.PositiveInfinity, maxZ = double.NegativeInfinity;
        foreach (Vector3 point in points)
        {
            double rx = cy * point.X + sy * point.Z;
            double rz = -sy * point.X + cy * point.Z;
            double ry = cp * point.Y - sp * rz;
            double depth = sp * point.Y + cp * rz;
            minX = Math.Min(minX, rx); maxX = Math.Max(maxX, rx);
            minY = Math.Min(minY, ry); maxY = Math.Max(maxY, ry);
            minZ = Math.Min(minZ, depth); maxZ = Math.Max(maxZ, depth);
        }

        // Automatic framing is based on the projected orbit, so one preset works at every window size.
        double extentX = Math.Max(maxX - minX, 1e-8), extentY = Math.Max(maxY - minY, 1e-8);
        double scale = Math.Min(width * .90 / extentX, height * .90 / extentY) * Math.Clamp(state.Zoom, .1, 20);
        double centerX = (minX + maxX) / 2, centerY = (minY + maxY) / 2;
        byte[] pixels = new byte[checked(width * height * 4)];
        float[] depthBuffer = new float[checked(width * height)];
        Array.Fill(depthBuffer, float.NegativeInfinity);
        Color background = state.BackgroundColor;
        for (int p = 0; p < pixels.Length; p += 4)
        {
            pixels[p] = background.B; pixels[p + 1] = background.G;
            pixels[p + 2] = background.R; pixels[p + 3] = 255;
        }

        Color foreground = state.PointColor;
        double depthRange = Math.Max(maxZ - minZ, 1e-8);
        for (int i = 0; i < count; i++)
        {
            if ((i & 8191) == 0) token.ThrowIfCancellationRequested();
            Vector3 point = points[i];
            double rx = cy * point.X + sy * point.Z;
            double rz = -sy * point.X + cy * point.Z;
            double ry = cp * point.Y - sp * rz;
            double depth = sp * point.Y + cp * rz;
            int px = (int)((rx - centerX) * scale + width * (.5 + state.PanX));
            int py = (int)((centerY - ry) * scale + height * (.5 + state.PanY));
            if ((uint)px >= (uint)width || (uint)py >= (uint)height) continue;
            int pixel = py * width + px;
            if (depth <= depthBuffer[pixel]) continue;
            depthBuffer[pixel] = (float)depth;
            double brightness = .3 + .7 * (depth - minZ) / depthRange;
            int offset = pixel * 4;
            pixels[offset] = Blend(background.B, foreground.B, brightness);
            pixels[offset + 1] = Blend(background.G, foreground.G, brightness);
            pixels[offset + 2] = Blend(background.R, foreground.R, brightness);
            if ((i & 65535) == 0) progress?.Report(45 + (int)(i * 55L / count));
        }
        progress?.Report(100);
        return pixels;
    }

    private static byte Blend(byte background, byte foreground, double amount) =>
        (byte)Math.Clamp((int)Math.Round(background + (foreground - background) * amount), 0, 255);
}
