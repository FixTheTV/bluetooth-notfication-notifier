using System.Text.Json;
using System.Text.Json.Serialization;

namespace BluetoothSMSNotifier.Models;

public sealed class NotificationPayload
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public string AppName { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    public string ToJson() => JsonSerializer.Serialize(this, NotificationPayloadJsonContext.Default.NotificationPayload);

    public static NotificationPayload? FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(json, NotificationPayloadJsonContext.Default.NotificationPayload);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true,
    WriteIndented = false)]
[JsonSerializable(typeof(NotificationPayload))]
internal sealed partial class NotificationPayloadJsonContext : JsonSerializerContext;
