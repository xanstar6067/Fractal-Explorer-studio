using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FractalExplorerWPF.Infrastructure.Serialization;

/// <summary>Lossless, bounded checkpoint storage; preserves float bits and prevents decompression bombs.</summary>
public sealed class CompressedFloatArrayJsonConverter : JsonConverter<float[]>
{
    private const int MaxValues = Models.TuringState.MaxGridSize * Models.TuringState.MaxGridSize;
    public override float[] Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) throw new JsonException("Ожидалось сжатое поле.");
        byte[] compressed = reader.GetBytesFromBase64();
        if (compressed.Length > MaxValues * 4 + 4096) throw new JsonException("Поле слишком велико.");
        using var input = new MemoryStream(compressed);
        using var zip = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        byte[] buffer = new byte[8192];
        int count;
        while ((count = zip.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (output.Length + count > MaxValues * 4) throw new JsonException("Поле превышает допустимый размер.");
            output.Write(buffer, 0, count);
        }
        byte[] bytes = output.ToArray();
        if (bytes.Length % 4 != 0) throw new JsonException("Повреждённое поле.");
        var values = new float[bytes.Length / 4];
        for (int i = 0; i < values.Length; i++) values[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i * 4, 4));
        return values;
    }
    public override void Write(Utf8JsonWriter writer, float[] values, JsonSerializerOptions options)
    {
        if (values.Length > MaxValues) throw new JsonException("Поле слишком велико.");
        byte[] bytes = new byte[values.Length * 4];
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 4, 4), values[i]);
        using var output = new MemoryStream();
        using (var zip = new BrotliStream(output, CompressionLevel.Optimal, leaveOpen: true)) zip.Write(bytes);
        writer.WriteBase64StringValue(output.ToArray());
    }
}
