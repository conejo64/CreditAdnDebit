using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using BuildingBlocks.Security;
using CardVault.Api.Security;
using CardVault.Infrastructure.Identity.Auth;
using CardVault.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace CardVault.Tests.Security;

/// <summary>
/// G1-T5: CardVault signs every token it mints (session, open banking, MFA pending) with an
/// ES256 private key and publishes only the public half. Anyone holding the public key can
/// verify a token; nobody can forge one — including a party holding the retired HS256 secret.
/// </summary>
public sealed class JwtAsymmetricSigningHttpTests : IClassFixture<CardVaultWebApplicationFactory>
{
    // Any endpoint behind [Authorize]; CanViewAudit admits the Admin role.
    private const string ProtectedEndpoint = "/health/vault";
    private const string RetiredSharedSecret = "TestSigningKeyForCardVaultIntegrationTests";

    private readonly CardVaultWebApplicationFactory _factory;

    public JwtAsymmetricSigningHttpTests(CardVaultWebApplicationFactory factory) => _factory = factory;

    private TokenService ResolveTokenService(IServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<TokenService>();

    private static JwtSecurityToken Parse(string token) => new JwtSecurityTokenHandler().ReadJwtToken(token);

    private static ECDsaSecurityKey PublicKey(string publicKeyPem)
    {
        var ecdsa = EcdsaKeyMaterial.ImportPublicKey(publicKeyPem);
        return new ECDsaSecurityKey(ecdsa) { KeyId = EcdsaKeyMaterial.ComputeKeyId(ecdsa) };
    }

    private static TokenValidationParameters ValidationParameters(SecurityKey key, string audience) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = CardVaultWebApplicationFactory.Issuer,
        ValidateAudience = true,
        ValidAudience = audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = key,
        ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
        ValidateLifetime = true
    };

    [Fact]
    public void IssuedTokens_AreEs256_CarryAStableKid_AndVerifyWithThePublicKeyOnly()
    {
        using var scope = _factory.Services.CreateScope();
        var tokens = ResolveTokenService(scope);

        var first = tokens.CreateOpenBankingAccessToken("client-a", "Client A", ["ob:balances"]);
        var second = tokens.CreateOpenBankingAccessToken("client-b", "Client B", ["ob:transactions"]);

        var header = Parse(first).Header;
        header.Alg.Should().Be(SecurityAlgorithms.EcdsaSha256);
        header.Kid.Should().NotBeNullOrWhiteSpace();
        Parse(second).Header.Kid.Should().Be(header.Kid, "the kid is derived from the public key, not per token");

        var expectedKid = EcdsaKeyMaterial.ComputeKeyId(EcdsaKeyMaterial.ImportPublicKey(CardVaultWebApplicationFactory.PublicKeyPem));
        header.Kid.Should().Be(expectedKid, "kid is the RFC 7638 thumbprint of the configured public key");

        var handler = new JwtSecurityTokenHandler();
        handler.ValidateToken(first, ValidationParameters(PublicKey(CardVaultWebApplicationFactory.PublicKeyPem), CardVaultWebApplicationFactory.Audience), out _);

        var otherPublicKey = PublicKey(EcdsaKeyMaterial.GenerateKeyPair().PublicKeyPem);
        var withWrongKey = () => handler.ValidateToken(first, ValidationParameters(otherPublicKey, CardVaultWebApplicationFactory.Audience), out _);
        withWrongKey.Should().Throw<SecurityTokenException>();
    }

    [Fact]
    public void MfaPendingToken_IsEs256_AndRoundTripsThroughTheValidator()
    {
        using var scope = _factory.Services.CreateScope();
        var tokens = ResolveTokenService(scope);
        var user = new AppUser { Id = Guid.NewGuid().ToString(), Email = "pending@demo.com", UserName = "pending@demo.com" };

        var issued = tokens.CreateMfaPendingToken(user);

        Parse(issued.Token).Header.Alg.Should().Be(SecurityAlgorithms.EcdsaSha256);
        var claims = tokens.ValidateMfaPendingToken(issued.Token);
        claims.Should().NotBeNull();
        claims!.UserId.Should().Be(user.Id);
        claims.Jti.Should().Be(issued.Jti);
    }

    [Fact]
    public async Task ProtectedEndpoint_AcceptsATokenSignedWithTheIssuerKey()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _factory.GenerateJwt(["Admin"]));

        var response = await client.GetAsync(ProtectedEndpoint);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ProtectedEndpoint_RejectsAnHs256TokenSignedWithTheRetiredSharedSecret()
    {
        var client = _factory.CreateClient();
        var hmac = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(RetiredSharedSecret));
        var forged = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: CardVaultWebApplicationFactory.Issuer,
            audience: CardVaultWebApplicationFactory.Audience,
            claims: [new Claim(ClaimTypes.Role, "Admin")],
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(hmac, SecurityAlgorithms.HmacSha256)));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", forged);

        var response = await client.GetAsync(ProtectedEndpoint);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "a symmetric secret can no longer mint tokens any service accepts");
    }

    [Fact]
    public async Task ProtectedEndpoint_RejectsAnEs256TokenSignedWithADifferentPrivateKey()
    {
        var client = _factory.CreateClient();
        var strangerCredentials = CardVaultWebApplicationFactory.CreateSigningCredentials(EcdsaKeyMaterial.GenerateKeyPair().PrivateKeyPem);
        var forged = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: CardVaultWebApplicationFactory.Issuer,
            audience: CardVaultWebApplicationFactory.Audience,
            claims: [new Claim(ClaimTypes.Role, "Admin")],
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: strangerCredentials));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", forged);

        var response = await client.GetAsync(ProtectedEndpoint);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Jwks_IsAnonymous_Cacheable_AndExposesOnlyThePublicKey()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/.well-known/jwks.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.Public.Should().BeTrue();
        response.Headers.CacheControl.MaxAge.Should().NotBeNull().And.BeGreaterThan(TimeSpan.Zero);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var keys = body.RootElement.GetProperty("keys");
        keys.GetArrayLength().Should().Be(1);
        var jwk = keys[0];
        jwk.GetProperty("kty").GetString().Should().Be("EC");
        jwk.GetProperty("crv").GetString().Should().Be("P-256");
        jwk.GetProperty("alg").GetString().Should().Be("ES256");
        jwk.GetProperty("use").GetString().Should().Be("sig");
        jwk.GetProperty("x").GetString().Should().NotBeNullOrWhiteSpace();
        jwk.GetProperty("y").GetString().Should().NotBeNullOrWhiteSpace();
        jwk.TryGetProperty("d", out _).Should().BeFalse("the private scalar must never leave the issuer");

        using var scope = _factory.Services.CreateScope();
        var tokenKid = Parse(ResolveTokenService(scope).CreateOpenBankingAccessToken("c", "C", ["ob:balances"])).Header.Kid;
        jwk.GetProperty("kid").GetString().Should().Be(tokenKid, "validators match tokens to the JWKS entry by kid");
    }
}
