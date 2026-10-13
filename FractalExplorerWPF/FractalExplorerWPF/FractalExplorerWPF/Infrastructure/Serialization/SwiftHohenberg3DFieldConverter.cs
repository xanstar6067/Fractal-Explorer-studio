using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure.Serialization;

/// <summary>Bounded lossless float32 checkpoint in the standard JSON save.</summary>
public sealed class SwiftHohenberg3DFieldConverter : JsonConverter<SwiftHohenberg3DField>
{
    public override SwiftHohenberg3DField Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        try
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            int size = root.GetProperty("Size").GetInt32(); long step = root.GetProperty("Step").GetInt64();
            if (!SwiftHohenberg3DSettings.IsSupportedSize(size) || step < 0) throw new JsonException("Некорректная сетка или время поля.");
            int bytes = checked(size * size * size * 4);
            string data = root.GetProperty("Data").GetString() ?? throw new JsonException("Нет поля Свифта–Хоэнберга.");
            if (data.Length > (bytes + 65536L) * 4 / 3 + 4) throw new JsonException("Поле превышает допустимый размер.");
            using var input = new MemoryStream(Convert.FromBase64String(data));
            using var brotli = new BrotliStream(input, CompressionMode.Decompress);
            byte[] raw = new byte[bytes]; brotli.ReadExactly(raw);
            if (brotli.ReadByte() != -1) throw new JsonException("Лишние данные поля.");
            float[] values = new float[bytes / 4]; Buffer.BlockCopy(raw, 0, values, 0, bytes);
            return new(size, step, values, true);
        }
        catch (Exception e) when (e is IOException or FormatException or ArgumentException or InvalidOperationException or KeyNotFoundException)
        { throw new JsonException("Повреждено поле Свифта–Хоэнберга 3D.", e); }
    }

    public override void Write(Utf8JsonWriter writer, SwiftHohenberg3DField field, JsonSerializerOptions options)
    {
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.Fastest, true)) brotli.Write(MemoryMarshal.AsBytes(field.Samples));
        writer.WriteStartObject(); writer.WriteNumber("Size", field.Size); writer.WriteNumber("Step", field.Step);
        writer.WriteBase64String("Data", output.ToArray()); writer.WriteEndObject();
    }
}
