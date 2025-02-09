using Newtonsoft.Json;
using System;

public class MaskMiddleConverter : JsonConverter
{
    public override bool CanConvert(Type objectType)
    {
        return objectType == typeof(string);
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        // 反序列化时不处理，直接返回原始值（假设不需要反向操作）
        return reader.Value;
    }

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        if (value == null)
        {
            writer.WriteNull();
            return;
        }

        string str = value.ToString();
        string masked = MaskString(str);
        writer.WriteValue(masked);
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
