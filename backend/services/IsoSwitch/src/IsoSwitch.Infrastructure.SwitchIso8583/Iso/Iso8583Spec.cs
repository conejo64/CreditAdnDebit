namespace IsoSwitch.Infrastructure.SwitchIso8583.Iso;

public enum IsoFieldDataType
{
    N,    // numeric
    AN,   // alphanumeric
    ANS,  // alphanumeric + special
    HEX   // hex-encoded string (even length)
}

/// <summary>
/// Minimal ISO8583 field specs used by the demo codec. This is NOT a complete spec,
/// but it supports common fixed/LLVAR/LLLVAR fields with validation and padding.
/// </summary>
public static class Iso8583Spec
{
    public sealed record FieldSpec(
        int Field,
        int? FixedLength,
        int? MaxLength,
        bool Variable,
        int LenDigits,              // 2 (LLVAR) / 3 (LLLVAR)
        IsoFieldDataType DataType,
        bool PadLeftNumeric = true  // numeric fixed fields are left-zero padded
    );

    public static readonly IReadOnlyDictionary<int, FieldSpec> Fields = new Dictionary<int, FieldSpec>
    {
        // Common financial fields
        { 2,  new FieldSpec(2,  null, 30,  true,  2, IsoFieldDataType.ANS) },       // PAN or Token LLVAR (demo; allows alphanumeric tokens)
        { 3,  new FieldSpec(3,  6,    6,   false, 0, IsoFieldDataType.N) },        // Processing code
        { 4,  new FieldSpec(4,  12,   12,  false, 0, IsoFieldDataType.N) },        // Amount, transaction
        { 7,  new FieldSpec(7,  10,   10,  false, 0, IsoFieldDataType.N) },        // Transmission datetime MMDDhhmmss
        { 11, new FieldSpec(11, 6,    6,   false, 0, IsoFieldDataType.N) },        // STAN
        { 12, new FieldSpec(12, 6,    6,   false, 0, IsoFieldDataType.N) },        // Local time hhmmss
        { 13, new FieldSpec(13, 4,    4,   false, 0, IsoFieldDataType.N) },        // Local date MMDD
        { 14, new FieldSpec(14, 4,    4,   false, 0, IsoFieldDataType.N) },        // Expiry YYMM
        { 22, new FieldSpec(22, 3,    3,   false, 0, IsoFieldDataType.N) },        // POS Entry Mode
        { 25, new FieldSpec(25, 2,    2,   false, 0, IsoFieldDataType.N) },        // POS Condition Code

        { 35, new FieldSpec(35, null, 37,  true,  2, IsoFieldDataType.ANS) },      // Track 2 Data LLVAR (digits + separators)
        { 37, new FieldSpec(37, 12,   12,  false, 0, IsoFieldDataType.AN, false) },// RRN
        { 38, new FieldSpec(38, 6,    6,   false, 0, IsoFieldDataType.AN, false) },// Authorization ID response
        { 39, new FieldSpec(39, 2,    2,   false, 0, IsoFieldDataType.AN, false) },// Response code
        { 41, new FieldSpec(41, 8,    8,   false, 0, IsoFieldDataType.AN, false) },// Terminal ID
        { 42, new FieldSpec(42, 15,   15,  false, 0, IsoFieldDataType.AN, false) },// Merchant ID
        { 49, new FieldSpec(49, 3,    3,   false, 0, IsoFieldDataType.AN, false) },// Currency

        { 52, new FieldSpec(52, 16,   16,  false, 0, IsoFieldDataType.HEX, false) },// PIN block (hex placeholder)
        { 54, new FieldSpec(54, null, 120, true,  3, IsoFieldDataType.ANS) },      // Additional amounts LLLVAR
        { 55, new FieldSpec(55, null, 999, true,  3, IsoFieldDataType.HEX, false) },// EMV TLV LLLVAR (hex string)

        // Network / secondary bitmap fields
        { 64,  new FieldSpec(64, 16,  16,  false, 0, IsoFieldDataType.HEX, false) }, // MAC primary
        { 70,  new FieldSpec(70, 3,   3,   false, 0, IsoFieldDataType.N) },          // Network management info code
        { 90,  new FieldSpec(90, 42,  42,  false, 0, IsoFieldDataType.N) },          // Original data elements
        { 102, new FieldSpec(102,null,28,  true,  2, IsoFieldDataType.AN, false) },  // Account ID 1 LLVAR
        { 103, new FieldSpec(103,null,28,  true,  2, IsoFieldDataType.AN, false) },  // Account ID 2 LLVAR
        { 120, new FieldSpec(120,null,999, true,  3, IsoFieldDataType.ANS) },        // Additional data LLLVAR

        // Private fields (commonly used by networks/acquirers; keep flexible)
        { 60, new FieldSpec(60, null, 999, true,  3, IsoFieldDataType.ANS) },
        { 61, new FieldSpec(61, null, 999, true,  3, IsoFieldDataType.ANS) },
        { 62, new FieldSpec(62, null, 999, true,  3, IsoFieldDataType.ANS) },

        { 128, new FieldSpec(128,16,  16,  false, 0, IsoFieldDataType.HEX, false) }, // MAC secondary
    };
}