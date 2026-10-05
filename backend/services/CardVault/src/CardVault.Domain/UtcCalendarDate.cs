namespace CardVault.Domain;

/// <summary>
/// Normalizes a caller-supplied <see cref="DateTime"/> to <see cref="DateTimeKind.Utc"/> at a service
/// boundary, so it can be persisted to PostgreSQL <c>timestamp with time zone</c> (Npgsql rejects
/// <see cref="DateTimeKind.Unspecified"/>).
/// <list type="bullet">
///   <item><see cref="DateTimeKind.Unspecified"/> is treated as a calendar date: the wall-clock value
///   is kept and relabelled UTC. A JSON body with <c>"2025-01-31"</c> means the 31st of January,
///   not an instant to shift by the server's offset.</item>
///   <item><see cref="DateTimeKind.Local"/> is an instant and is converted with
///   <see cref="DateTime.ToUniversalTime"/>.</item>
///   <item><see cref="DateTimeKind.Utc"/> is returned unchanged.</item>
/// </list>
/// </summary>
public static class UtcCalendarDate
{
    public static DateTime Normalize(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    public static DateTime? Normalize(DateTime? value) => value.HasValue ? Normalize(value.Value) : null;
}
