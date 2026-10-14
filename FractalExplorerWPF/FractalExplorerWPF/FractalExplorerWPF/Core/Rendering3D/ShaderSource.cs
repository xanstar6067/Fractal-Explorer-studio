using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>
/// Исходники HLSL: файлы <c>Shaders/*.hlsl</c>, вложенные в сборку. Варианты шейдера
/// различаются только через <c>#define</c> и <c>#if</c> и через явные места
/// <c>#include "@имя"</c>, которые заполняет вызывающий код; текст шейдера никто не правит.
/// </summary>
internal static class ShaderSource
{
    private const string ResourcePrefix = "FractalExplorerWPF.Core.Rendering3D.Shaders.";
    private static readonly ConcurrentDictionary<string, string> Loaded = new(StringComparer.Ordinal);
    private static readonly Regex IncludeLine = new(@"^[ \t]*#include ""([^""@][^""]*)""(?=\r?$)", RegexOptions.Multiline);
    private static readonly Regex SlotLine = new(@"^[ \t]*#include ""@([^""]+)""(?=\r?$)", RegexOptions.Multiline);

    /// <summary>
    /// <c>#define KIND_&lt;имя&gt; &lt;номер&gt;</c> для каждого пункта <see cref="Fractal3DKind"/>: шейдеры
    /// сравнивают режим по имени, и перестановка пунктов перечисления их не ломает.
    /// </summary>
    public static string KindDefines { get; } = string.Concat(
        Enum.GetValues<Fractal3DKind>().Select(kind => $"#define KIND_{kind} {(int)kind}\n"));

    /// <summary>
    /// Текст файла <c>Shaders/<paramref name="name"/></c> без завершающего перевода строки.
    /// Строка <c>#include "файл.hlsl"</c> заменяется текстом этого файла здесь, а не
    /// компилятором: ключ дискового кэша считается по полному исходнику.
    /// </summary>
    public static string Load(string name)
    {
        string source = Loaded.GetOrAdd(name, Read);
        Match slot = SlotLine.Match(source);
        return slot.Success
            ? throw new InvalidOperationException($"Шейдер {name}: место @{slot.Groups[1].Value} не заполнено.")
            : source;
    }

    /// <summary>
    /// <see cref="Load(string)"/> с заполненными местами <c>#include "@имя"</c>: строка места
    /// заменяется текстом (пустым, если режиму здесь добавить нечего). Незаполненное или
    /// лишнее место — ошибка.
    /// </summary>
    public static string Load(string name, params ReadOnlySpan<(string Slot, string Text)> slots)
    {
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string slot, string text) in slots) texts.Add(slot, text);
        var used = new HashSet<string>(StringComparer.Ordinal);
        string source = SlotLine.Replace(Loaded.GetOrAdd(name, Read), match =>
        {
            string slot = match.Groups[1].Value;
            if (!texts.TryGetValue(slot, out string? text))
                throw new InvalidOperationException($"Шейдер {name}: место @{slot} не заполнено.");
            used.Add(slot);
            return text;
        });
        foreach (string slot in texts.Keys)
            if (!used.Contains(slot))
                throw new InvalidOperationException($"Шейдер {name}: нет места @{slot}.");
        return source;
    }

    private static string Read(string name)
    {
        using Stream stream = typeof(ShaderSource).Assembly.GetManifestResourceStream(ResourcePrefix + name)
            ?? throw new InvalidOperationException($"В сборке нет шейдера {name}.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        string text = reader.ReadToEnd();
        if (text.EndsWith("\r\n", StringComparison.Ordinal)) text = text[..^2];
        else if (text.EndsWith('\n')) text = text[..^1];
        return IncludeLine.Replace(text, match => Loaded.GetOrAdd(match.Groups[1].Value, Read));
    }
}
