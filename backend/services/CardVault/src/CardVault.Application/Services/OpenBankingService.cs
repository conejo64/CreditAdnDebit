using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using System.Diagnostics;
using CardVault.Application.Contracts;
using CardVault.Application.Ports;
using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Billing;
using CardVault.Infrastructure.Persistence.OpenBanking;
using Microsoft.EntityFrameworkCore;

namespace CardVault.Application.Services;

public sealed class OpenBankingService
{
    private static readonly string[] SupportedScopes = ["ob:balances", "ob:transactions"];

    private readonly CardVaultDbContext _db;
    private readonly IOpenBankingTokenIssuer _tokens;
    private readonly AuditService _audit;

    public OpenBankingService(CardVaultDbContext db, IOpenBankingTokenIssuer tokens, AuditService audit)
    {
        _db = db;
        _tokens = tokens;
        _audit = audit;
    }

    public async Task<OpenBankingClientResponse> CreateClientAsync(CreateOpenBankingClientRequest request, CancellationToken ct)
    {
        var scopes = NormalizeScopes(request.Scopes);
        if (scopes.Length == 0)
            throw new InvalidOperationException("At least one supported scope is required.");

        var clientId = $"ob_{Guid.NewGuid():N}"[..19];
        var clientSecret = GenerateClientSecret();

        var entity = new OpenBankingClientEntity
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            Name = request.Name.Trim(),
            SecretHash = HashSecret(clientSecret),
            AllowedScopes = string.Join(' ', scopes),
            Enabled = true,
            AllowAllAccounts = request.AllowAllAccounts,
            CreatedOn = DateTimeOffset.UtcNow,
            UpdatedOn = DateTimeOffset.UtcNow
        };

        _db.OpenBankingClients.Add(entity);
        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync("openbanking.client.created", new
        {
            clientId = entity.ClientId,
            name = entity.Name,
            scopes,
            allowAllAccounts = entity.AllowAllAccounts
        }, entity.ClientId, Activity.Current?.TraceId.ToString(), ct);

        return MapClient(entity, [], clientSecret);
    }

    public async Task<IReadOnlyList<OpenBankingClientResponse>> GetClientsAsync(CancellationToken ct)
    {
        var clients = await _db.OpenBankingClients
            .AsNoTracking()
            .Include(x => x.AccountAccesses)
            .OrderBy(x => x.Name)
            .ToListAsync(ct);

        return clients.Select(x => MapClient(x, x.AccountAccesses.Select(a => a.AccountId).ToArray(), null)).ToList();
    }

    public async Task<OpenBankingClientResponse?> GrantAccountAccessAsync(string clientId, Guid accountId, CancellationToken ct)
    {
        var client = await _db.OpenBankingClients
            .Include(x => x.AccountAccesses)
            .FirstOrDefaultAsync(x => x.ClientId == clientId, ct);

        if (client is null)
            return null;

        var accountExists = await _db.Accounts.AsNoTracking().AnyAsync(x => x.Id == accountId, ct);
        if (!accountExists)
            throw new KeyNotFoundException("Account not found.");

        if (!client.AccountAccesses.Any(x => x.AccountId == accountId))
        {
            client.AccountAccesses.Add(new OpenBankingClientAccountAccessEntity
            {
                Id = Guid.NewGuid(),
                ClientEntityId = client.Id,
                AccountId = accountId,
                CreatedOn = DateTimeOffset.UtcNow
            });
            client.UpdatedOn = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        await _audit.WriteAsync("openbanking.client.account-granted", new
        {
            clientId = client.ClientId,
            accountId
        }, client.ClientId, Activity.Current?.TraceId.ToString(), ct);

        return MapClient(client, client.AccountAccesses.Select(x => x.AccountId).ToArray(), null);
    }

    public async Task<OpenBankingTokenResponse?> IssueTokenAsync(OpenBankingTokenRequest request, string traceId, CancellationToken ct)
    {
        if (!string.Equals(request.GrantType, "client_credentials", StringComparison.OrdinalIgnoreCase))
            return null;

        var client = await _db.OpenBankingClients.FirstOrDefaultAsync(x => x.ClientId == request.ClientId, ct);
        if (client is null || !client.Enabled)
            return null;

        if (!VerifySecret(request.ClientSecret, client.SecretHash))
            return null;

        var allowedScopes = SplitScopes(client.AllowedScopes);
        var requestedScopes = string.IsNullOrWhiteSpace(request.Scope) ? allowedScopes : NormalizeScopes(request.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        if (requestedScopes.Length == 0 || requestedScopes.Except(allowedScopes, StringComparer.OrdinalIgnoreCase).Any())
            return null;

        var token = _tokens.CreateOpenBankingAccessToken(client.ClientId, client.Name, requestedScopes);
        client.LastTokenIssuedOn = DateTimeOffset.UtcNow;
        client.UpdatedOn = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync("openbanking.token.issued", new
        {
            clientId = client.ClientId,
            scopes = requestedScopes
        }, client.ClientId, traceId, ct);

        return new OpenBankingTokenResponse(token, "Bearer", _tokens.GetAccessTokenLifetimeSeconds(), string.Join(' ', requestedScopes));
    }

    public async Task<OpenBankingBalanceResponse?> GetBalanceAsync(ClaimsPrincipal user, Guid accountId, string traceId, CancellationToken ct)
    {
        var client = await GetAuthorizedClientAsync(user, accountId, ct);
        if (client is null)
            return null;

        var account = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == accountId, ct);
        if (account is null)
            return null;

        var ledgerBalance = await _db.LedgerEntries
            .AsNoTracking()
            .Where(x => x.AccountId == accountId)
            .SumAsync(x => x.Amount, ct);

        await _audit.WriteAsync("openbanking.balance.read", new
        {
            clientId = client.ClientId,
            accountId,
            traceId
        }, client.ClientId, traceId, ct);

        return new OpenBankingBalanceResponse(
            account.Id,
            account.CustomerId,
            account.CurrencyCode,
            ledgerBalance,
            account.AvailableLimit,
            account.HoldBalance,
            account.Status.ToString().ToUpperInvariant(),
            DateTimeOffset.UtcNow);
    }

    public async Task<IReadOnlyList<OpenBankingTransactionItem>?> GetTransactionsAsync(
        ClaimsPrincipal user,
        Guid accountId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int take,
        string traceId,
        CancellationToken ct)
    {
        var client = await GetAuthorizedClientAsync(user, accountId, ct);
        if (client is null)
            return null;

        var limit = take <= 0 ? 50 : Math.Min(take, 200);
        var query = _db.LedgerEntries.AsNoTracking().Where(x => x.AccountId == accountId);

        if (from.HasValue)
            query = query.Where(x => x.PostedOn >= from.Value);

        if (to.HasValue)
            query = query.Where(x => x.PostedOn <= to.Value);

        var items = await query
            .OrderByDescending(x => x.PostedOn)
            .Take(limit)
            .Select(x => new OpenBankingTransactionItem(
                x.Id,
                x.Type.ToString().ToUpperInvariant(),
                x.Amount,
                x.Description,
                x.PostedOn))
            .ToListAsync(ct);

        await _audit.WriteAsync("openbanking.transactions.read", new
        {
            clientId = client.ClientId,
            accountId,
            from,
            to,
            take = limit,
            traceId
        }, client.ClientId, traceId, ct);

        return items;
    }

    public static string HashSecret(string secret)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant();

    private static bool VerifySecret(string providedSecret, string storedHash)
        => string.Equals(HashSecret(providedSecret), storedHash, StringComparison.Ordinal);

    private static string GenerateClientSecret()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace("+", "A", StringComparison.Ordinal).Replace("/", "B", StringComparison.Ordinal);

    private async Task<OpenBankingClientEntity?> GetAuthorizedClientAsync(ClaimsPrincipal user, Guid accountId, CancellationToken ct)
    {
        var clientId = user.FindFirstValue("client_id");
        if (string.IsNullOrWhiteSpace(clientId))
            return null;

        var client = await _db.OpenBankingClients
            .AsNoTracking()
            .Include(x => x.AccountAccesses)
            .FirstOrDefaultAsync(x => x.ClientId == clientId && x.Enabled, ct);

        if (client is null)
            return null;

        if (client.AllowAllAccounts || client.AccountAccesses.Any(x => x.AccountId == accountId))
            return client;

        return null;
    }

    private static string[] NormalizeScopes(IEnumerable<string> scopes)
        => scopes
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(x => SupportedScopes.Contains(x, StringComparer.OrdinalIgnoreCase))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string[] SplitScopes(string scopes)
        => NormalizeScopes(scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static OpenBankingClientResponse MapClient(OpenBankingClientEntity entity, Guid[] accountIds, string? clientSecret)
        => new(
            entity.ClientId,
            entity.Name,
            SplitScopes(entity.AllowedScopes),
            entity.Enabled,
            entity.AllowAllAccounts,
            entity.CreatedOn,
            entity.UpdatedOn,
            entity.LastTokenIssuedOn,
            accountIds,
            clientSecret);
}
