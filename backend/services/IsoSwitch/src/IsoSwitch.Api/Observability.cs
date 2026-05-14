using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace IsoSwitch.Api;

public static class Observability
{
    public static readonly ActivitySource ActivitySource = new("IsoSwitch");
    public static readonly Meter Meter = new("IsoSwitch.Metrics", "1.0.0");

    public static readonly Counter<long> IsoRequestsTotal =
        Meter.CreateCounter<long>("iso_requests_total", description: "Total ISO requests handled by IsoSwitch");

    public static readonly Histogram<double> IsoRequestDurationMs =
        Meter.CreateHistogram<double>("iso_request_duration_ms", unit: "ms", description: "ISO request duration");

    public static readonly Counter<long> IsoResponseCodesTotal =
        Meter.CreateCounter<long>("iso_response_codes_total", description: "ISO response codes count");
}