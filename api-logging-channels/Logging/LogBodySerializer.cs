using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace ApiLogging.Api.Logging;

/// <summary>
/// Serializes request/response models for the log, with [SensitiveData] masking,
/// cycle protection and truncation. Never throws: a body that cannot be serialized
/// must not fail the request it describes.
/// </summary>
internal static class LogBodySerializer
{
    private const string TruncatedMarker = "...[truncated]";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { MaskSensitiveProperties } }
    };

    public static string? Serialize(object? value, int maxLength)
    {
        if (value is null) return null;

        string json;
        try
        {
            json = value as string ?? JsonSerializer.Serialize(value, value.GetType(), Options);
        }
        catch (Exception ex) when (ex is NotSupportedException or JsonException or InvalidOperationException)
        {
            return $"<unserializable {value.GetType().Name}: {ex.GetType().Name}>";
        }

        return Truncate(json, maxLength);
    }

    public static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength
            ? value
            : string.Concat(value.AsSpan(0, maxLength), TruncatedMarker);

    private static void MaskSensitiveProperties(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object) return;

        foreach (var property in typeInfo.Properties)
        {
            if (property.AttributeProvider?.IsDefined(typeof(SensitiveDataAttribute), inherit: true) != true)
                continue;

            // A converter per property type keeps the JSON valid whatever the type is.
            var converterType = typeof(MaskingConverter<>).MakeGenericType(property.PropertyType);
            property.CustomConverter = (JsonConverter)Activator.CreateInstance(converterType)!;
        }
    }

    private sealed class MaskingConverter<T> : JsonConverter<T>
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException("Masked values are write-only.");

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            writer.WriteStringValue("***");
    }
}
