using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using FractalExplorerWPF.Core.NewtonMath;

namespace FractalExplorerWPF.Infrastructure.Serialization;

/// <summary>
/// Сериализация <see cref="FloatExp"/> — типа коэффициента зума глубокого движка.
///
/// Значения в диапазоне double пишутся JSON-числом: файлы сохранений, созданные до
/// перехода зума на расширенный диапазон, остаются читаемыми, а новые — совместимыми со
/// старыми версиями формата. Выход за 1.8e308 представим только строкой научной нотации
/// (<see cref="FloatExp.ToInvariantString"/>), и она же читается обратно. Чтение принимает
/// оба вида токена, поэтому порядок версий в обе стороны безопасен.
/// </summary>
public sealed class FloatExpJsonConverter : JsonConverter<FloatExp>
{
    public override FloatExp Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return FloatExp.FromDouble(reader.GetDouble());
            case JsonTokenType.String:
                string text = reader.GetString() ?? string.Empty;
                return FloatExp.TryParse(text, out FloatExp parsed) ? parsed : default;
            case JsonTokenType.Null:
                return default;
            default:
                throw new JsonException($"Неожидаемый токен {reader.TokenType} для FloatExp.");
        }
    }

    public override void Write(Utf8JsonWriter writer, FloatExp value, JsonSerializerOptions options)
    {
        double asDouble = value.ToDouble();
        if (double.IsFinite(asDouble) && (asDouble != 0.0 || value.IsZero))
            writer.WriteNumberValue(asDouble);
        else
            writer.WriteStringValue(value.ToInvariantString());
    }

    /// <summary>
    /// Отображение для пользовательского интерфейса: привычные 8 значащих цифр в пределах
    /// диапазона double, иначе короткая научная нотация вида <c>1.2345678e+1000</c>.
    /// Формат предназначен только для показа и ввода — точность позиции несут точные
    /// координаты центра, а не зум.
    /// </summary>
    public static string ToDisplay(FloatExp value)
    {
        double asDouble = value.ToDouble();
        if (double.IsFinite(asDouble) && asDouble != 0.0)
            return asDouble.ToString("G8", CultureInfo.InvariantCulture);
        if (value.IsZero || !value.IsFinite) return value.ToInvariantString();

        double log10 = value.Log10();
        int decimalExponent = (int)Math.Floor(log10);
        double lead = Math.Pow(10.0, log10 - decimalExponent);
        if (lead >= 10.0) { lead /= 10.0; decimalExponent++; }
        if (value.Sign < 0) lead = -lead;
        return lead.ToString("0.#######", CultureInfo.InvariantCulture) +
               "e+" + decimalExponent.ToString(CultureInfo.InvariantCulture);
    }
}
