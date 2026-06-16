using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CardVault.Api.Contracts;
using CardVault.Api.Pci;
using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Ecommerce;
using CardVault.Infrastructure.Persistence.Issuer;
using Microsoft.EntityFrameworkCore;

namespace CardVault.Api.Services;

public sealed class ThreeDsService
{
    private const int ChallengeTtlMinutes = 5;
    private const int MaxOtpAttempts = 3;

    private readonly CardVaultDbContext _db;
    private readonly AuditService _audit;
    private readonly PciAuditPublisher _pciAudit;
    private readonly IHostEnvironment _environment;
    private readonly NotificationService _notifications;

    public ThreeDsService(
        CardVaultDbContext db,
        AuditService audit,
        PciAuditPublisher pciAudit,
        IHostEnvironment environment,
        NotificationService notifications)
    {
        _db = db;
        _audit = audit;
        _pciAudit = pciAudit;
        _environment = environment;
        _notifications = notifications;
    }

    public async Task<StartThreeDsChallengeResponse> StartChallengeAsync(
        StartThreeDsChallengeRequest request,
        string actor,
        string traceId,
        CancellationToken ct)
    {
        if (request.Amount <= 0)
            throw new InvalidOperationException("Amount must be greater than zero.");

        var card = await _db.Cards
            .Include(x => x.Account)
            .ThenInclude(x => x.Customer)
            .FirstOrDefaultAsync(x => x.Id == request.CardId, ct)
            ?? throw new KeyNotFoundException("Card not found.");

        if (card.Status != CardStatus.Active)
            throw new InvalidOperationException("Only active cards can start a 3DS challenge.");

        if (card.Account.Status != AccountStatus.Active)
            throw new InvalidOperationException("Only active accounts can start a 3DS challenge.");

        var normalizedCurrency = string.IsNullOrWhiteSpace(request.Currency) ? card.Account.CurrencyCode : request.Currency.Trim().ToUpperInvariant();
        var normalizedDeviceChannel = string.IsNullOrWhiteSpace(request.DeviceChannel) ? "BROWSER" : request.DeviceChannel.Trim().ToUpperInvariant();
        var normalizedMerchantCountry = NormalizeCountry(request.MerchantCountry);
        var normalizedBrowserCountry = NormalizeCountry(request.BrowserIpCountry);
        var limits = await _db.AccountLimits.AsNoTracking().FirstOrDefaultAsync(x => x.AccountId == card.AccountId, ct);
        var failureWindowStart = DateTimeOffset.UtcNow.AddHours(-24);
        var recentFailures = await _db.ThreeDsChallenges
            .AsNoTracking()
            .CountAsync(x =>
                x.CardId == card.Id &&
                x.CreatedOn >= failureWindowStart &&
                (x.Status == ThreeDsChallengeStatus.Rejected || x.Status == ThreeDsChallengeStatus.Expired), ct);

        var riskReasons = BuildRiskReasons(request.Amount, limits, normalizedMerchantCountry, normalizedBrowserCountry, recentFailures);
        var riskScore = CalculateRiskScore(request.Amount, limits, normalizedMerchantCountry, normalizedBrowserCountry, recentFailures);

        var otp = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var salt = Base64Url(RandomNumberGenerator.GetBytes(18));
        var now = DateTimeOffset.UtcNow;

        var entity = new ThreeDsChallengeEntity
        {
            Id = Guid.NewGuid(),
            CardId = card.Id,
            AccountId = card.AccountId,
            CustomerId = card.Account.CustomerId,
            MaskedPan = card.MaskedPan,
            Amount = decimal.Round(request.Amount, 2, MidpointRounding.AwayFromZero),
            CurrencyCode = normalizedCurrency,
            MerchantId = request.MerchantId.Trim(),
            MerchantName = string.IsNullOrWhiteSpace(request.MerchantName) ? "UNKNOWN MERCHANT" : request.MerchantName.Trim(),
            MerchantCountry = normalizedMerchantCountry,
            BrowserIpCountry = normalizedBrowserCountry,
            DeviceChannel = normalizedDeviceChannel,
            RiskScore = riskScore,
            RiskReasonsJson = JsonSerializer.Serialize(riskReasons),
            ContactHint = BuildContactHint(card.Account.Customer),
            OtpSalt = salt,
            OtpHash = HashOtp(otp, salt),
            OtpAttempts = 0,
            MaxAttempts = MaxOtpAttempts,
            Status = ThreeDsChallengeStatus.Pending,
            Decision = ThreeDsDecision.Pending,
            RequestedBy = actor,
            TraceId = traceId,
            ExpiresOn = now.AddMinutes(ChallengeTtlMinutes),
            CreatedOn = now,
            UpdatedOn = now
        };

        _db.ThreeDsChallenges.Add(entity);
        await _db.SaveChangesAsync(ct);

        await PublishAuditAsync("cardvault.3ds.started", entity, riskReasons, actor, traceId, ct);

        if (entity.RiskScore >= 70)
        {
            await _notifications.CreateSecurityAlertAsync(
                entity.CustomerId,
                entity.AccountId,
                entity.CardId,
                "Suspicious ecommerce activity detected",
                $"A high-risk ecommerce authentication attempt was detected for {entity.MaskedPan}.",
                "cardvault.3ds.started",
                traceId,
                Infrastructure.Persistence.Notifications.NotificationSeverity.Warning,
                ct);
        }

        return new StartThreeDsChallengeResponse(
            entity.Id,
            entity.CardId,
            entity.AccountId,
            entity.CustomerId,
            entity.Status.ToString().ToUpperInvariant(),
            entity.Decision.ToString().ToUpperInvariant(),
            entity.RiskScore,
            riskReasons,
            entity.ContactHint,
            entity.ExpiresOn,
            _environment.IsDevelopment() ? otp : null);
    }

    public async Task<VerifyThreeDsChallengeResponse> VerifyChallengeAsync(
        Guid challengeId,
        string otp,
        string actor,
        string traceId,
        CancellationToken ct)
    {
        var entity = await _db.ThreeDsChallenges.FirstOrDefaultAsync(x => x.Id == challengeId, ct)
            ?? throw new KeyNotFoundException("3DS challenge not found.");

        var now = DateTimeOffset.UtcNow;

        if (entity.Status != ThreeDsChallengeStatus.Pending)
        {
            return BuildVerifyResponse(entity, entity.DecisionReason ?? "challenge_already_completed");
        }

        if (now > entity.ExpiresOn)
        {
            entity.Status = ThreeDsChallengeStatus.Expired;
            entity.Decision = ThreeDsDecision.Reject;
            entity.DecisionReason = "challenge_expired";
            entity.CompletedOn = now;
            entity.UpdatedOn = now;

            await _db.SaveChangesAsync(ct);
            await PublishAuditAsync("cardvault.3ds.expired", entity, DeserializeReasons(entity.RiskReasonsJson), actor, traceId, ct);
            await _notifications.CreateSecurityAlertAsync(
                entity.CustomerId,
                entity.AccountId,
                entity.CardId,
                "Ecommerce authentication expired",
                $"A 3DS authentication challenge for {entity.MaskedPan} expired before completion.",
                "cardvault.3ds.expired",
                traceId,
                Infrastructure.Persistence.Notifications.NotificationSeverity.Warning,
                ct);

            return BuildVerifyResponse(entity, entity.DecisionReason);
        }

        if (!VerifyOtp(entity, otp))
        {
            entity.OtpAttempts++;
            entity.UpdatedOn = now;

            if (entity.OtpAttempts >= entity.MaxAttempts)
            {
                entity.Status = ThreeDsChallengeStatus.Rejected;
                entity.Decision = ThreeDsDecision.Reject;
                entity.DecisionReason = "otp_attempts_exhausted";
                entity.CompletedOn = now;

                await _db.SaveChangesAsync(ct);
                await PublishAuditAsync("cardvault.3ds.rejected", entity, DeserializeReasons(entity.RiskReasonsJson), actor, traceId, ct);
                await _notifications.CreateSecurityAlertAsync(
                    entity.CustomerId,
                    entity.AccountId,
                    entity.CardId,
                    "Repeated invalid ecommerce authentication attempts",
                    $"Multiple invalid OTP attempts were detected for {entity.MaskedPan}.",
                    "cardvault.3ds.rejected",
                    traceId,
                    Infrastructure.Persistence.Notifications.NotificationSeverity.Critical,
                    ct);
            }
            else
            {
                await _db.SaveChangesAsync(ct);
                await PublishAuditAsync("cardvault.3ds.failed-attempt", entity, DeserializeReasons(entity.RiskReasonsJson), actor, traceId, ct);
            }

            return BuildVerifyResponse(entity, entity.DecisionReason ?? "invalid_otp");
        }

        entity.OtpAttempts++;
        entity.Status = ThreeDsChallengeStatus.Authenticated;
        entity.Decision = ThreeDsDecision.Approve;
        entity.DecisionReason = "authenticated";
        entity.AuthenticatedOn = now;
        entity.CompletedOn = now;
        entity.UpdatedOn = now;

        await _db.SaveChangesAsync(ct);
        await PublishAuditAsync("cardvault.3ds.authenticated", entity, DeserializeReasons(entity.RiskReasonsJson), actor, traceId, ct);

        return BuildVerifyResponse(entity, entity.DecisionReason);
    }

    public async Task<ThreeDsChallengeView?> GetChallengeAsync(Guid challengeId, CancellationToken ct)
    {
        var entity = await _db.ThreeDsChallenges.AsNoTracking().FirstOrDefaultAsync(x => x.Id == challengeId, ct);
        return entity is null ? null : Map(entity);
    }

    public async Task<IReadOnlyList<ThreeDsChallengeView>> ListChallengesAsync(string? status, int take, CancellationToken ct)
    {
        var limit = take <= 0 ? 50 : Math.Min(take, 200);
        var query = _db.ThreeDsChallenges.AsNoTracking().AsQueryable();

        if (TryParseStatus(status, out var parsedStatus))
            query = query.Where(x => x.Status == parsedStatus);

        var items = await query
            .OrderByDescending(x => x.CreatedOn)
            .Take(limit)
            .ToListAsync(ct);

        return items.Select(Map).ToList();
    }

    private async Task PublishAuditAsync(
        string eventType,
        ThreeDsChallengeEntity entity,
        IReadOnlyList<string> riskReasons,
        string actor,
        string traceId,
        CancellationToken ct)
    {
        var payload = new
        {
            challengeId = entity.Id,
            cardId = entity.CardId,
            accountId = entity.AccountId,
            customerId = entity.CustomerId,
            maskedPan = entity.MaskedPan,
            amount = entity.Amount,
            currency = entity.CurrencyCode,
            merchantId = entity.MerchantId,
            merchantName = entity.MerchantName,
            merchantCountry = entity.MerchantCountry,
            browserIpCountry = entity.BrowserIpCountry,
            deviceChannel = entity.DeviceChannel,
            riskScore = entity.RiskScore,
            riskReasons,
            attemptsUsed = entity.OtpAttempts,
            maxAttempts = entity.MaxAttempts,
            status = entity.Status.ToString(),
            decision = entity.Decision.ToString(),
            decisionReason = entity.DecisionReason,
            contactHint = entity.ContactHint,
            requestedBy = actor,
            traceId
        };

        await _audit.WriteAsync(eventType, payload, entity.Id.ToString("N"), traceId, ct);
        await _pciAudit.PublishAsync($"pci.{eventType.Replace("cardvault.", string.Empty, StringComparison.Ordinal)}", entity.Id.ToString("N"), payload, ct);
    }

    private static VerifyThreeDsChallengeResponse BuildVerifyResponse(ThreeDsChallengeEntity entity, string decisionReason)
        => new(
            entity.Id,
            entity.Status.ToString().ToUpperInvariant(),
            entity.Decision.ToString().ToUpperInvariant(),
            decisionReason,
            entity.OtpAttempts,
            Math.Max(entity.MaxAttempts - entity.OtpAttempts, 0),
            entity.CompletedOn);

    private static ThreeDsChallengeView Map(ThreeDsChallengeEntity entity)
        => new(
            entity.Id,
            entity.CardId,
            entity.AccountId,
            entity.CustomerId,
            entity.MaskedPan,
            entity.Amount,
            entity.CurrencyCode,
            entity.MerchantId,
            entity.MerchantName,
            entity.MerchantCountry,
            entity.BrowserIpCountry,
            entity.DeviceChannel,
            entity.RiskScore,
            DeserializeReasons(entity.RiskReasonsJson),
            entity.ContactHint,
            entity.Status.ToString().ToUpperInvariant(),
            entity.Decision.ToString().ToUpperInvariant(),
            entity.DecisionReason,
            entity.OtpAttempts,
            entity.MaxAttempts,
            entity.ExpiresOn,
            entity.AuthenticatedOn,
            entity.CompletedOn,
            entity.CreatedOn,
            entity.UpdatedOn,
            entity.RequestedBy,
            entity.TraceId);

    private static List<string> BuildRiskReasons(
        decimal amount,
        AccountLimitEntity? limits,
        string? merchantCountry,
        string? browserIpCountry,
        int recentFailures)
    {
        var reasons = new List<string>();

        if (amount >= 250m)
            reasons.Add("amount_above_standard_ticket");

        if (limits is not null && limits.DailyEcommerceLimit > 0 && amount > limits.DailyEcommerceLimit)
            reasons.Add("amount_above_daily_ecommerce_limit");

        if (!string.IsNullOrWhiteSpace(merchantCountry) &&
            !string.IsNullOrWhiteSpace(browserIpCountry) &&
            !string.Equals(merchantCountry, browserIpCountry, StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("country_mismatch_between_browser_and_merchant");
        }

        if (recentFailures > 0)
            reasons.Add("recent_failed_3ds_activity");

        if (reasons.Count == 0)
            reasons.Add("baseline_ecommerce_authentication");

        return reasons;
    }

    private static int CalculateRiskScore(
        decimal amount,
        AccountLimitEntity? limits,
        string? merchantCountry,
        string? browserIpCountry,
        int recentFailures)
    {
        var score = 15;

        if (amount >= 250m)
            score += 20;

        if (amount >= 1_000m)
            score += 10;

        if (limits is not null && limits.DailyEcommerceLimit > 0 && amount > limits.DailyEcommerceLimit)
            score += 30;

        if (!string.IsNullOrWhiteSpace(merchantCountry) &&
            !string.IsNullOrWhiteSpace(browserIpCountry) &&
            !string.Equals(merchantCountry, browserIpCountry, StringComparison.OrdinalIgnoreCase))
        {
            score += 20;
        }

        if (recentFailures > 0)
            score += Math.Min(recentFailures * 10, 25);

        return Math.Clamp(score, 0, 100);
    }

    private static IReadOnlyList<string> DeserializeReasons(string json)
        => JsonSerializer.Deserialize<List<string>>(json) ?? [];

    private static string BuildContactHint(CustomerEntity customer)
    {
        if (!string.IsNullOrWhiteSpace(customer.Email) && customer.Email.Contains('@'))
        {
            var parts = customer.Email.Split('@', 2, StringSplitOptions.RemoveEmptyEntries);
            var local = parts[0];
            var domain = parts[1];
            var prefix = local.Length <= 2 ? local[..1] : local[..2];
            return $"{prefix}***@{domain}";
        }

        if (!string.IsNullOrWhiteSpace(customer.Phone))
        {
            var digits = new string(customer.Phone.Where(char.IsDigit).ToArray());
            if (digits.Length >= 4)
                return $"***{digits[^4..]}";
        }

        return "contact-on-file";
    }

    private static string NormalizeCountry(string? value)
        => string.IsNullOrWhiteSpace(value) ? null! : value.Trim().ToUpperInvariant()[..Math.Min(2, value.Trim().Length)];

    private static string HashOtp(string otp, string salt)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{salt}:{otp}"));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static bool VerifyOtp(ThreeDsChallengeEntity entity, string otp)
        => string.Equals(entity.OtpHash, HashOtp(otp.Trim(), entity.OtpSalt), StringComparison.Ordinal);

    private static bool TryParseStatus(string? value, out ThreeDsChallengeStatus status)
    {
        if (!string.IsNullOrWhiteSpace(value) && Enum.TryParse<ThreeDsChallengeStatus>(value, true, out status))
            return true;

        status = default;
        return false;
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
