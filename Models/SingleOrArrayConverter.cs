using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace db_restore_tool.Models
{
    public class SingleOrArrayConverter<T> : JsonConverter<List<T>>
    {
        public override List<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.StartArray)
            {
                return JsonSerializer.Deserialize<List<T>>(ref reader, options);
            }

            var singleValue = JsonSerializer.Deserialize<T>(ref reader, options);
            return new List<T> { singleValue };
        }

        public override void Write(Utf8JsonWriter writer, List<T> value, JsonSerializerOptions options)
        {
            if (value.Count == 1)
                JsonSerializer.Serialize(writer, value[0], options);
            else
                JsonSerializer.Serialize(writer, value, options);
        }
    }
}
