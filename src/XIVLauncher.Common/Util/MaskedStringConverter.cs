using Newtonsoft.Json;
using System;
using System.Collections.Generic;

public class MaskMiddleConverter : JsonConverter
{
    public override bool CanConvert(Type objectType)
    {
        return objectType == typeof(string) || objectType == typeof(List<string>);
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        if (objectType == typeof(List<string>))
        {
            var list = new List<string>();
            if (reader.TokenType == JsonToken.StartArray)
            {
                while (reader.Read())
                {
                    if (reader.TokenType == JsonToken.EndArray)
                        break;

                    if (reader.TokenType == JsonToken.String)
                    {
                        list.Add((string)reader.Value);
                    }
                }
            }
            return list;
        }

        // 反序列化单个字符串
        if (reader.TokenType == JsonToken.String)
        {
            return reader.Value;
        }

        throw new JsonSerializationException($"Unexpected token type: {reader.TokenType}");
    }

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        if (value == null)
        {
            writer.WriteNull();
            return;
        }

        if (value is string str)
        {
            string masked = MaskString(str);
            writer.WriteValue(masked);
        }
        else if (value is List<string> list)
        {
            writer.WriteStartArray();
            foreach (var item in list)
            {
                writer.WriteValue(MaskString(item));
            }
            writer.WriteEndArray();
        }
    }

    private string MaskString(string input)
    {
        if (string.IsNullOrEmpty(input) || input.Length <= 2)
        {
            // 长度不足时返回原字符串或全部替换为*
            return input.Length == 1 ? "*" : new string('*', input.Length);
        }

        int maskLength = input.Length - 2;
        return $"{input[0]}{new string('*', maskLength)}{input[input.Length - 1]}";
    }
}
