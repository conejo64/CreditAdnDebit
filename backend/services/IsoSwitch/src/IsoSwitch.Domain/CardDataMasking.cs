using System.Text.Json;
using System.Text.Json.Nodes;

namespace IsoSwitch.Domain;

/// <summary>
/// Pure masking rules for card data that must never be stored, logged or published in clear.
///
/// The switch needs the real ISO 8583 fields on the wire, but every copy it keeps (the
/// <c>Transactions</c> row, the ISO audit log, Kafka audit events, API read models) goes through
/// this class first. The rules follow PCI DSS: a PAN may show at most its first six and last four
/// digits; track 2, the PIN block, EMV data and MACs are never retained after authorization.
///
/// <list type="bullet">
///   <item><description>DE2 (PAN): <see cref="MaskPan"/> — first 6 + <c>*</c> + last 4; shorter inputs are fully masked.</description></item>
///   <item><description>DE35 (track 2): <see cref="MaskTrack2"/> — the leading PAN digit run is masked like DE2, every other character (separator, expiry, service code, discretionary data, sentinels) becomes <c>*</c>.</description></item>
///   <item><description>DE52 (PIN block), DE55 (EMV), DE64 and DE128 (MAC): replaced by <see cref="Redacted"/>.</description></item>
/// </list>
/// </summary>
public static class CardDataMasking
{
    /// <summary>Value written in place of a field whose content must not survive at all.</summary>
    public const string Redacted = "***";

    /// <summary>Character used for masked positions.</summary>
    public const char MaskChar = '*';

    /// <summary>
    /// A PAN is 13 to 19 digits. Anything shorter cannot keep 6 + 4 digits without exposing most of
    /// it, so it is masked entirely.
    /// </summary>
    public const int MinimumMaskablePanLength = 13;

    private const int VisiblePrefixLength = 6;
    private const int VisibleSuffixLength = 4;

    /// <summary>Fields whose value is masked while keeping the PCI-permitted digits (DE2, DE35).</summary>
    public static readonly IReadOnlySet<int> MaskedFields = new HashSet<int> { 2, 35 };

    /// <summary>Fields whose value is replaced by <see cref="Redacted"/> (DE52, DE55, DE64, DE128).</summary>
    public static readonly IReadOnlySet<int> RedactedFields = new HashSet<int> { 52, 55, 64, 128 };

    /// <summary>Every field this class rewrites: the union of <see cref="MaskedFields"/> and <see cref="RedactedFields"/>.</summary>
    public static readonly IReadOnlySet<int> SensitiveFields = new HashSet<int>(MaskedFields.Concat(RedactedFields));

    /// <summary>
    /// Masks a PAN as <c>453957******1486</c>: first six and last four characters kept, the rest
    /// replaced by <see cref="MaskChar"/>. Inputs shorter than <see cref="MinimumMaskablePanLength"/>
    /// are replaced by <see cref="MaskChar"/> of the same length; null or empty yields an empty string.
    /// </summary>
    public static string MaskPan(string? pan)
    {
        if (string.IsNullOrEmpty(pan)) return string.Empty;
        if (pan.Length < MinimumMaskablePanLength) return new string(MaskChar, pan.Length);

        var hidden = pan.Length - VisiblePrefixLength - VisibleSuffixLength;
        return string.Concat(pan.AsSpan(0, VisiblePrefixLength), new string(MaskChar, hidden), pan.AsSpan(pan.Length - VisibleSuffixLength));
    }

    /// <summary>
    /// Masks track 2 data (<c>PAN=YYMMSSSDDD…</c>, optionally wrapped in <c>;</c> and <c>?</c>
    /// sentinels). The leading digit run is the PAN and is masked with <see cref="MaskPan"/>; every
    /// other character, including the separator and the expiry date, becomes <see cref="MaskChar"/>.
    /// Length is preserved. Example: <c>4539578763621486=29121011234567890</c> becomes
    /// <c>453957******1486******************</c>.
    /// </summary>
    public static string MaskTrack2(string? track2)
    {
        if (string.IsNullOrEmpty(track2)) return string.Empty;

        var output = new char[track2.Length];
        Array.Fill(output, MaskChar);

        var panStart = track2[0] == ';' ? 1 : 0;
        var panEnd = panStart;
        while (panEnd < track2.Length && char.IsAsciiDigit(track2[panEnd])) panEnd++;

        var maskedPan = MaskPan(track2.Substring(panStart, panEnd - panStart));
        maskedPan.CopyTo(0, output, panStart, maskedPan.Length);

        return new string(output);
    }

    /// <summary>
    /// Returns a copy of <paramref name="fields"/> safe to persist or publish: DE2 and DE35 masked,
    /// <see cref="RedactedFields"/> replaced by <see cref="Redacted"/>, every other field unchanged.
    /// The input dictionary is never modified, so the outbound message keeps its real values.
    /// </summary>
    public static Dictionary<int, string> MaskFields(IReadOnlyDictionary<int, string> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var result = new Dictionary<int, string>(fields.Count);
        foreach (var (field, value) in fields)
        {
            result[field] = MaskFieldValue(field, value);
        }
        return result;
    }

    /// <summary>Masks a single field value according to its data element number.</summary>
    public static string MaskFieldValue(int field, string? value)
    {
        if (RedactedFields.Contains(field)) return Redacted;
        if (field == 2) return MaskPan(value);
        if (field == 35) return MaskTrack2(value);
        return value ?? string.Empty;
    }

    /// <summary>
    /// Defensive re-masking of a JSON document that was persisted before masking was enforced, or
    /// whose provenance is unknown. Every object property, at any depth, whose name is a sensitive
    /// data element number (<c>"2"</c>, <c>"35"</c>, <c>"52"</c>, …) is rewritten with
    /// <see cref="MaskFieldValue"/>; non-string values of such properties are replaced by
    /// <see cref="Redacted"/>. Both historical shapes are covered: <c>{"mti":…,"fields":{"2":…}}</c>
    /// and the bare <c>{"2":…}</c> map. Unparseable input fails closed and returns <see cref="Redacted"/>;
    /// null returns null.
    /// </summary>
    public static string? MaskFieldsJson(string? json)
    {
        if (json is null) return null;
        if (string.IsNullOrWhiteSpace(json)) return json;

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return Redacted;
        }

        if (root is null) return json;

        MaskNode(root, firstMaskedPan: null);
        return root.ToJsonString();
    }

    /// <summary>
    /// Extracts the masked PAN (DE2) from a persisted JSON document, re-masking it first so the
    /// result is safe regardless of how the row was written. Returns null when no DE2 is present.
    /// </summary>
    public static string? ExtractMaskedPan(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        if (root is null) return null;

        var found = new string?[1];
        MaskNode(root, found);
        return found[0];
    }

    /// <summary>
    /// Luhn (mod 10) check over the digits of <paramref name="pan"/>. Non-digit characters are
    /// ignored; fewer than 12 digits is rejected.
    /// </summary>
    public static bool IsValidLuhn(string? pan)
    {
        if (string.IsNullOrWhiteSpace(pan)) return false;
        var digits = pan.Where(char.IsDigit).Select(c => c - '0').ToArray();
        if (digits.Length < 12) return false;

        var sum = 0;
        var alternate = false;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var d = digits[i];
            if (alternate)
            {
                d *= 2;
                if (d > 9) d -= 9;
            }
            sum += d;
            alternate = !alternate;
        }
        return sum % 10 == 0;
    }

    private static void MaskNode(JsonNode node, string?[]? firstMaskedPan)
    {
        switch (node)
        {
            case JsonObject obj:
                // Collect first: rewriting while enumerating invalidates the enumerator.
                var sensitive = new List<(string Name, int Field, JsonNode? Value)>();
                foreach (var (name, value) in obj)
                {
                    if (int.TryParse(name, out var field) && SensitiveFields.Contains(field))
                    {
                        sensitive.Add((name, field, value));
                    }
                    else if (value is not null)
                    {
                        MaskNode(value, firstMaskedPan);
                    }
                }

                foreach (var (name, field, value) in sensitive)
                {
                    string masked;
                    if (value is JsonValue jv && jv.TryGetValue<string>(out var s))
                    {
                        masked = MaskFieldValue(field, s);
                    }
                    else
                    {
                        masked = Redacted;
                    }

                    obj[name] = masked;
                    if (field == 2 && firstMaskedPan is not null && firstMaskedPan[0] is null)
                    {
                        firstMaskedPan[0] = masked;
                    }
                }
                break;

            case JsonArray array:
                foreach (var item in array)
                {
                    if (item is not null) MaskNode(item, firstMaskedPan);
                }
                break;
        }
    }
}
