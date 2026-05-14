using System.Text.Json;
using CardVault.Api.Contracts;
using CardVault.Api.Services;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Billing;
using CardVault.Infrastructure.Persistence.Switch;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;

namespace CardVault.Api.Background;

public sealed class SwitchTxnConsumer : BackgroundService
{
    private readonly ILogger<SwitchTxnConsumer> _logger;
    private readonly IConfiguration _cfg;
    private readonly IServiceProvider _sp;

    public SwitchTxnConsumer(ILogger<SwitchTxnConsumer> logger, IConfiguration cfg, IServiceProvider sp)
    {
        _logger = logger;
        _cfg = cfg;
        _sp = sp;
    }

    private static async Task<bool> TryCreateJournalAsync(CardVaultDbContext db, string network, string mti, string stan, string rrn,
        SwitchTxnType txnType, Guid accountId, decimal amount, DateTimeOffset postedOn, CancellationToken ct)
    {
        var exists = await db.TxnJournal.AsNoTracking()
            .AnyAsync(x => x.Network == network && x.Mti == mti && x.Stan == stan && x.Rrn == rrn, ct);

        if (exists) return false;

        db.TxnJournal.Add(new TxnJournalEntity
        {
            Id = Guid.NewGuid(),
            Network = network,
            Mti = mti,
            Stan = stan,
            Rrn = rrn,
            TxnType = txnType,
            AccountId = accountId,
            Amount = amount,
            Status = "received",
            PostedOn = postedOn,
            CreatedOn = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync(ct);
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        var bootstrap = _cfg["Kafka:BootstrapServers"] ?? "localhost:9092";
        var topic = _cfg["Kafka:SwitchConsumer:Topic"] ?? "sw.tx.events";

        var conf = new ConsumerConfig
        {
            BootstrapServers = bootstrap,
            GroupId = _cfg["Kafka:SwitchConsumer:GroupId"] ?? "cardvault-switch-consumer",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true
        };

        using var consumer = new ConsumerBuilder<string, string>(conf).Build();
        consumer.Subscribe(topic);

        _logger.LogInformation("SwitchTxnConsumer started. Topic={Topic} Bootstrap={Bootstrap}", topic, bootstrap);

        while (!stoppingToken.IsCancellationRequested)
        {
            ConsumeResult<string, string>? cr = null;
            try
            {
                cr = consumer.Consume(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Kafka consume error");
                continue;
            }

            if (cr?.Message?.Value is null) continue;

            try
            {
                using var doc = JsonDocument.Parse(cr.Message.Value);
                var root = doc.RootElement;

                var eventName = root.GetProperty("eventName").GetString() ?? "";
                var payload = root.GetProperty("payload");

                switch (eventName)
                {
                    case EventNames.SwitchPurchaseApprovedV1:
                        await HandlePurchaseApprovedAsync(payload, stoppingToken);
                        break;
                    case EventNames.SwitchPurchaseReversedV1:
                        await HandlePurchaseReversedAsync(payload, stoppingToken);
                        break;
                    case EventNames.SwitchRefundPostedV1:
                        await HandleRefundPostedAsync(payload, stoppingToken);
                        break;
                    case EventNames.SwitchChargebackPostedV1:
                        await HandleChargebackPostedAsync(payload, stoppingToken);
                        break;

                    // v42 preauth/clearing
                    case EventNames.SwitchAuthApprovedV1:
                        await HandleAuthApprovedAsync(payload, stoppingToken);
                        break;
                    case EventNames.SwitchAuthReversedV1:
                        await HandleAuthReversedAsync(payload, stoppingToken);
                        break;
                    case EventNames.SwitchClearingPostedV1:
                        await HandleClearingPostedAsync(payload, stoppingToken);
                        break;
                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed processing switch event: {Json}", cr.Message.Value);
            }
        }

        _logger.LogInformation("SwitchTxnConsumer stopped.");
    }

    private async Task HandlePurchaseApprovedAsync(JsonElement payload, CancellationToken ct)
    {
        var e = JsonSerializer.Deserialize<SwitchPurchaseApprovedV1>(payload.GetRawText());
        if (e is null) return;

        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CardVaultDbContext>();

        var created = await TryCreateJournalAsync(db, e.Network, e.Mti, e.Stan, e.Rrn, SwitchTxnType.Purchase, e.AccountId, e.Amount, e.PostedOn, ct);
        if (!created) return;

        var ledger = scope.ServiceProvider.GetRequiredService<LedgerService>();
        var le = await ledger.AddEntryAsync(e.AccountId, LedgerEntryType.Purchase, Math.Abs(e.Amount),
            $"SWITCH PURCHASE {e.Network} MTI:{e.Mti} STAN:{e.Stan} RRN:{e.Rrn}", e.PostedOn, ct);

        var j = await db.TxnJournal.FirstAsync(x => x.Network == e.Network && x.Mti == e.Mti && x.Stan == e.Stan && x.Rrn == e.Rrn, ct);
        j.LedgerEntryId = le.Id;
        j.Status = "posted";
        await db.SaveChangesAsync(ct);

        // posting rule: overlimit fee
        try
        {
            var fees = scope.ServiceProvider.GetRequiredService<FeeService>();
            await fees.AssessOverlimitAsync(e.AccountId, DateOnly.FromDateTime(e.PostedOn.UtcDateTime), e.PostedOn, ct);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Overlimit fee failed (non-fatal)."); }

        await TryRecalcOpenStatementAsync(scope, e.AccountId, e.PostedOn, ct);
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();
        await notifications.CreateTransactionNotificationAsync(e.AccountId, null, Math.Abs(e.Amount), "USD", $"{e.Network} merchant", EventNames.SwitchPurchaseApprovedV1, $"txn-{e.Stan}-{e.Rrn}", ct);
        var loyalty = scope.ServiceProvider.GetRequiredService<LoyaltyService>();
        await loyalty.ApplyPurchaseRewardsAsync(e.AccountId, Math.Abs(e.Amount), $"txn-{e.Stan}-{e.Rrn}", EventNames.SwitchPurchaseApprovedV1, ct);
    }

    private async Task HandlePurchaseReversedAsync(JsonElement payload, CancellationToken ct)
    {
        var e = JsonSerializer.Deserialize<SwitchPurchaseReversedV1>(payload.GetRawText());
        if (e is null) return;

        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CardVaultDbContext>();

        var created = await TryCreateJournalAsync(db, e.Network, e.Mti, e.Stan, e.Rrn, SwitchTxnType.Reversal, e.AccountId, e.Amount, e.PostedOn, ct);
        if (!created) return;

        var ledger = scope.ServiceProvider.GetRequiredService<LedgerService>();
        var le = await ledger.AddEntryAsync(e.AccountId, LedgerEntryType.Reversal, -Math.Abs(e.Amount),
            $"SWITCH REVERSAL {e.Network} MTI:{e.Mti} STAN:{e.Stan} RRN:{e.Rrn}", e.PostedOn, ct);

        var j = await db.TxnJournal.FirstAsync(x => x.Network == e.Network && x.Mti == e.Mti && x.Stan == e.Stan && x.Rrn == e.Rrn, ct);
        j.LedgerEntryId = le.Id;
        j.Status = "posted";
        await db.SaveChangesAsync(ct);

        await TryRecalcOpenStatementAsync(scope, e.AccountId, e.PostedOn, ct);
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();
        await notifications.CreateTransactionNotificationAsync(e.AccountId, null, -Math.Abs(e.Amount), "USD", $"{e.Network} reversal", EventNames.SwitchPurchaseReversedV1, $"txn-{e.Stan}-{e.Rrn}", ct);
        var loyalty = scope.ServiceProvider.GetRequiredService<LoyaltyService>();
        await loyalty.ReversePurchaseRewardsAsync(e.AccountId, Math.Abs(e.Amount), $"txn-{e.Stan}-{e.Rrn}", EventNames.SwitchPurchaseReversedV1, ct);
    }

    private async Task HandleRefundPostedAsync(JsonElement payload, CancellationToken ct)
    {
        var e = JsonSerializer.Deserialize<SwitchRefundPostedV1>(payload.GetRawText());
        if (e is null) return;

        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CardVaultDbContext>();

        var created = await TryCreateJournalAsync(db, e.Network, e.Mti, e.Stan, e.Rrn, SwitchTxnType.Refund, e.AccountId, e.Amount, e.PostedOn, ct);
        if (!created) return;

        var ledger = scope.ServiceProvider.GetRequiredService<LedgerService>();
        var le = await ledger.AddEntryAsync(e.AccountId, LedgerEntryType.Refund, -Math.Abs(e.Amount),
            $"SWITCH REFUND {e.Network} MTI:{e.Mti} STAN:{e.Stan} RRN:{e.Rrn}", e.PostedOn, ct);

        var j = await db.TxnJournal.FirstAsync(x => x.Network == e.Network && x.Mti == e.Mti && x.Stan == e.Stan && x.Rrn == e.Rrn, ct);
        j.LedgerEntryId = le.Id;
        j.Status = "posted";
        await db.SaveChangesAsync(ct);

        await TryRecalcOpenStatementAsync(scope, e.AccountId, e.PostedOn, ct);
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();
        await notifications.CreateTransactionNotificationAsync(e.AccountId, null, -Math.Abs(e.Amount), "USD", $"{e.Network} refund", EventNames.SwitchRefundPostedV1, $"txn-{e.Stan}-{e.Rrn}", ct);
        var loyalty = scope.ServiceProvider.GetRequiredService<LoyaltyService>();
        await loyalty.ReversePurchaseRewardsAsync(e.AccountId, Math.Abs(e.Amount), $"txn-{e.Stan}-{e.Rrn}", EventNames.SwitchRefundPostedV1, ct);
    }

    private async Task HandleChargebackPostedAsync(JsonElement payload, CancellationToken ct)
    {
        var e = JsonSerializer.Deserialize<SwitchChargebackPostedV1>(payload.GetRawText());
        if (e is null) return;

        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CardVaultDbContext>();

        var created = await TryCreateJournalAsync(db, e.Network, e.Mti, e.Stan, e.Rrn, SwitchTxnType.Chargeback, e.AccountId, e.Amount, e.PostedOn, ct);
        if (!created) return;

        var disputes = scope.ServiceProvider.GetRequiredService<DisputeService>();
        await disputes.OpenChargebackAsync(e.AccountId, e.Network, e.Stan, e.Rrn, e.ReasonCode ?? "0000", e.Amount, e.PostedOn, ct);

        var j = await db.TxnJournal.FirstAsync(x => x.Network == e.Network && x.Mti == e.Mti && x.Stan == e.Stan && x.Rrn == e.Rrn, ct);
        j.Status = "posted";
        await db.SaveChangesAsync(ct);

        await TryRecalcOpenStatementAsync(scope, e.AccountId, e.PostedOn, ct);
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();
        var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == e.AccountId, ct);
        if (account is not null)
        {
            await notifications.CreateSecurityAlertAsync(account.CustomerId, e.AccountId, null, "Chargeback activity detected", $"A chargeback or dispute event was detected for amount {Math.Abs(e.Amount):0.00} on your account.", EventNames.SwitchChargebackPostedV1, $"txn-{e.Stan}-{e.Rrn}", Infrastructure.Persistence.Notifications.NotificationSeverity.Warning, ct);
        }
    }

    // v42 - Preauth/clearing flows

    private async Task HandleAuthApprovedAsync(JsonElement payload, CancellationToken ct)
    {
        var e = JsonSerializer.Deserialize<SwitchAuthApprovedV1>(payload.GetRawText());
        if (e is null) return;

        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CardVaultDbContext>();

        var created = await TryCreateJournalAsync(db, e.Network, e.Mti, e.Stan, e.Rrn, SwitchTxnType.Authorization, e.AccountId, e.Amount, e.PostedOn, ct);
        if (!created) return;

        var holds = scope.ServiceProvider.GetRequiredService<HoldService>();
        try
        {
            await holds.AuthorizeAsync(e.AccountId, e.CardId, e.Network, e.Mti, e.Stan, e.Rrn, e.OriginalDataElements90, e.MerchantId, e.MerchantCategory, e.CountryCode, e.PinBlock, e.Amount, e.PostedOn, ct);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("AUTH_DECLINED"))
        {
            var jDecl = await db.TxnJournal.FirstAsync(x => x.Network == e.Network && x.Mti == e.Mti && x.Stan == e.Stan && x.Rrn == e.Rrn, ct);
            jDecl.Status = "declined";
            await db.SaveChangesAsync(ct);
            return;
        }

        var j = await db.TxnJournal.FirstAsync(x => x.Network == e.Network && x.Mti == e.Mti && x.Stan == e.Stan && x.Rrn == e.Rrn, ct);
        j.Status = "posted";
        await db.SaveChangesAsync(ct);
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();
        await notifications.CreateTransactionNotificationAsync(e.AccountId, e.CardId, Math.Abs(e.Amount), "USD", e.MerchantId ?? e.Network, EventNames.SwitchAuthApprovedV1, $"txn-{e.Stan}-{e.Rrn}", ct);
    }

    private async Task HandleAuthReversedAsync(JsonElement payload, CancellationToken ct)
    {
        var e = JsonSerializer.Deserialize<SwitchAuthReversedV1>(payload.GetRawText());
        if (e is null) return;

        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CardVaultDbContext>();

        var created = await TryCreateJournalAsync(db, e.Network, e.Mti, e.Stan, e.Rrn, SwitchTxnType.Reversal, e.AccountId, e.Amount, e.PostedOn, ct);
        if (!created) return;

        var holds = scope.ServiceProvider.GetRequiredService<HoldService>();
        await holds.ReleaseAsync(e.AccountId, e.Network, e.Mti, e.Stan, e.Rrn, e.OriginalDataElements90, e.PostedOn, ct);

        var j = await db.TxnJournal.FirstAsync(x => x.Network == e.Network && x.Mti == e.Mti && x.Stan == e.Stan && x.Rrn == e.Rrn, ct);
        j.Status = "posted";
        await db.SaveChangesAsync(ct);
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();
        await notifications.CreateTransactionNotificationAsync(e.AccountId, e.CardId, -Math.Abs(e.Amount), "USD", e.MerchantId ?? e.Network, EventNames.SwitchAuthReversedV1, $"txn-{e.Stan}-{e.Rrn}", ct);
    }

    private async Task HandleClearingPostedAsync(JsonElement payload, CancellationToken ct)
    {
        var e = JsonSerializer.Deserialize<SwitchClearingPostedV1>(payload.GetRawText());
        if (e is null) return;

        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CardVaultDbContext>();

        var created = await TryCreateJournalAsync(db, e.Network, e.Mti, e.Stan, e.Rrn, SwitchTxnType.Clearing, e.AccountId, e.Amount, e.PostedOn, ct);
        if (!created) return;

        var holds = scope.ServiceProvider.GetRequiredService<HoldService>();
        await holds.CaptureAsync(e.AccountId, e.Network, e.Mti, e.Stan, e.Rrn, e.OriginalDataElements90, e.Amount, e.PostedOn, ct);

        var j = await db.TxnJournal.FirstAsync(x => x.Network == e.Network && x.Mti == e.Mti && x.Stan == e.Stan && x.Rrn == e.Rrn, ct);
        j.Status = "posted";
        await db.SaveChangesAsync(ct);

        // clearing affects statement
        await TryRecalcOpenStatementAsync(scope, e.AccountId, e.PostedOn, ct);
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();
        await notifications.CreateTransactionNotificationAsync(e.AccountId, e.CardId, Math.Abs(e.Amount), "USD", e.MerchantId ?? e.Network, EventNames.SwitchClearingPostedV1, $"txn-{e.Stan}-{e.Rrn}", ct);
    }

    private async Task TryRecalcOpenStatementAsync(IServiceScope scope, Guid accountId, DateTimeOffset postedOn, CancellationToken ct)
    {
        try
        {
            var db = scope.ServiceProvider.GetRequiredService<CardVaultDbContext>();
            var minPay = scope.ServiceProvider.GetRequiredService<MinimumPaymentService>();
            await UpdateOpenStatementAsync(db, minPay, accountId, postedOn, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Open statement recalculation failed (non-fatal).");
        }
    }

    private static async Task UpdateOpenStatementAsync(CardVaultDbContext db,
        MinimumPaymentService minPay,
        Guid accountId,
        DateTimeOffset postedOn,
        CancellationToken ct)
    {
        var st = await db.Statements.FirstOrDefaultAsync(x =>
            x.AccountId == accountId &&
            x.Status == StatementStatus.Open &&
            postedOn.UtcDateTime >= x.CycleStart &&
            postedOn.UtcDateTime <= x.CycleEnd, ct);

        if (st is null) return;

        var cycleStart = st.CycleStart;
        var cycleEnd = st.CycleEnd;

        var prevBalance = await db.LedgerEntries.AsNoTracking()
            .Where(x => x.AccountId == accountId && x.PostedOn < cycleStart)
            .SumAsync(x => x.Amount, ct);

        var cycleEntries = await db.LedgerEntries.AsNoTracking()
            .Where(x => x.AccountId == accountId && x.PostedOn >= cycleStart && x.PostedOn <= cycleEnd)
            .ToListAsync(ct);

        st.PreviousBalance = prevBalance;

        // Purchases include PURCHASE + CLEARING + refunds/reversals/chargebacks/adjustments. Holds are excluded.
        st.Purchases = cycleEntries.Where(x =>
                x.Type == LedgerEntryType.Purchase ||
                x.Type == LedgerEntryType.Clearing ||
                x.Type == LedgerEntryType.Refund ||
                x.Type == LedgerEntryType.Reversal ||
                x.Type == LedgerEntryType.Chargeback ||
                x.Type == LedgerEntryType.Adjustment)
            .Sum(x => x.Amount);

        st.Payments = cycleEntries.Where(x => x.Type == LedgerEntryType.Payment).Sum(x => x.Amount);
        st.Fees = cycleEntries.Where(x => x.Type == LedgerEntryType.Fee).Sum(x => x.Amount);
        st.Interest = cycleEntries.Where(x => x.Type == LedgerEntryType.Interest).Sum(x => x.Amount);

        st.InterestAccrued = st.Interest;
        st.InterestDue = st.InterestAccrued;
        st.FeesDue = st.Fees;

        var computedBalance = st.PreviousBalance + st.Purchases + st.Payments + st.Fees + st.Interest;
        st.PrincipalDue = Math.Max(0, computedBalance - st.InterestDue - st.FeesDue);

        st.TotalPaymentDue = st.PrincipalDue + st.InterestDue + st.FeesDue;
        st.NewBalance = st.TotalPaymentDue;

        var mp = await minPay.GetDefaultAsync(ct);
        st.MinimumPayment = minPay.CalculateMinimum(st, mp);

        await db.SaveChangesAsync(ct);
    }
}
