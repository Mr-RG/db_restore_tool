using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace db_restore_tool
{
    public class RestoreConfig
    {
        public string ServerName { get; init; }
        public string Username { get; init; }
        public string Password { get; init; }
        public string TempDirectory { get; init; }

        [JsonConverter(typeof(SingleOrArrayConverter<string>))]
        public List<string> ZipPassword { get; init; }

        public string DataLocation { get; init; }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(ServerName))
                throw new Exception("Config error: 'ServerName' is missing or empty.");
            
            if (string.IsNullOrWhiteSpace(DataLocation))
                throw new Exception("Config error: 'DataLocation' is missing or empty.");
        }
    }

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
