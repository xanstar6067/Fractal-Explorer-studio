using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace FractalExplorerWPF.Infrastructure;

/// <summary>Описание одного варианта шейдера; ключ должен быть уникальным для всех GPU-режимов.</summary>
internal readonly record struct ShaderCacheEntry(string Key, string Source, string EntryPoint, string Profile);

/// <summary>
/// Единый дисковый кэш байткода Direct3D. Сигнатура учитывает исходник, точку входа и профиль;
/// контрольная сумма байткода не позволяет использовать повреждённый файл. Устройство и драйвер
/// сюда не входят: их собственный машинный код создаётся при CreatePixelShader/CreateVertexShader.
/// </summary>
internal static class ShaderBytecodeCache
{
    private static readonly object Sync = new();
    private static readonly byte[] Magic = "FXSHDR01"u8.ToArray();
    private const int HeaderSize = 8 + 32 + 32 + 4;
    private const int MaxBytecodeSize = 16 * 1024 * 1024;

    public static ReadOnlyMemory<byte> GetOrCompile(ShaderCacheEntry entry, Func<ShaderCacheEntry, ReadOnlyMemory<byte>> compiler)
    {
        lock (Sync)
        {
            string path = AppPaths.GetShaderCacheFile(entry.Key);
            try
            {
                byte[]? cached = TryRead(path, Signature(entry));
                if (cached is not null) return cached;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Недоступный кэш не должен мешать рендерингу.
            }

            byte[] compiled = compiler(entry).ToArray();
            try
            {
                Write(path, Signature(entry), compiled);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Шейдер уже скомпилирован: отсутствие прав на запись не делает кадр ошибочным.
            }
            return compiled;
        }
    }

    /// <summary>
    /// Сначала компилирует полный новый набор, затем заменяет файлы под общим замком.
    /// Если компиляция не удалась, прежний кэш остаётся нетронутым.
    /// </summary>
    public static void Rebuild(
        IReadOnlyList<ShaderCacheEntry> entries,
        Func<ShaderCacheEntry, ReadOnlyMemory<byte>> compiler,
        Action<int, int, string>? progress = null)
    {
        var compiled = new List<(ShaderCacheEntry Entry, byte[] Bytecode)>(entries.Count);
        foreach (ShaderCacheEntry entry in entries)
        {
            byte[] bytecode = compiler(entry).ToArray();
            compiled.Add((entry, bytecode));
            progress?.Invoke(compiled.Count, entries.Count, entry.Key);
        }

        lock (Sync)
        {
            Directory.CreateDirectory(AppPaths.ShaderCacheDirectory);
            foreach (string path in Directory.EnumerateFiles(AppPaths.ShaderCacheDirectory, "*.cso", SearchOption.TopDirectoryOnly))
                File.Delete(path);
            foreach ((ShaderCacheEntry entry, byte[] bytecode) in compiled)
                Write(AppPaths.GetShaderCacheFile(entry.Key), Signature(entry), bytecode);
        }
    }

    private static byte[] Signature(ShaderCacheEntry entry) => SHA256.HashData(Encoding.UTF8.GetBytes(
        $"D3DCompile-default-v1\0{entry.Key}\0{entry.EntryPoint}\0{entry.Profile}\0{entry.Source}"));

    private static byte[]? TryRead(string path, byte[] signature)
    {
        if (!File.Exists(path)) return null;
        long length = new FileInfo(path).Length;
        if (length is < HeaderSize or > HeaderSize + MaxBytecodeSize) return null;
        byte[] data = File.ReadAllBytes(path);
        if (data.Length < HeaderSize || !data.AsSpan(0, 8).SequenceEqual(Magic) ||
            !data.AsSpan(8, 32).SequenceEqual(signature)) return null;
        int bytecodeLength = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(72, 4));
        if (bytecodeLength <= 0 || bytecodeLength > MaxBytecodeSize || data.Length != HeaderSize + bytecodeLength)
            return null;
        ReadOnlySpan<byte> bytecode = data.AsSpan(HeaderSize, bytecodeLength);
        if (!SHA256.HashData(bytecode).AsSpan().SequenceEqual(data.AsSpan(40, 32))) return null;
        return bytecode.ToArray();
    }

    private static void Write(string path, byte[] signature, byte[] bytecode)
    {
        if (bytecode.Length is <= 0 or > MaxBytecodeSize)
            throw new InvalidDataException("Недопустимый размер скомпилированного шейдера.");

        byte[] data = new byte[HeaderSize + bytecode.Length];
        Magic.CopyTo(data, 0);
        signature.CopyTo(data, 8);
        SHA256.HashData(bytecode).CopyTo(data, 40);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(72, 4), bytecode.Length);
        bytecode.CopyTo(data, HeaderSize);

        AppPaths.EnsureDirectoryFor(path);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, data);
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
