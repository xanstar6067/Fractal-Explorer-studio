using FractalExplorer.Utilities;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FractalExplorer.Utilities.JsonConverters
{
    /// <summary>
    /// Предоставляет пользовательский конвертер JSON для типа <see cref="ComplexDecimal"/>.
    /// Это позволяет правильно сериализовать и десериализовать объекты <see cref="ComplexDecimal"/>
    /// в формат JSON и из него.
    /// </summary>
    public class JsonComplexDecimalConverter : JsonConverter<ComplexDecimal>
    {
        /// <summary>
        /// Считывает и десериализует объект <see cref="ComplexDecimal"/> из формата JSON.
        /// </summary>
        /// <param name="reader">Объект <see cref="Utf8JsonReader"/> для чтения из JSON.</param>
        /// <param name="typeToConvert">Тип, который должен быть преобразован (ожидается <see cref="ComplexDecimal"/>).</param>
        /// <param name="options">Параметры сериализации JSON.</param>
        /// <returns>Десериализованный объект <see cref="ComplexDecimal"/>.</returns>
        /// <exception cref="JsonException">Выбрасывается, если JSON-структура не соответствует ожидаемой.</exception>
        public override ComplexDecimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("Ожидался начальный объект.");
            }

            decimal real = 0;
            decimal imaginary = 0;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    return new ComplexDecimal(real, imaginary);
                }

                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    string propertyName = reader.GetString();
                    reader.Read(); // Переходим к значению свойства

                    if (propertyName.Equals("Real", StringComparison.OrdinalIgnoreCase))
                    {
                        real = reader.GetDecimal();
                    }
                    else if (propertyName.Equals("Imaginary", StringComparison.OrdinalIgnoreCase))
                    {
                        imaginary = reader.GetDecimal();
                    }
                }
            }
            throw new JsonException("Ошибка при чтении ComplexDecimal: не найден конечный объект.");
        }

        /// <summary>
        /// Записывает и сериализует объект <see cref="ComplexDecimal"/> в формат JSON.
        /// </summary>
        /// <param name="writer">Объект <see cref="Utf8JsonWriter"/> для записи в JSON.</param>
        /// <param name="value">Объект <see cref="ComplexDecimal"/> для сериализации.</param>
        /// <param name="options">Параметры сериализации JSON.</param>
        public override void Write(Utf8JsonWriter writer, ComplexDecimal value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("Real", value.Real);
            writer.WriteNumber("Imaginary", value.Imaginary);
            writer.WriteEndObject();
        }
    }
}
