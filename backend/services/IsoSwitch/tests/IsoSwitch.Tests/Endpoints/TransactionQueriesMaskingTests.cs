using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using IsoSwitch.Infrastructure.Persistence;
using IsoSwitch.Infrastructure.Persistence.IsoAudit;
using IsoSwitch.Infrastructure.Persistence.Transactions;
using IsoSwitch.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace IsoSwitch.Tests.Endpoints;

/// <summary>
/// Rows written before masking was enforced may still hold clear card data. The read endpoints
/// must therefore never return a stored JSON blob as-is: they re-mask it defensively so an
/// operator keeps the diagnostic value without the browser ever receiving a PAN or track 2.
/// </summary>
public sealed class TransactionQueriesMaskingTests : IClassFixture<IsoSwitchWebApplicationFactory>
{
    private const string Pan = "4539578763621486";
    private const string Track2 = "4539578763621486=29121011234567890";
    private const string PinBlock = "A1B2C3D4E5F60718";

    private readonly IsoSwitchWebApplicationFactory _factory;

    public TransactionQueriesMaskingTests(IsoSwitchWebApplicationFactory factory) => _factory = factory;

    private static string CreateAccessToken()
    {
        var key = new SymmetricSecurityKey(
            System.Text.Encoding.UTF8.GetBytes(IsoSwitchWebApplicationFactory.TestJwtSigningKey));

        var token = new JwtSecurityToken(
            issuer: IsoSwitchWebApplicationFactory.Issuer,
            audience: IsoSwitchWebApplicationFactory.Audience,
            claims: new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "masking-test-user"),
                new Claim(ClaimTypes.Role, "Admin")
            },
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private HttpClient CreateAuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateAccessToken());
        return client;
    }

    private async Task<string> SeedLegacyRowsAsync()
    {
        var traceId = $"legacy-{Guid.NewGuid():N}";

        // Legacy shapes: Authorize wrote {mti, fields}, Capture/Reversal wrote the bare field map.
        var legacyRequest = JsonSerializer.Serialize(new
        {
            mti = "0100",
            fields = new Dictionary<string, string>
            {
                ["2"] = Pan, ["4"] = "000000010050", ["35"] = Track2, ["52"] = PinBlock, ["49"] = "840"
            }
        });
        var legacyResponse = JsonSerializer.Serialize(new Dictionary<string, string> { ["2"] = Pan, ["39"] = "00" });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IsoSwitchDbContext>();
        db.Transactions.Add(new TransactionEntity
        {
            TraceId = traceId,
            CorrelationId = traceId,
            RequestMti = "0100",
            Stan = "123456",
            TxType = "AUTH",
            Status = "COMPLETED",
            Decision = "APPROVED",
            ResponseCode = "00",
            ConnectorId = "SIMULATOR",
            RequestJson = legacyRequest,
            ResponseJson = legacyResponse,
            Amount12 = "000000010050",
            Currency = "840",
            TerminalId = "T001",
            MerchantId = "M001",
            InDoubt = true,
            ReversalStatus = "PENDING"
        });
        db.IsoMessageLogs.Add(new IsoMessageLogEntity
        {
            TraceId = traceId,
            Direction = "OUT",
            Mti = "0100",
            FieldsJson = legacyRequest
        });
        await db.SaveChangesAsync();
        return traceId;
    }

    [Fact]
    public async Task GetTransactionByTraceId_ReturnsADtoWithNoClearCardData_EvenForALegacyRow()
    {
        var traceId = await SeedLegacyRowsAsync();
        var client = CreateAuthenticatedClient();

        var response = await client.GetAsync($"/api/transactions/{traceId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(Pan).And.NotContain(Track2).And.NotContain(PinBlock).And.NotContain("=2912");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        root.GetProperty("traceId").GetString().Should().Be(traceId);
        root.GetProperty("requestMti").GetString().Should().Be("0100");
        root.GetProperty("responseCode").GetString().Should().Be("00");
        root.GetProperty("amount12").GetString().Should().Be("000000010050");
        root.GetProperty("maskedPan").GetString().Should().Be("453957******1486");
        root.GetProperty("requestJson").GetString().Should().Contain("453957******1486").And.Contain("000000010050");
        root.GetProperty("responseJson").GetString().Should().Contain("\"39\":\"00\"");

        // The raw entity must not be what is serialized: no EF-only internals.
        root.TryGetProperty("id", out _).Should().BeFalse("the endpoint returns a DTO, not the entity");
    }

    [Fact]
    public async Task GetIsoLogsByTraceId_ReMasksLegacyFieldsJson()
    {
        var traceId = await SeedLegacyRowsAsync();
        var client = CreateAuthenticatedClient();

        var response = await client.GetAsync($"/api/iso/logs/{traceId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(Pan).And.NotContain(Track2).And.NotContain(PinBlock);
        body.Should().Contain("453957******1486");
    }

    [Fact]
    public async Task PostIsoReconcile_ReturnsNoClearCardData_ForInDoubtLegacyRows()
    {
        var traceId = await SeedLegacyRowsAsync();
        var client = CreateAuthenticatedClient();

        var response = await client.PostAsync("/api/iso/reconcile", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(traceId);
        body.Should().NotContain(Pan).And.NotContain(Track2).And.NotContain(PinBlock);
    }
}
