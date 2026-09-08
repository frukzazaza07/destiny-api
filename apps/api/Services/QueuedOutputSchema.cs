using System.Text.Json;
using TarotDestiny.Api.Contracts;

namespace TarotDestiny.Api.Services;

internal static class QueuedOutputSchema
{
    public static void Validate(string body, InterpretationPayload payload)
    {
        using var envelope = JsonDocument.Parse(body);
        var content = envelope.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        using var reading = JsonDocument.Parse(content ?? throw new JsonException());
        var format = JsonSerializer.SerializeToElement(LlmClient.BuildReadingResponseFormat(payload));
        Check(reading.RootElement, format.GetProperty("json_schema").GetProperty("schema"));
    }
    private static void Check(JsonElement value, JsonElement schema)
    {
        switch (schema.GetProperty("type").GetString())
        {
            case "object":
                if (value.ValueKind != JsonValueKind.Object) throw new JsonException();
                var properties = schema.GetProperty("properties");
                var seen = new HashSet<string>();
                foreach (var field in value.EnumerateObject())
                {
                    if (!seen.Add(field.Name) || !properties.TryGetProperty(field.Name, out var child)) throw new JsonException();
                    Check(field.Value, child);
                }
                foreach (var required in schema.GetProperty("required").EnumerateArray())
                    if (!seen.Contains(required.GetString()!)) throw new JsonException();
                break;
            case "array":
                if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() < schema.GetProperty("minItems").GetInt32() ||
                    value.GetArrayLength() > schema.GetProperty("maxItems").GetInt32()) throw new JsonException();
                foreach (var item in value.EnumerateArray()) Check(item, schema.GetProperty("items"));
                break;
            case "string":
                if (value.ValueKind != JsonValueKind.String) throw new JsonException();
                var text = value.GetString()!;
                if (schema.TryGetProperty("minLength", out var min) && text.Length < min.GetInt32() ||
                    schema.TryGetProperty("maxLength", out var max) && text.Length > max.GetInt32()) throw new JsonException();
                if (schema.TryGetProperty("enum", out var allowed) && !allowed.EnumerateArray().Any(x => x.GetString() == text)) throw new JsonException();
                break;
            default: throw new JsonException();
        }
    }
}
