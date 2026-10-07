using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure.Serialization;

/// <summary>Bounded, lossless float32 checkpoint in the normal per-save JSON file.</summary>
public sealed class Physarum3DFieldConverter : JsonConverter<Physarum3DField>
{
    public override Physarum3DField Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        try
        {
            int side = root.GetProperty("Size").GetInt32();
            long step = root.GetProperty("Step").GetInt64();
            if (!Physarum3DSettings.IsSupportedSize(side) || step < 0) throw new JsonException("Некорректный размер или время поля.");
            int count = root.GetProperty("AgentCount").GetInt32();
            if (count is < 1024 or > 262144) throw new JsonException("Недопустимое число агентов.");
            int cells = side * side * side;
            int bytes = checked((cells + count * 8) * 4);
            string data = root.GetProperty("Data").GetString() ?? throw new JsonException("Нет поля состава.");
            if (data.Length > (bytes + 65536L) * 4 / 3 + 4) throw new JsonException("Поле превышает допустимый размер.");
            using var input = new MemoryStream(Convert.FromBase64String(data));
            using var brotli = new BrotliStream(input, CompressionMode.Decompress);
            byte[] raw = new byte[bytes]; brotli.ReadExactly(raw);
            if (brotli.ReadByte() != -1) throw new JsonException("Лишние данные поля.");
            float[] values = new float[bytes / 4]; Buffer.BlockCopy(raw, 0, values, 0, bytes);
            return new Physarum3DField(side, step, values.AsSpan(0,cells), values.AsSpan(cells));
        }
        catch (Exception e) when (e is IOException or FormatException or ArgumentException or InvalidOperationException or KeyNotFoundException)
        { throw new JsonException("Повреждено поле Physarum 3D.", e); }
    }

    public override void Write(Utf8JsonWriter writer, Physarum3DField field, JsonSerializerOptions options)
    {
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.Fastest, true))
        {
            brotli.Write(System.Runtime.InteropServices.MemoryMarshal.AsBytes(field.Trail));
            brotli.Write(System.Runtime.InteropServices.MemoryMarshal.AsBytes(field.AgentState));
        }
        writer.WriteStartObject(); writer.WriteNumber("Size", field.Size); writer.WriteNumber("Step", field.Step);
        writer.WriteNumber("AgentCount", field.AgentCount);
        writer.WriteBase64String("Data", output.ToArray()); writer.WriteEndObject();
    }
}
