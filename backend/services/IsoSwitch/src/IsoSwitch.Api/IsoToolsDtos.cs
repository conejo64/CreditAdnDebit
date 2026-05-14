using System.Collections.Generic;

namespace IsoSwitch.Api;

public sealed record IsoParseRequest(string? Ascii, string? Base64Payload);

public sealed record IsoBuildRequest(string Mti, Dictionary<int, string>? Fields);