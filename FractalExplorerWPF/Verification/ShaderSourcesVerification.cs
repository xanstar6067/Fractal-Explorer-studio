using System.IO;
using System.Security.Cryptography;
using System.Text;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure;
using Vortice.D3DCompiler;

internal static partial class Program
{
    /// <summary>
    /// Хеши итоговых исходников всех GPU-шейдеров (набор «Пересобрать шейдеры») без компиляции
    /// и без видеокарты: побайтная сверка до и после правки того, как шейдеры хранятся и
    /// собираются. Совпавший исходник — тот же ключ дискового кэша и тот же байткод.
    /// <c>--compile префикс[,префикс]</c> компилирует шейдеры с такими ключами и добавляет хеш
    /// байткода: исходник, изменившийся только в пробелах, сверяется по нему.
    /// </summary>
    private static void VerifyShaderSources(string[] args)
    {
        string? record = ReadOption(args, "--record");
        string? compare = ReadOption(args, "--compare");
        string[] compiled = ReadOption(args, "--compile")?.Split(',') ?? [];
        List<ShaderCacheEntry> entries = Fractal3DRenderer.AllShaderEntries();
        Check(entries.Select(entry => entry.Key).Distinct().Count() == entries.Count,
            "Ключи шейдеров должны быть уникальными.");

        List<string> lines = entries.Select(entry =>
            $"{entry.Key} {entry.EntryPoint} {entry.Profile} {(entry.SkipOptimization ? "skip" : "opt")}:" +
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(entry.Source))) +
            (compiled.Any(prefix => entry.Key.StartsWith(prefix, StringComparison.Ordinal))
                ? " dxbc:" + Convert.ToHexString(SHA256.HashData(Compiler.Compile(entry.Source, entry.EntryPoint, "Fractal3D",
                    entry.Profile, entry.SkipOptimization ? ShaderFlags.SkipOptimization : ShaderFlags.None).Span))
                : "")).ToList();

        if (record is not null)
        {
            File.WriteAllLines(record, lines);
            Console.WriteLine($"Записано {lines.Count} исходников шейдеров в {record}.");
        }
        if (compare is not null)
        {
            string[] expected = File.ReadAllLines(compare).Where(line => line.Length > 0).ToArray();
            var differing = lines.Where(line => !expected.Contains(line) && !SameBytecode(line)).ToList();
            foreach (string line in differing)
                Console.WriteLine($"Исходник отличается от эталона: {line}");
            foreach (string line in lines.Where(line => !expected.Contains(line) && SameBytecode(line)))
                Console.WriteLine($"Исходник изменился, байткод тот же: {line[..line.IndexOf(' ')]}");

            // Ключ и байткод те же, отличается только текст исходника.
            bool SameBytecode(string line) =>
                line.Contains(" dxbc:") && expected.Any(old => old.Contains(" dxbc:") &&
                    old[..old.IndexOf(' ')] == line[..line.IndexOf(' ')] &&
                    old[old.IndexOf(" dxbc:")..] == line[line.IndexOf(" dxbc:")..]);
            Check(expected.Length == lines.Count, $"В эталоне {expected.Length} шейдеров, собрано {lines.Count}.");
            Check(differing.Count == 0, $"{differing.Count} исходников шейдеров отличаются от эталона {compare}.");
            Console.WriteLine($"Все {lines.Count} исходников шейдеров совпадают с эталоном {compare}.");
        }
        if (record is null && compare is null)
            Console.WriteLine($"Собрано {lines.Count} исходников шейдеров (--record/--compare файл — сверка).");
    }
}
