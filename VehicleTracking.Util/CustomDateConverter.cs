using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VehicleTracking.Util
{
    public class CustomDateConverter : JsonConverter<DateTime>
    {
        private readonly string _dateFormat;

        public CustomDateConverter()
        {
            _dateFormat = "yyyy-MM-dd";
        }

        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String && DateTime.TryParseExact(reader.GetString(), _dateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
            {
                return date;
            }
            throw new JsonException($"Fecha inválida, se esperaba el formato {_dateFormat}");
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString(_dateFormat));
        }
    }
}
