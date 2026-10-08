using System.IO;
using System.IO.Compression;
using System.Text.Json;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure;

/// <summary>Five single-channel 3D seeds from Bert Chan's Lenia (MIT), converted losslessly from RLE to bytes.</summary>
internal static class Lenia3DSeedLibrary
{
    internal sealed record SeedData(string Key, string Name, double Radius, double Mean, double Width,
        double[] Shells, int X, int Y, int Z, string Data)
    {
        internal byte[] Cells { get; set; } = [];
    }
    private static readonly Lazy<IReadOnlyList<SeedData>> Data = new(Load);
    internal static SeedData Get(Lenia3DSeed seed) => Data.Value.Single(s => s.Key == seed.ToString());
    private static IReadOnlyList<SeedData> Load()
    {
        using var stream = typeof(Lenia3DSeedLibrary).Assembly.GetManifestResourceStream("FractalExplorerWPF.Assets.Lenia.seeds.json")
            ?? throw new InvalidOperationException("Отсутствуют затравки Lenia.");
        var seeds = JsonSerializer.Deserialize<SeedData[]>(stream) ?? throw new InvalidOperationException("Повреждены затравки Lenia.");
        foreach (var seed in seeds)
        {
            if (seed.X is < 1 or > 64 || seed.Y is < 1 or > 64 || seed.Z is < 1 or > 64) throw new InvalidDataException();
            using var input = new MemoryStream(Convert.FromBase64String(seed.Data));
            using var zip = new GZipStream(input, CompressionMode.Decompress);
            seed.Cells = new byte[seed.X*seed.Y*seed.Z]; zip.ReadExactly(seed.Cells);
            if (zip.ReadByte() != -1) throw new InvalidDataException();
        }
        return seeds;
    }
    internal static Lenia3DSettings Settings(Lenia3DSeed seed)
    {
        var s = Get(seed); var b = s.Shells;
        return new() { SeedShape = seed, Radius = s.Radius, GrowthMean = s.Mean, GrowthWidth = s.Width,
            ShellCount = b.Length, Beta1 = b[0], Beta2 = b.Length>1?b[1]:0, Beta3 = b.Length>2?b[2]:0, Beta4 = b.Length>3?b[3]:0 };
    }
}
