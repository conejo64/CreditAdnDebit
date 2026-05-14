using System.Text.Json;

namespace IsoSwitch.Api.Iso8583;

public static class IsoResponseBuilder
{
    public sealed record SwitchAuthResponse(
        string Network,
        string Mti,
        string Stan,
        string Rrn,
        decimal Amount,
        string ResponseCode,
        string? Reason,
        string? TerminalId,
        string? AcceptorId,
        string? Mcc,
        string Currency,
        string? TransmissionDateTime7,
        string? LocalTime12,
        string? LocalDate13
    );

    public static Iso8583MessageBinary Build0110Binary(SwitchAuthResponse r)
    {
        var now = DateTime.UtcNow;
        var de7 = string.IsNullOrWhiteSpace(r.TransmissionDateTime7) ? now.ToString("MMddHHmmss") : r.TransmissionDateTime7!;
        var de12 = string.IsNullOrWhiteSpace(r.LocalTime12) ? now.ToString("HHmmss") : r.LocalTime12!;
        var de13 = string.IsNullOrWhiteSpace(r.LocalDate13) ? now.ToString("MMdd") : r.LocalDate13!;

        var amount12 = ((long)Math.Round(Math.Abs(r.Amount) * 100m)).ToString(); // cents
        var authId = (r.ResponseCode == "00") ? "APPRV1" : "      ";

        var msg = new Iso8583MessageBinary("0110")
            .Set(3, "000000")
            .Set(4, amount12)
            .Set(7, de7)
            .Set(11, r.Stan)
            .Set(12, de12)
            .Set(13, de13)
            .Set(37, r.Rrn)
            .Set(38, authId)
            .Set(39, r.ResponseCode)
            .Set(49, r.Currency);

        if (!string.IsNullOrWhiteSpace(r.Mcc)) msg.Set(18, r.Mcc!);
        if (!string.IsNullOrWhiteSpace(r.TerminalId)) msg.Set(41, r.TerminalId!);
        if (!string.IsNullOrWhiteSpace(r.AcceptorId)) msg.Set(42, r.AcceptorId!);

        return msg;
    }

    public static Iso8583MessageBinary Build0210Binary(SwitchAuthResponse r)
    {
        var now = DateTime.UtcNow;
        var de7 = string.IsNullOrWhiteSpace(r.TransmissionDateTime7) ? now.ToString("MMddHHmmss") : r.TransmissionDateTime7!;
        var de12 = string.IsNullOrWhiteSpace(r.LocalTime12) ? now.ToString("HHmmss") : r.LocalTime12!;
        var de13 = string.IsNullOrWhiteSpace(r.LocalDate13) ? now.ToString("MMdd") : r.LocalDate13!;

        var amount12 = ((long)Math.Round(Math.Abs(r.Amount) * 100m)).ToString();

        var msg = new Iso8583MessageBinary("0210")
            .Set(3, "000000")
            .Set(4, amount12)
            .Set(7, de7)
            .Set(11, r.Stan)
            .Set(12, de12)
            .Set(13, de13)
            .Set(37, r.Rrn)
            .Set(39, r.ResponseCode)
            .Set(49, r.Currency);

        if (!string.IsNullOrWhiteSpace(r.Mcc)) msg.Set(18, r.Mcc!);
        if (!string.IsNullOrWhiteSpace(r.TerminalId)) msg.Set(41, r.TerminalId!);
        if (!string.IsNullOrWhiteSpace(r.AcceptorId)) msg.Set(42, r.AcceptorId!);

        return msg;
    }

    public static bool TryParseAuthResponseEnvelope(string json, out SwitchAuthResponse? res)
    {
        res = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("eventName", out var en)) return false;
            if (en.GetString() != "switch.v1.auth.response") return false;

            var payload = root.GetProperty("payload");
            res = new SwitchAuthResponse(
                Network: payload.GetProperty("network").GetString() ?? "Visa",
                Mti: payload.GetProperty("mti").GetString() ?? "0100",
                Stan: payload.GetProperty("stan").GetString() ?? "000000",
                Rrn: payload.GetProperty("rrn").GetString() ?? "000000000000",
                Amount: payload.GetProperty("amount").GetDecimal(),
                ResponseCode: payload.GetProperty("responseCode").GetString() ?? "05",
                Reason: payload.TryGetProperty("reason", out var rs) ? rs.GetString() : null,
                TerminalId: payload.TryGetProperty("terminalId", out var t) ? t.GetString() : null,
                AcceptorId: payload.TryGetProperty("acceptorId", out var a) ? a.GetString() : null,
                Mcc: payload.TryGetProperty("mcc", out var m) ? m.GetString() : null,
                Currency: payload.TryGetProperty("currency", out var c) ? c.GetString() ?? "840" : "840",
                TransmissionDateTime7: payload.TryGetProperty("de7", out var d7) ? d7.GetString() : null,
                LocalTime12: payload.TryGetProperty("de12", out var d12) ? d12.GetString() : null,
                LocalDate13: payload.TryGetProperty("de13", out var d13) ? d13.GetString() : null
            );
            return true;
        }
        catch
        {
            return false;
        }
    }
}
