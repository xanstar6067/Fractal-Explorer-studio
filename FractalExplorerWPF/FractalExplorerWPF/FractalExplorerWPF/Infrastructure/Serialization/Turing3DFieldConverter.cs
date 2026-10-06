using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure.Serialization;

/// <summary>Ограниченная по размеру контрольная точка без потерь: float32-поле и байтовая карта масштабов, Brotli.</summary>
public sealed class Turing3DFieldConverter : JsonConverter<Turing3DField>
{
    public override Turing3DField Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        int side = root.GetProperty("Size").GetInt32();
        long step = root.GetProperty("Step").GetInt64();
        if (side is < Turing3DField.MinSize or > Turing3DField.MaxSize || step < 0) throw new JsonException("Некорректный размер или время поля.");
        int cells = checked(side * side * side), bytes = cells * 5;
        string data = root.GetProperty("Data").GetString() ?? throw new JsonException("Нет поля узора.");
        if (data.Length > (bytes + 65536L) * 4 / 3 + 4) throw new JsonException("Поле превышает допустимый размер.");
        try
        {
            using var input = new MemoryStream(Convert.FromBase64String(data));
            using var brotli = new BrotliStream(input, CompressionMode.Decompress);
            byte[] raw = new byte[bytes]; brotli.ReadExactly(raw);
            if (brotli.ReadByte() != -1) throw new JsonException("Лишние данные поля.");
            float[] values = new float[cells]; Buffer.BlockCopy(raw, 0, values, 0, cells * 4);
            byte[] scales = raw.AsSpan(cells * 4).ToArray();
            return new Turing3DField(side, step, values, scales, true);
        }
        catch (Exception e) when (e is IOException or FormatException or ArgumentException)
        { throw new JsonException("Повреждено поле узора Тьюринга 3D.", e); }
    }

    public override void Write(Utf8JsonWriter writer, Turing3DField field, JsonSerializerOptions options)
    {
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.Fastest, true))
        {
            brotli.Write(System.Runtime.InteropServices.MemoryMarshal.AsBytes(field.Field));
            brotli.Write(field.ScaleMap);
        }
        writer.WriteStartObject(); writer.WriteNumber("Size", field.Size); writer.WriteNumber("Step", field.Step);
        writer.WriteBase64String("Data", output.ToArray()); writer.WriteEndObject();
    }
}
