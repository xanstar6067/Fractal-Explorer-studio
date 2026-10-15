using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Models;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Vortice.D3DCompiler;

namespace FractalExplorerWPF.Core.Rendering3D;

internal static class Newton3DComputeShader
{
    private static readonly ConcurrentDictionary<string, ShaderCacheEntry> Used = new(StringComparer.Ordinal);
    internal static ShaderCacheEntry CacheEntry => Entry(new());
    internal static IEnumerable<ShaderCacheEntry> UsedEntries => Used.Values;

    internal static Newton3DSettings Prepare(Newton3DSettings settings, CancellationToken token)
    {
        var entry = Entry(settings);
        settings = Newton3DFormula.Prepare(settings, token);
        token.ThrowIfCancellationRequested();
        ShaderBytecodeCache.GetOrCompile(entry, e => Compiler.Compile(e.Source, e.EntryPoint, "Fractal3D", e.Profile));
        token.ThrowIfCancellationRequested();
        return settings;
    }

    internal static ShaderCacheEntry Entry(Newton3DSettings settings, bool expressionProbe = false)
    {
        if (settings.Formula is null)
            return new("newton3d-field", "#define NEWTON_CUSTOM 0\n" +
                ShaderSource.Load("Newton3DCompute.hlsl", ("Formula", "")), "Compute", "cs_5_0");
        string functions = ComplexExpressionShader.Build(Newton3DFormula.Parse(settings.Formula));
        string key = "newton3d-formula-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(functions)))[..16] + (expressionProbe ? "-values" : "");
        return Used.GetOrAdd(key, _ => new(key, "#define NEWTON_CUSTOM 1\n" +
            ShaderSource.Load("Newton3DCompute.hlsl", ("Formula", functions)), expressionProbe ? "ProbeExpression" : "Compute", "cs_5_0"));
    }
}
