using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>
/// Исходники HLSL: файлы <c>Shaders/*.hlsl</c>, вложенные в сборку, и общая заплатка для
/// шейдеров, собранных правкой соседнего исходника.
/// </summary>
internal static class ShaderSource
{
    private const string ResourcePrefix = "FractalExplorerWPF.Core.Rendering3D.Shaders.";
    private static readonly ConcurrentDictionary<string, string> Loaded = new(StringComparer.Ordinal);
    private static readonly Regex IncludeLine = new(@"^[ \t]*#include ""([^""]+)""(?=\r?$)", RegexOptions.Multiline);

    /// <summary>
    /// Текст файла <c>Shaders/<paramref name="name"/></c> без завершающего перевода строки.
    /// Строка <c>#include "файл.hlsl"</c> заменяется текстом этого файла здесь, а не
    /// компилятором: ключ дискового кэша считается по полному исходнику.
    /// </summary>
    public static string Load(string name) => Loaded.GetOrAdd(name, Read);

    private static string Read(string name)
    {
        using Stream stream = typeof(ShaderSource).Assembly.GetManifestResourceStream(ResourcePrefix + name)
            ?? throw new InvalidOperationException($"В сборке нет шейдера {name}.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        string text = reader.ReadToEnd();
        if (text.EndsWith("\r\n", StringComparison.Ordinal)) text = text[..^2];
        else if (text.EndsWith('\n')) text = text[..^1];
        return IncludeLine.Replace(text, match => Load(match.Groups[1].Value));
    }

    /// <summary>
    /// Заменяет каждый фрагмент во всех вхождениях, по порядку. Фрагмент, которого нет в
    /// исходнике, — ошибка: изменившийся базовый шейдер должен падать явно, а не терять
    /// заплатку молча.
    /// </summary>
    public static string Patch(string source, string shader, params ReadOnlySpan<(string Find, string Replace)> patches)
    {
        foreach ((string find, string replace) in patches)
        {
            if (!source.Contains(find, StringComparison.Ordinal))
                throw new InvalidOperationException($"Шейдер {shader}: в базовом исходнике не найден фрагмент «{find}».");
            source = source.Replace(find, replace, StringComparison.Ordinal);
        }
        return source;
    }
}
