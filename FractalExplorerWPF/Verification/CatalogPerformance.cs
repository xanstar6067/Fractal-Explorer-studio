using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;

internal static partial class Program
{
    private sealed record CatalogTiming(
        string Pass, string Key, string Name, string Category, bool Rendered,
        double WallMilliseconds, double ProcessCpuMilliseconds, string? Error);

    private static async Task MeasureCatalogPreviewsAsync(string[] args)
    {
        string output = Path.GetFullPath(args.Length > 1 ? args[1] :
            Path.Combine(AppContext.BaseDirectory, "CatalogPreviewTimings"));
        Directory.CreateDirectory(output);
        using var sandbox = DataSandbox.Create("catalog-performance");
        var catalog = FractalCatalog.Create();
        var rows = new List<CatalogTiming>();
        string adapter;
        var host = new Direct3DDeviceHost();
        try { host.EnsureCreated(); adapter = host.AdapterName; }
        finally { host.Release(); }
        using var process = Process.GetCurrentProcess();
        Console.WriteLine($"Catalog timings: {catalog.Count} items; {Environment.ProcessorCount} logical CPUs; GPU: {adapter}; 512 x 512 rendered / 256 px bundled.");
        foreach (string pass in new[] { "cold", "warm-1", "warm-2" })
        {
            var loader = new CatalogPreviewLoader();
            var passWatch = Stopwatch.StartNew();
            foreach (var item in catalog)
            {
                bool rendered = CatalogPreviewLoader.IsRendered(item);
                string? error = null;
                TimeSpan cpuBefore = process.TotalProcessorTime;
                var watch = Stopwatch.StartNew();
                try
                {
                    if (rendered)
                    {
                        var image = await CatalogPreviewLoader.RenderAsync(item, CancellationToken.None);
                        Check(image.IsFrozen && image.PixelWidth == CatalogPreviewLoader.RenderedPixelSize,
                            $"Unexpected rendered preview for {item.DisplayName}.");
                    }
                    else
                    {
                        var tile = new CatalogTile(item, new CatalogGroup(item.CategoryPath));
                        loader.LoadThumbnails([tile]);
                        Check(tile.Thumbnail is not null, $"Missing resource for {item.DisplayName}.");
                    }
                }
                catch (Exception exception) { error = exception.ToString(); }
                watch.Stop();
                double cpu = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
                rows.Add(new CatalogTiming(pass, item.LaunchKey ?? item.DisplayName, item.DisplayName,
                    item.CategoryBreadcrumb, rendered, watch.Elapsed.TotalMilliseconds, cpu, error));
                // Flush after every item so partial results survive an interrupted measurement.
                File.WriteAllText(Path.Combine(output, "timings.json"), JsonSerializer.Serialize(new
                {
                    Timestamp = DateTimeOffset.Now,
                    Configuration =
#if DEBUG
                        "Debug",
#else
                        "Release",
#endif
                    LogicalProcessors = Environment.ProcessorCount,
                    Adapter = adapter,
                    RenderedPixelSize = CatalogPreviewLoader.RenderedPixelSize,
                    ThumbnailPixelWidth = CatalogPreviewLoader.ThumbnailPixelWidth,
                    Rows = rows
                }, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine($"{pass} {rows.Count(row => row.Pass == pass),3}/{catalog.Count}: {watch.Elapsed.TotalMilliseconds / 1000,8:F3} s | {item.DisplayName}" +
                    (error is null ? "" : " | ERROR"));
            }
            Console.WriteLine($"{pass}: sum {rows.Where(row => row.Pass == pass).Sum(row => row.WallMilliseconds) / 1000:F3} s; elapsed {passWatch.Elapsed.TotalSeconds:F3} s.");
        }
        using var report = new StreamWriter(Path.Combine(output, "timings.md"));
        await report.WriteLineAsync("# Время подготовки превью каталога WPF");
        await report.WriteLineAsync($"\nRelease; {Environment.ProcessorCount} логических процессора; {adapter}. Последовательная очередь, штатные пресеты, рендер 512×512, встроенные PNG — 256 px.");
        await report.WriteLineAsync("\nПервый проход: новый изолированный кэш шейдеров. Два следующих: тот же кэш, новые рендереры и изображения. Разность времён включает также прогрев JIT и драйвера; это не чистое время компилятора.");
        await report.WriteLineAsync("\n| Превью | Тип | Пустой кэш, с | Готовый кэш 1, с | Готовый кэш 2, с | Среднее с готовым кэшем, с |");
        await report.WriteLineAsync("|---|---|---:|---:|---:|---:|");
        foreach (var group in rows.GroupBy(row => row.Key)
            .OrderByDescending(group => group.Where(row => row.Pass != "cold").Average(row => row.WallMilliseconds)))
        {
            string Seconds(string pass) => (group.Single(row => row.Pass == pass).WallMilliseconds / 1000).ToString("F3", CultureInfo.InvariantCulture);
            double warm = group.Where(row => row.Pass != "cold").Average(row => row.WallMilliseconds) / 1000;
            await report.WriteLineAsync($"| {group.First().Name} | {(group.First().Rendered ? "рендер" : "PNG")} | {Seconds("cold")} | {Seconds("warm-1")} | {Seconds("warm-2")} | {warm.ToString("F3", CultureInfo.InvariantCulture)} |");
        }
        Console.WriteLine($"Results: {output}; errors: {rows.Count(row => row.Error is not null)}.");
        Check(rows.All(row => row.Error is null), "Some measured previews failed; see timings.json.");
    }
}