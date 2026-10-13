using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure.Serialization;

/// <summary>Bounded Brotli float32 checkpoint stored in the standard save JSON.</summary>
public sealed class GinzburgLandau3DFieldConverter : JsonConverter<GinzburgLandau3DField>
{
    public override GinzburgLandau3DField Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        try
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            int size = root.GetProperty("Size").GetInt32();
            long step = root.GetProperty("Step").GetInt64();
            if (size is < 32 or > 128 || step < 0) throw new JsonException("Некорректный размер или время комплексного поля.");
            int bytes = checked(size * size * size * 8);
            string data = root.GetProperty("Data").GetString() ?? throw new JsonException("Нет комплексного поля.");
            if (data.Length > (bytes + 65536L) * 4 / 3 + 4) throw new JsonException("Поле превышает допустимый размер.");
            using var input = new MemoryStream(Convert.FromBase64String(data));
            using var brotli = new BrotliStream(input, CompressionMode.Decompress);
            byte[] raw = new byte[bytes]; brotli.ReadExactly(raw);
            if (brotli.ReadByte() != -1) throw new JsonException("Лишние данные комплексного поля.");
            float[] values = new float[bytes / 4]; Buffer.BlockCopy(raw, 0, values, 0, bytes);
            return new(size, step, values, true);
        }
        catch (Exception e) when (e is IOException or FormatException or ArgumentException or InvalidOperationException or KeyNotFoundException)
        { throw new JsonException("Повреждено поле Гинзбурга–Ландау 3D.", e); }
    }

    public override void Write(Utf8JsonWriter writer, GinzburgLandau3DField field, JsonSerializerOptions options)
    {
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.Fastest, true))
            brotli.Write(System.Runtime.InteropServices.MemoryMarshal.AsBytes(field.Components));
        writer.WriteStartObject(); writer.WriteNumber("Size", field.Size); writer.WriteNumber("Step", field.Step);
        writer.WriteBase64String("Data", output.ToArray()); writer.WriteEndObject();
    }
}
