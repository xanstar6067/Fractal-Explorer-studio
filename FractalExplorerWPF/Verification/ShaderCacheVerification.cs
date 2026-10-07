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

        VerifyParallelShaderRebuild();

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

        watch.Restart();
        Fractal3DRenderer.RebuildShaderCache();
        Console.WriteLine($"Parallel shader rebuild: {watch.Elapsed.TotalSeconds:F1} s.");
        Check(Directory.GetFiles(AppPaths.ShaderCacheDirectory, "*.cso").Length ==
              Enum.GetValues<Fractal3DKind>().Length + TuringComputeShader.EntryPoints.Length + GrayScottComputeShader.CacheEntries.Count + GrayScott3DComputeShader.CacheEntries.Count +
              Turing3DComputeShader.CacheEntries.Count + CahnHilliard3DComputeShader.CacheEntries.Count + 2 &&
              File.Exists(AppPaths.GetShaderCacheFile("ifs3d-pixel")) &&
              File.Exists(AppPaths.GetShaderCacheFile("flame3d-pixel")) &&
              File.Exists(AppPaths.GetShaderCacheFile("buddhabrot4d-pixel")) &&
              File.Exists(AppPaths.GetShaderCacheFile("turing-Render")) &&
              File.Exists(AppPaths.GetShaderCacheFile("gray-scott-Render")) &&
              File.Exists(AppPaths.GetShaderCacheFile("gray-scott3d-pixel")) &&
              File.Exists(AppPaths.GetShaderCacheFile("turing3d-pixel")) &&
              File.Exists(AppPaths.GetShaderCacheFile("turing3d-Blur")) &&
              File.Exists(AppPaths.GetShaderCacheFile("dla3d-pixel")) &&
              File.Exists(AppPaths.GetShaderCacheFile("fractal3d-LSystem3D-pixel")) &&
              File.Exists(AppPaths.GetShaderCacheFile("fractal3d-Hopf-pixel")) &&
              File.Exists(AppPaths.GetShaderCacheFile("fractal3d-Vicsek-pixel")) &&
              File.Exists(AppPaths.GetShaderCacheFile("fractal3d-CantorDust-pixel")),
            "Rebuild must create a shader for every 3D mode, sharing IFS/attractor density and including colored Flame.");
        Console.WriteLine("PASS (shadercache): reuse, invalidation, corruption recovery, bounded parallel rebuild, failure preservation and real rendering.");
    }

    private static void VerifyParallelShaderRebuild()
    {
        ShaderCacheEntry[] entries = Enumerable.Range(0, 12)
            .Select(i => new ShaderCacheEntry($"parallel-{i}", ((char)('A' + i)).ToString(), "PSMain", "ps_5_0"))
            .ToArray();
        var original = Directory.GetFiles(AppPaths.ShaderCacheDirectory, "*.cso")
            .ToDictionary(path => path, File.ReadAllBytes);
        int parallelism = Math.Max(1, Math.Min(entries.Length, Environment.ProcessorCount - 1));
        // На многоядерном ЦП требуем больше четырёх одновременных компиляций,
        // чтобы прежний фиксированный лимит не прошёл эту проверку.
        int requiredOverlap = Math.Min(5, parallelism);
        using var overlap = new CountdownEvent(requiredOverlap);
        int started = 0, active = 0, peak = 0;
        var compilerSync = new object();
        var reports = new List<(int Completed, int Total, string Key)>();

        ReadOnlyMemory<byte> Compile(ShaderCacheEntry entry)
        {
            lock (compilerSync)
                peak = Math.Max(peak, ++active);
            try
            {
                if (Interlocked.Increment(ref started) <= requiredOverlap)
                {
                    overlap.Signal();
                    Check(overlap.Wait(TimeSpan.FromSeconds(30)), "Independent shaders must compile concurrently and scale beyond four workers on larger CPUs.");
                }
                return new byte[] { (byte)entry.Source[0], 42 };
            }
            finally
            {
                lock (compilerSync) active--;
            }
        }

        ShaderBytecodeCache.Rebuild(entries, Compile, (completed, total, key) =>
        {
            reports.Add((completed, total, key));
            Check(original.All(file => File.ReadAllBytes(file.Key).SequenceEqual(file.Value)) &&
                  Directory.GetFiles(AppPaths.ShaderCacheDirectory, "*.cso").Length == original.Count,
                "Compilation must leave the old cache untouched until every shader succeeds.");
        });
        Check(peak >= requiredOverlap && peak <= parallelism,
            "Rebuild must scale independent compilations to the available CPUs while leaving one logical processor free.");
        Console.WriteLine($"Shader rebuild concurrency: peak {peak}, limit {parallelism}, logical processors {Environment.ProcessorCount}.");
        Check(started == entries.Length && reports.Select(r => r.Completed).SequenceEqual(Enumerable.Range(1, entries.Length)) &&
              reports.All(r => r.Total == entries.Length) && reports.Select(r => r.Key).ToHashSet().SetEquals(entries.Select(e => e.Key)),
            "Each shader must compile once and report ordered progress exactly once.");
        Check(Directory.GetFiles(AppPaths.ShaderCacheDirectory, "*.cso").Length == entries.Length,
            "A parallel rebuild must replace the complete cache set.");
        foreach (ShaderCacheEntry entry in entries)
            Check(ShaderBytecodeCache.GetOrCompile(entry, _ => throw new InvalidOperationException("Unexpected cache miss."))
                    .Span.SequenceEqual(new byte[] { (byte)entry.Source[0], 42 }),
                "Parallel results must remain associated with the correct shader key.");

        var beforeFailure = Directory.GetFiles(AppPaths.ShaderCacheDirectory, "*.cso")
            .ToDictionary(path => path, File.ReadAllBytes);
        bool failed = false;
        try
        {
            ShaderBytecodeCache.Rebuild(entries, entry => entry.Key == "parallel-5"
                ? throw new InvalidOperationException("Simulated compiler failure.")
                : new byte[] { 99 });
        }
        catch (AggregateException exception)
        {
            failed = exception.Flatten().InnerExceptions.Any(e => e.Message == "Simulated compiler failure.");
        }
        Check(failed && beforeFailure.All(file => File.ReadAllBytes(file.Key).SequenceEqual(file.Value)) &&
              Directory.GetFiles(AppPaths.ShaderCacheDirectory, "*.cso").Length == beforeFailure.Count,
            "A failed parallel compilation must preserve every previous cache file byte for byte.");
    }
}
