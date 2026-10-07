using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure.Serialization;

public sealed class Lichtenberg3DFieldConverter : JsonConverter<Lichtenberg3DField>
{
    public override Lichtenberg3DField Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        try
        {
            var root = document.RootElement;
            int n = root.GetProperty("Size").GetInt32(), count = root.GetProperty("Cells").GetInt32();
            if (!Lichtenberg3DSettings.IsSupportedSize(n) || count is < 1 or > Lichtenberg3DSettings.MaxSegments + 1)
                throw new JsonException("Некорректный размер поля пробоя.");
            int bytes = checked((n * n * n + count) * 4);
            string data = root.GetProperty("Data").GetString() ?? throw new JsonException("Нет поля пробоя.");
            if (data.Length > (bytes + 65536L) * 4 / 3 + 4) throw new JsonException("Слишком большое поле.");
            using var input = new MemoryStream(Convert.FromBase64String(data));
            using var stream = new BrotliStream(input, CompressionMode.Decompress);
            byte[] raw = new byte[bytes]; stream.ReadExactly(raw);
            if (stream.ReadByte() != -1) throw new JsonException("Лишние данные поля.");
            int[] cells = new int[count]; float[] values = new float[n * n * n];
            Buffer.BlockCopy(raw, 0, cells, 0, count * 4);
            Buffer.BlockCopy(raw, count * 4, values, 0, values.Length * 4);
            return new(n, cells, values);
        }
        catch (Exception e) when (e is IOException or FormatException or ArgumentException or InvalidOperationException or KeyNotFoundException)
        { throw new JsonException("Повреждено поле фигур Лихтенберга.", e); }
    }

    public override void Write(Utf8JsonWriter writer, Lichtenberg3DField field, JsonSerializerOptions options)
    {
        using var output = new MemoryStream();
        using (var stream = new BrotliStream(output, CompressionLevel.Fastest, true))
        {
            stream.Write(System.Runtime.InteropServices.MemoryMarshal.AsBytes(field.GrowthOrder));
            stream.Write(System.Runtime.InteropServices.MemoryMarshal.AsBytes(field.Potential));
        }
        writer.WriteStartObject(); writer.WriteNumber("Size", field.Size); writer.WriteNumber("Cells", field.Count + 1);
        writer.WriteBase64String("Data", output.ToArray()); writer.WriteEndObject();
    }
}
