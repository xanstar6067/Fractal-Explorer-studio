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
        var model = root.TryGetProperty("Model", out var property) ? (TuringReactionModel)property.GetInt32() : TuringReactionModel.McCabe;
        if (!Enum.IsDefined(model)) throw new JsonException("Неизвестная модель реакции.");
        int cells = checked(side * side * side), bytes = cells * (model == TuringReactionModel.McCabe ? 5 : 13);
        string data = root.GetProperty("Data").GetString() ?? throw new JsonException("Нет поля узора.");
        if (data.Length > (bytes + 65536L) * 4 / 3 + 4) throw new JsonException("Поле превышает допустимый размер.");
        try
        {
            using var input = new MemoryStream(Convert.FromBase64String(data));
            using var brotli = new BrotliStream(input, CompressionMode.Decompress);
            byte[] raw = new byte[bytes]; brotli.ReadExactly(raw);
            if (brotli.ReadByte() != -1) throw new JsonException("Лишние данные поля.");
            float[] values = new float[cells]; Buffer.BlockCopy(raw, 0, values, 0, cells * 4);
            byte[] scales = raw.AsSpan(cells * 4, cells).ToArray();
            float[] u = [], v = [];
            if (model != TuringReactionModel.McCabe) {
                u = new float[cells]; v = new float[cells];
                Buffer.BlockCopy(raw, cells * 5, u, 0, cells * 4); Buffer.BlockCopy(raw, cells * 9, v, 0, cells * 4);
            }
            return new Turing3DField(side, step, values, scales, true, model, u, v);
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
            brotli.Write(System.Runtime.InteropServices.MemoryMarshal.AsBytes(field.ConcentrationU));
            brotli.Write(System.Runtime.InteropServices.MemoryMarshal.AsBytes(field.ConcentrationV));
        }
        writer.WriteStartObject(); writer.WriteNumber("Size", field.Size); writer.WriteNumber("Step", field.Step);
        if (field.Model != TuringReactionModel.McCabe) writer.WriteNumber("Model", (int)field.Model);
        writer.WriteBase64String("Data", output.ToArray()); writer.WriteEndObject();
    }
}
