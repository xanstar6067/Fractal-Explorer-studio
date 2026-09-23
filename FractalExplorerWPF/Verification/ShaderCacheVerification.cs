using System.Diagnostics;
using System.IO;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;

internal static partial class Program
{
    private static async Task VerifyShaderCacheAsync()
    {
        using var sandbox = DataSandbox.Create("shadercache");
        var entry = new ShaderCacheEntry("verification-pixel", "source A", "PSMain", "ps_5_0");
        int compilations = 0;
        ReadOnlyMemory<byte> FakeCompile(ShaderCacheEntry _) => new byte[] { 1, 2, (byte)++compilations };

        byte[] first = ShaderBytecodeCache.GetOrCompile(entry, FakeCompile).ToArray();
        byte[] fromDisk = ShaderBytecodeCache.GetOrCompile(entry, FakeCompile).ToArray();
        Check(compilations == 1 && first.SequenceEqual(fromDisk), "Shader bytecode must be reused from disk.");

        ShaderCacheEntry changed = entry with { Source = "source B" };
        ShaderBytecodeCache.GetOrCompile(changed, FakeCompile);
        Check(compilations == 2, "A source change must invalidate shader bytecode.");

        string cacheFile = AppPaths.GetShaderCacheFile(entry.Key);
        File.WriteAllBytes(cacheFile, new byte[] { 0, 1, 2 });
        ShaderBytecodeCache.GetOrCompile(entry, FakeCompile);
        Check(compilations == 3, "A damaged cache file must be regenerated.");

        ShaderBytecodeCache.Rebuild([entry], FakeCompile);
        Check(compilations == 4 && Directory.GetFiles(AppPaths.ShaderCacheDirectory, "*.cso").Length == 1,
            "Rebuild must replace the stored shader set.");

        Fractal3DState state = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.ApollonianPacking);
        var watch = Stopwatch.StartNew();
        using (var renderer = new Fractal3DRenderer())
            await renderer.RenderPixelsAsync(state, 120, 90, null, null, CancellationToken.None);
        double coldMs = watch.Elapsed.TotalMilliseconds;
        Check(File.Exists(AppPaths.GetShaderCacheFile("fractal3d-vertex")) &&
              File.Exists(AppPaths.GetShaderCacheFile("fractal3d-ApollonianPacking-pixel")),
            "A real 3D render must persist its vertex and pixel shaders.");

        watch.Restart();
        using (var renderer = new Fractal3DRenderer())
            await renderer.RenderPixelsAsync(state, 120, 90, null, null, CancellationToken.None);
        double warmMs = watch.Elapsed.TotalMilliseconds;
        Console.WriteLine($"Shader cache: cold renderer {coldMs:F0} ms, new renderer from disk {warmMs:F0} ms.");

        Fractal3DRenderer.RebuildShaderCache();
        Check(Directory.GetFiles(AppPaths.ShaderCacheDirectory, "*.cso").Length ==
              Enum.GetValues<Fractal3DKind>().Length + 1 &&
              File.Exists(AppPaths.GetShaderCacheFile("fractal3d-Vicsek-pixel")) &&
              File.Exists(AppPaths.GetShaderCacheFile("fractal3d-CantorDust-pixel")),
            "Rebuild must create a shader for every 3D mode and the shared vertex shader.");
        Console.WriteLine("PASS (shadercache): reuse, invalidation, corruption recovery, real rendering and full rebuild.");
    }
}
