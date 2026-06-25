using System.Text.Json.Serialization;

namespace CardVault.Application.Contracts;

public sealed record EventEnvelope(
    [property: JsonPropertyName("eventName")] string EventName,
    [property: JsonPropertyName("eventId")] string EventId,
    [property: JsonPropertyName("occurredOn")] DateTimeOffset OccurredOn,
    [property: JsonPropertyName("payload")] object Payload
);
