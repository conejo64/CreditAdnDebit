using IsoSwitch.Infrastructure.SwitchIso8583.Net;
using Microsoft.Extensions.Configuration;

namespace IsoSwitch.Tests.Net;

/// <summary>
/// Verifies SEC-04: TcpIsoClientOptions.UseTls defaults to true, so a config
/// section that doesn't set it explicitly still requires TLS on the ISO 8583
/// TCP channel. RED before the default flip: default was `false`.
/// </summary>
public class TcpIsoClientOptionsTests
{
    [Fact]
    public void DefaultConstruction_UseTlsIsTrue()
    {
        var options = new TcpIsoClientOptions();

        Assert.True(options.UseTls);
    }

    [Fact]
    public void Bind_SectionWithoutExplicitUseTls_ResolvesToTrue()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["IsoClient:Host"] = "acquirer.example.com",
                ["IsoClient:Port"] = "5000"
            })
            .Build();

        var options = new TcpIsoClientOptions();
        config.GetSection("IsoClient").Bind(options);

        Assert.True(options.UseTls);
    }
}
