using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Core.Rendering;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>
/// Шаг многомасштабной модели Маккейба в кубе: для каждого масштаба — сглаженные активатор и
/// ингибитор, их сглаженное различие, выбор масштаба с наименьшей вариацией; затем симметрия и
/// нормировка поля в [-1, 1]. Сглаживание — три прохода квадратного окна по каждой оси (как в 2D),
/// скользящей суммой: стоимость не зависит от радиуса.
/// </summary>
internal static class Turing3DComputeShader
{
    public const uint CellGroup = 256, VolumeX = 8, VolumeY = 8, VolumeZ = 4;

    public static readonly string[] EntryPoints = ["Blur", "Difference", "Choose", "Compose", "Symmetry", "Reduce", "Normalize", "Paint", "Publish", "ReactionStep", "ReactionDisplay"];

    public static IReadOnlyList<ShaderCacheEntry> CacheEntries { get; } = EntryPoints
        .Select(entry => new ShaderCacheEntry("turing3d-" + entry, Source, entry, "cs_5_0")).ToArray();

    public static string Source => ShaderSource.Load("Turing3DCompute.hlsl");
}
