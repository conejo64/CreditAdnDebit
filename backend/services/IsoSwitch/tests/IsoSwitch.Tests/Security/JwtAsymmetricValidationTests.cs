using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using BuildingBlocks.Security;
using FluentAssertions;
using IsoSwitch.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;

namespace IsoSwitch.Tests.Security;

/// <summary>
/// G1-T5: IsoSwitch validates CardVault-issued tokens with the ES256 public key only. A token
/// signed with the retired shared HS256 secret, or with any other private key, is rejected,
/// and the service refuses to start without a public key.
/// </summary>
public sealed class JwtAsymmetricValidationTests : IClassFixture<IsoSwitchWebApplicationFactory>
{
    private const string ProtectedEndpoint = "/api/transactions?take=1";

    private readonly IsoSwitchWebApplicationFactory _factory;

    public JwtAsymmetricValidationTests(IsoSwitchWebApplicationFactory factory) => _factory = factory;

    private static string Mint(SigningCredentials credentials) => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
        issuer: IsoSwitchWebApplicationFactory.Issuer,
        audience: IsoSwitchWebApplicationFactory.Audience,
        claims: [new Claim(ClaimTypes.NameIdentifier, "asymmetric-test-user"), new Claim(ClaimTypes.Role, "Admin")],
        notBefore: DateTime.UtcNow.AddMinutes(-1),
        expires: DateTime.UtcNow.AddMinutes(10),
        signingCredentials: credentials));

    private async Task<HttpStatusCode> CallProtectedEndpointAsync(string token)
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, ProtectedEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (await client.SendAsync(request)).StatusCode;
    }

    [Fact]
    public async Task TokenSignedWithTheIssuerPrivateKey_IsAccepted()
    {
        var token = Mint(IsoSwitchWebApplicationFactory.CreateSigningCredentials());

        new JwtSecurityTokenHandler().ReadJwtToken(token).Header.Kid.Should().NotBeNullOrWhiteSpace();
        (await CallProtectedEndpointAsync(token)).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Hs256TokenSignedWithTheRetiredSharedSecret_IsRejected()
    {
        var hmac = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("TestJwtSigningKeyForIsoSwitchTests32Plus"));
        var token = Mint(new SigningCredentials(hmac, SecurityAlgorithms.HmacSha256));

        (await CallProtectedEndpointAsync(token)).Should().Be(HttpStatusCode.Unauthorized,
            "no symmetric secret may mint a token IsoSwitch accepts");
    }

    [Fact]
    public async Task Es256TokenSignedWithADifferentPrivateKey_IsRejected()
    {
        var token = Mint(IsoSwitchWebApplicationFactory.CreateSigningCredentials(EcdsaKeyMaterial.GenerateKeyPair().PrivateKeyPem));

        (await CallProtectedEndpointAsync(token)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public void Startup_WithoutAPublicKey_IsRefused()
    {
        using var factory = new IsoSwitchWebApplicationFactory()
            .WithWebHostBuilder(b => b.UseSetting("Jwt:PublicKeyPem", string.Empty));

        Record.Exception(() => factory.CreateClient()).Should().NotBeNull();
    }

    [Fact]
    public void Startup_WithAPrivateKeyInThePublicSlot_IsRefused()
    {
        using var factory = new IsoSwitchWebApplicationFactory()
            .WithWebHostBuilder(b => b.UseSetting("Jwt:PublicKeyPem", IsoSwitchWebApplicationFactory.PrivateKeyPem));

        Record.Exception(() => factory.CreateClient()).Should().NotBeNull();
    }

    [Fact]
    public void Startup_WithTheRetiredSymmetricKeyStillConfigured_IsRefused()
    {
        using var factory = new IsoSwitchWebApplicationFactory()
            .WithWebHostBuilder(b => b.UseSetting("Jwt:SigningKey", "TestJwtSigningKeyForIsoSwitchTests32Plus"));

        Record.Exception(() => factory.CreateClient()).Should().NotBeNull();
    }
}
