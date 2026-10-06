using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure.Serialization;

/// <summary>Bounded, lossless float32 checkpoint in the normal per-save JSON file.</summary>
public sealed class GrayScott3DFieldConverter : JsonConverter<GrayScott3DField>
{
    public override GrayScott3DField Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        int side = root.GetProperty("Size").GetInt32();
        long step = root.GetProperty("Step").GetInt64();
        if (side is < 32 or > 128 || step < 0) throw new JsonException("Некорректный размер или время поля.");
        int bytes = checked(side * side * side * 8);
        string data = root.GetProperty("Data").GetString() ?? throw new JsonException("Нет поля U/V.");
        if (data.Length > (bytes + 65536L) * 4 / 3 + 4) throw new JsonException("Поле превышает допустимый размер.");
        try
        {
            using var input = new MemoryStream(Convert.FromBase64String(data));
            using var brotli = new BrotliStream(input, CompressionMode.Decompress);
            byte[] raw = new byte[bytes]; brotli.ReadExactly(raw);
            if (brotli.ReadByte() != -1) throw new JsonException("Лишние данные поля.");
            float[] values = new float[bytes / 4]; Buffer.BlockCopy(raw, 0, values, 0, bytes);
            return new GrayScott3DField(side, step, values, true);
        }
        catch (Exception e) when (e is IOException or FormatException or ArgumentException)
        { throw new JsonException("Повреждено поле Gray–Scott 3D.", e); }
    }

    public override void Write(Utf8JsonWriter writer, GrayScott3DField field, JsonSerializerOptions options)
    {
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.Fastest, true))
            brotli.Write(System.Runtime.InteropServices.MemoryMarshal.AsBytes(field.Concentrations));
        writer.WriteStartObject(); writer.WriteNumber("Size", field.Size); writer.WriteNumber("Step", field.Step);
        writer.WriteBase64String("Data", output.ToArray()); writer.WriteEndObject();
    }
}
