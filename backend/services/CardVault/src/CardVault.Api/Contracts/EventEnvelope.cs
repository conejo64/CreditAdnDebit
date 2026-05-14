using System.Text.Json.Serialization;

namespace CardVault.Api.Contracts;

public sealed record EventEnvelope(
    [property: JsonPropertyName("eventName")] string EventName,
    [property: JsonPropertyName("eventId")] string EventId,
    [property: JsonPropertyName("occurredOn")] DateTimeOffset OccurredOn,
    [property: JsonPropertyName("payload")] object Payload
);
