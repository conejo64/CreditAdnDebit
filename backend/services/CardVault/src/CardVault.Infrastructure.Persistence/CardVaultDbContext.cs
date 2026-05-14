using CardVault.Infrastructure.Persistence.Outbox;
using CardVault.Infrastructure.Persistence.Audit;
using CardVault.Infrastructure.Persistence.Catalog;
using CardVault.Infrastructure.Persistence.Routing;
using CardVault.Infrastructure.Persistence.Issuer;
using CardVault.Infrastructure.Persistence.Billing;
using CardVault.Infrastructure.Persistence.Settlement;
using CardVault.Infrastructure.Persistence.Switch;
using CardVault.Infrastructure.Persistence.Ecommerce;
using CardVault.Infrastructure.Persistence.Notifications;
using CardVault.Infrastructure.Persistence.OpenBanking;
using CardVault.Infrastructure.Persistence.Accounting;
using CardVault.Infrastructure.Persistence.Loyalty;
using CardVault.Infrastructure.Persistence.Wallets;
using CardVault.Infrastructure.Persistence.CreditLimits;
using Microsoft.EntityFrameworkCore;

namespace CardVault.Infrastructure.Persistence;

public sealed class CardVaultDbContext : DbContext
{
    public CardVaultDbContext(DbContextOptions<CardVaultDbContext> options) : base(options) { }

    public DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();
    public DbSet<RoutingRuleEntity> RoutingRules => Set<RoutingRuleEntity>();
    public DbSet<AuditEventEntity> AuditEvents => Set<AuditEventEntity>();

    public DbSet<BinRangeEntity> BinRanges => Set<BinRangeEntity>();
    public DbSet<CountryEntity> Countries => Set<CountryEntity>();
    public DbSet<CardProductEntity> CardProducts => Set<CardProductEntity>();

    // v30 - Issuer
    public DbSet<CustomerEntity> Customers => Set<CustomerEntity>();
    public DbSet<CardAccountEntity> Accounts => Set<CardAccountEntity>();
    public DbSet<CardVault.Infrastructure.Persistence.Billing.AuthorizationHoldEntity> AuthorizationHolds => Set<CardVault.Infrastructure.Persistence.Billing.AuthorizationHoldEntity>();
    public DbSet<CardEntity> Cards => Set<CardEntity>();
    public DbSet<CardStatusHistoryEntity> CardStatusHistory => Set<CardStatusHistoryEntity>();

    // v31 - Billing/Ledger
    public DbSet<LedgerEntryEntity> LedgerEntries => Set<LedgerEntryEntity>();
    public DbSet<StatementEntity> Statements => Set<StatementEntity>();
    public DbSet<StatementLineEntity> StatementLines => Set<StatementLineEntity>();
    public DbSet<PaymentAllocationPolicyEntity> PaymentAllocationPolicies => Set<PaymentAllocationPolicyEntity>();
    public DbSet<MinimumPaymentPolicyEntity> MinimumPaymentPolicies => Set<MinimumPaymentPolicyEntity>();
    public DbSet<InterestAccrualRecordEntity> InterestAccrualRecords => Set<InterestAccrualRecordEntity>();
    public DbSet<FeeAssessmentEntity> FeeAssessments => Set<FeeAssessmentEntity>();
    public DbSet<CreditPolicyEntity> CreditPolicies => Set<CreditPolicyEntity>();
    public DbSet<InstallmentPlanEntity> InstallmentPlans => Set<InstallmentPlanEntity>();
    public DbSet<AmortizationScheduleEntity> AmortizationSchedules => Set<AmortizationScheduleEntity>();

    // v34 - Settlement
    public DbSet<SettlementBatchEntity> SettlementBatches => Set<SettlementBatchEntity>();
    public DbSet<SettlementItemEntity> SettlementItems => Set<SettlementItemEntity>();

    // v35 - Switch idempotency/disputes
    public DbSet<TxnJournalEntity> TxnJournal => Set<TxnJournalEntity>();
    public DbSet<CardVault.Infrastructure.Persistence.Billing.DisputeCaseEntity> DisputeCases => Set<CardVault.Infrastructure.Persistence.Billing.DisputeCaseEntity>();
    public DbSet<RefundRecordEntity> RefundRecords => Set<RefundRecordEntity>();
    public DbSet<DisputeEventEntity> DisputeEvents => Set<DisputeEventEntity>();

    public DbSet<CardVault.Infrastructure.Persistence.Vault.TokenVaultEntryEntity> TokenVault => Set<CardVault.Infrastructure.Persistence.Vault.TokenVaultEntryEntity>();
    public DbSet<CardVault.Infrastructure.Persistence.Vault.VaultSettingsEntity> VaultSettings => Set<CardVault.Infrastructure.Persistence.Vault.VaultSettingsEntity>();

    // v44 - MCC rules
    public DbSet<MccRuleEntity> MccRules => Set<MccRuleEntity>();
    
    // v45 - Velocity and Antifraud rules
    public DbSet<VelocityRuleEntity> VelocityRules => Set<VelocityRuleEntity>();
    public DbSet<AntifraudRuleEntity> AntifraudRules => Set<AntifraudRuleEntity>();
    public DbSet<AccountLimitEntity> AccountLimits => Set<AccountLimitEntity>();
    public DbSet<ThreeDsChallengeEntity> ThreeDsChallenges => Set<ThreeDsChallengeEntity>();
    public DbSet<CustomerNotificationEntity> CustomerNotifications => Set<CustomerNotificationEntity>();
    public DbSet<CustomerNotificationDeliveryEntity> CustomerNotificationDeliveries => Set<CustomerNotificationDeliveryEntity>();
    public DbSet<OpenBankingClientEntity> OpenBankingClients => Set<OpenBankingClientEntity>();
    public DbSet<OpenBankingClientAccountAccessEntity> OpenBankingClientAccountAccesses => Set<OpenBankingClientAccountAccessEntity>();
    public DbSet<LedgerAccountEntity> LedgerAccounts => Set<LedgerAccountEntity>();
    public DbSet<AccountingMappingEntity> AccountingMappings => Set<AccountingMappingEntity>();
    public DbSet<JournalEntryEntity> JournalEntries => Set<JournalEntryEntity>();
    public DbSet<JournalEntryLineEntity> JournalEntryLines => Set<JournalEntryLineEntity>();
    public DbSet<RewardProgramEntity> RewardPrograms => Set<RewardProgramEntity>();
    public DbSet<LoyaltyBalanceEntity> LoyaltyBalances => Set<LoyaltyBalanceEntity>();
    public DbSet<LoyaltyEntryEntity> LoyaltyEntries => Set<LoyaltyEntryEntity>();
    public DbSet<RewardCatalogItemEntity> RewardCatalogItems => Set<RewardCatalogItemEntity>();
    public DbSet<WalletTokenEntity> WalletTokens => Set<WalletTokenEntity>();
    public DbSet<WalletAuthorizationEntity> WalletAuthorizations => Set<WalletAuthorizationEntity>();
    public DbSet<OverlimitEventEntity> OverlimitEvents => Set<OverlimitEventEntity>();
    public DbSet<CreditLimitProposalEntity> CreditLimitProposals => Set<CreditLimitProposalEntity>();

    // v76 - Early Delinquency (Mora Temprana)
    public DbSet<DelinquencyRecordEntity> DelinquencyRecords => Set<DelinquencyRecordEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessageEntity>(b =>
        {
            b.ToTable("OutboxMessages");
            b.HasKey(x => x.Id);
            b.Property(x => x.Topic).IsRequired();
            b.Property(x => x.Key).IsRequired();
            b.Property(x => x.PayloadJson).IsRequired();
            b.HasIndex(x => x.ProcessedOn);
        });

        modelBuilder.Entity<RoutingRuleEntity>(b =>
        {
            b.ToTable("RoutingRules");
            b.HasKey(x => x.Id);
            b.Property(x => x.ConnectorId).IsRequired();
            b.HasIndex(x => x.Priority);
            b.HasIndex(x => new { x.BinStart, x.BinEnd });
        });

        modelBuilder.Entity<BinRangeEntity>(b =>
        {
            b.ToTable("BinRanges");
            b.HasKey(x => x.Id);
            b.Property(x => x.Brand).IsRequired();
            b.Property(x => x.Product).IsRequired();
            b.HasIndex(x => new { x.BinStart, x.BinEnd });
            b.HasIndex(x => x.Brand);
        });

        modelBuilder.Entity<CountryEntity>(b =>
        {
            b.ToTable("Countries");
            b.HasKey(x => x.Code);
            b.Property(x => x.Code).HasMaxLength(2);
            b.Property(x => x.Name).HasMaxLength(80);
            b.Property(x => x.NumericCode).HasMaxLength(3);
            b.Property(x => x.Currency).HasMaxLength(3);
        });

        modelBuilder.Entity<AuditEventEntity>(b =>
        {
            b.ToTable("AuditEvents");
            b.HasKey(x => x.Id);
            b.Property(x => x.Service).HasMaxLength(64);
            b.Property(x => x.EventType).HasMaxLength(128).IsRequired();
            b.Property(x => x.CorrelationId).HasMaxLength(64);
            b.Property(x => x.TraceId).HasMaxLength(64);
            b.Property(x => x.PayloadSha256).HasMaxLength(64);
            b.HasIndex(x => x.OccurredOn);
            b.HasIndex(x => x.EventType);
        });

        modelBuilder.Entity<CardProductEntity>(b =>
        {
            b.ToTable("CardProducts");
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).IsRequired();
            b.HasIndex(x => x.Code).IsUnique();
        });

        // v30 - Issuer entities
        modelBuilder.Entity<CustomerEntity>(b =>
        {
            b.ToTable("Customers");
            b.HasKey(x => x.Id);
            b.Property(x => x.CustomerNumber).HasMaxLength(32).IsRequired();
            b.Property(x => x.FullName).HasMaxLength(120).IsRequired();
            b.Property(x => x.DocumentId).HasMaxLength(20).IsRequired();
            b.Property(x => x.Email).HasMaxLength(80).IsRequired();
            b.Property(x => x.Phone).HasMaxLength(20).IsRequired();
            b.HasIndex(x => x.CustomerNumber).IsUnique();
            b.HasIndex(x => x.DocumentId).IsUnique();
        });

        modelBuilder.Entity<CardAccountEntity>(b =>
        {
            b.ToTable("Accounts");
            b.HasKey(x => x.Id);
            b.Property(x => x.ProductCode).HasMaxLength(64).IsRequired();
            b.HasOne(x => x.Customer).WithMany(x => x.Accounts).HasForeignKey(x => x.CustomerId);
            b.HasIndex(x => new { x.CustomerId, x.AccountType });
        });

        modelBuilder.Entity<CardEntity>(b =>
        {
            b.ToTable("Cards");
            b.HasKey(x => x.Id);
            b.Property(x => x.Bin).HasMaxLength(12).IsRequired();
            b.Property(x => x.PanToken).HasMaxLength(32).IsRequired();
            b.Property(x => x.MaskedPan).HasMaxLength(19).IsRequired();
            b.Property(x => x.ExpiryYyMm).HasMaxLength(6).IsRequired();
            b.Property(x => x.Last4).HasMaxLength(8).IsRequired();
            b.HasOne(x => x.Account).WithMany(x => x.Cards).HasForeignKey(x => x.AccountId);
            b.HasIndex(x => x.PanToken).IsUnique();
            b.HasIndex(x => x.Last4);
        });

        modelBuilder.Entity<CardStatusHistoryEntity>(b =>
        {
            b.ToTable("CardStatusHistory");
            b.HasKey(x => x.Id);
            b.Property(x => x.Reason).HasMaxLength(120).IsRequired();
            b.HasOne(x => x.Card).WithMany(x => x.History).HasForeignKey(x => x.CardId);
            b.HasIndex(x => x.ChangedOn);
        });

        // v31 - Billing/Ledger entities
        modelBuilder.Entity<LedgerEntryEntity>(b =>
        {
            b.ToTable("LedgerEntries");
            b.HasKey(x => x.Id);
            b.Property(x => x.Description).HasMaxLength(200).IsRequired();
            b.HasIndex(x => new { x.AccountId, x.PostedOn });
            b.HasOne(x => x.Statement).WithMany().HasForeignKey(x => x.StatementId);
        });

        modelBuilder.Entity<StatementEntity>(b =>
        {
            b.ToTable("Statements");
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.AccountId, x.StatementDate });
            b.HasMany(x => x.Lines).WithOne(x => x.Statement).HasForeignKey(x => x.StatementId);
        });

        modelBuilder.Entity<StatementLineEntity>(b =>
        {
            b.ToTable("StatementLines");
            b.HasKey(x => x.Id);
            b.Property(x => x.Description).HasMaxLength(200).IsRequired();
            b.HasOne(x => x.LedgerEntry).WithMany().HasForeignKey(x => x.LedgerEntryId);
            b.HasIndex(x => new { x.StatementId, x.PostedOn });
        });

        // v37 - payment allocation policy
        modelBuilder.Entity<PaymentAllocationPolicyEntity>(b =>
        {
            b.ToTable("PaymentAllocationPolicies");
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasMaxLength(64).IsRequired();
            b.Property(x => x.Order).HasMaxLength(128).IsRequired();
            b.HasIndex(x => x.Code).IsUnique();
        });

        // v38 - minimum payment policy
        modelBuilder.Entity<MinimumPaymentPolicyEntity>(b =>
        {
            b.ToTable("MinimumPaymentPolicies");
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasMaxLength(64).IsRequired();
            b.HasIndex(x => x.Code).IsUnique();
        });

        // v39 - interest accrual records
        modelBuilder.Entity<InterestAccrualRecordEntity>(b =>
        {
            b.ToTable("InterestAccrualRecords");
            b.HasKey(x => x.Id);
            b.Property(x => x.AccountId).IsRequired();
            b.HasIndex(x => new { x.AccountId, x.AccrualDate, x.Segment }).IsUnique();
        });

        // v40 - fee assessments
        modelBuilder.Entity<FeeAssessmentEntity>(b =>
        {
            b.ToTable("FeeAssessments");
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.AccountId, x.FeeType, x.BusinessDate }).IsUnique();
        });

        modelBuilder.Entity<CreditPolicyEntity>(b =>
        {
            b.ToTable("CreditPolicies");
            b.HasKey(x => x.ProductCode);
            b.Property(x => x.ProductCode).HasMaxLength(64).IsRequired();
            b.HasIndex(x => x.ProductCode).IsUnique();
        });

        // v34 - Settlement entities
        modelBuilder.Entity<SettlementBatchEntity>(b =>
        {
            b.ToTable("SettlementBatches");
            b.HasKey(x => x.Id);
            b.HasMany(x => x.Items).WithOne(x => x.Batch).HasForeignKey(x => x.BatchId);
            b.HasIndex(x => new { x.Network, x.BusinessDate }).IsUnique();
        });

        modelBuilder.Entity<SettlementItemEntity>(b =>
        {
            b.ToTable("SettlementItems");
            b.HasKey(x => x.Id);
            b.Property(x => x.NetworkRef).HasMaxLength(64).IsRequired();
            b.HasIndex(x => new { x.BatchId, x.PostedOn });
        });

        // v35 - Switch idempotency / disputes
        modelBuilder.Entity<TxnJournalEntity>(b =>
        {
            b.ToTable("TxnJournal");
            b.HasKey(x => x.Id);
            b.Property(x => x.Network).HasMaxLength(16).IsRequired();
            b.Property(x => x.Mti).HasMaxLength(4).IsRequired();
            b.Property(x => x.Stan).HasMaxLength(6).IsRequired();
            b.Property(x => x.Rrn).HasMaxLength(12).IsRequired();
            b.HasIndex(x => new { x.Network, x.Mti, x.Stan, x.Rrn }).IsUnique();
            b.HasIndex(x => x.AccountId);
        });

        modelBuilder.Entity<CardVault.Infrastructure.Persistence.Billing.DisputeCaseEntity>(b =>
        {
            b.ToTable("DisputeCases");
            b.HasKey(x => x.Id);
            b.Property(x => x.Network).HasMaxLength(16).IsRequired();
            b.Property(x => x.Rrn).HasMaxLength(12).IsRequired();
            b.Property(x => x.ReasonCode).HasMaxLength(64).IsRequired();
            b.HasIndex(x => new { x.Network, x.Rrn });
            b.HasIndex(x => x.AccountId);
        });

        modelBuilder.Entity<RefundRecordEntity>(b =>
        {
            b.ToTable("RefundRecords");
            b.HasKey(x => x.Id);
            b.Property(x => x.Network).HasMaxLength(16).IsRequired();
            b.Property(x => x.Rrn).HasMaxLength(12).IsRequired();
            b.Property(x => x.Stan).HasMaxLength(6).IsRequired();
            b.HasIndex(x => new { x.Network, x.Rrn, x.Stan }).IsUnique();
            b.HasIndex(x => x.AccountId);
        });

        modelBuilder.Entity<DisputeEventEntity>(b =>
        {
            b.ToTable("DisputeEvents");
            b.HasKey(x => x.Id);
            b.Property(x => x.Action).HasMaxLength(32).IsRequired();
            b.Property(x => x.Notes).HasMaxLength(256);
            b.HasIndex(x => new { x.DisputeId, x.CreatedOn });
        });

        // v44 - MCC rules
        modelBuilder.Entity<MccRuleEntity>(b =>
        {
            b.ToTable("MccRules");
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.Mcc).IsUnique();
        });

        // v45 - velocity rules
        modelBuilder.Entity<VelocityRuleEntity>(b =>
        {
            b.ToTable("VelocityRules");
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.ProductCode);
        });

        // v66 - Installment entities
        modelBuilder.Entity<InstallmentPlanEntity>(b =>
        {
            b.ToTable("InstallmentPlans");
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.AccountId);
            b.HasMany(x => x.AmortizationSchedule).WithOne().HasForeignKey(x => x.PlanId);
        });

        modelBuilder.Entity<AmortizationScheduleEntity>(b =>
        {
            b.ToTable("AmortizationSchedules");
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.PlanId, x.InstallmentNumber }).IsUnique();
            b.HasIndex(x => new { x.Status, x.DueDate });
        });

        // v81 - Account Limits
        modelBuilder.Entity<AccountLimitEntity>(b =>
        {
            b.ToTable("AccountLimits");
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.AccountId).IsUnique();
        });

        // v70 - Ecommerce 3DS
        modelBuilder.Entity<ThreeDsChallengeEntity>(b =>
        {
            b.ToTable("ThreeDsChallenges");
            b.HasKey(x => x.Id);
            b.Property(x => x.MaskedPan).HasMaxLength(19).IsRequired();
            b.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            b.Property(x => x.MerchantId).HasMaxLength(32).IsRequired();
            b.Property(x => x.MerchantName).HasMaxLength(120).IsRequired();
            b.Property(x => x.MerchantCountry).HasMaxLength(2);
            b.Property(x => x.BrowserIpCountry).HasMaxLength(2);
            b.Property(x => x.DeviceChannel).HasMaxLength(24).IsRequired();
            b.Property(x => x.RiskReasonsJson).IsRequired();
            b.Property(x => x.ContactHint).HasMaxLength(120).IsRequired();
            b.Property(x => x.OtpHash).HasMaxLength(128).IsRequired();
            b.Property(x => x.OtpSalt).HasMaxLength(64).IsRequired();
            b.Property(x => x.DecisionReason).HasMaxLength(64);
            b.Property(x => x.RequestedBy).HasMaxLength(120).IsRequired();
            b.Property(x => x.TraceId).HasMaxLength(64).IsRequired();
            b.HasOne(x => x.Card).WithMany().HasForeignKey(x => x.CardId);
            b.HasIndex(x => new { x.CardId, x.CreatedOn });
            b.HasIndex(x => new { x.Status, x.CreatedOn });
            b.HasIndex(x => x.TraceId);
        });

        // v74 - Customer notifications
        modelBuilder.Entity<CustomerNotificationEntity>(b =>
        {
            b.ToTable("CustomerNotifications");
            b.HasKey(x => x.Id);
            b.Property(x => x.Title).HasMaxLength(140).IsRequired();
            b.Property(x => x.Message).HasMaxLength(512).IsRequired();
            b.Property(x => x.CurrencyCode).HasMaxLength(3);
            b.Property(x => x.MerchantName).HasMaxLength(120);
            b.Property(x => x.SourceEvent).HasMaxLength(64);
            b.Property(x => x.TraceId).HasMaxLength(64);
            b.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId);
            b.HasMany(x => x.Deliveries).WithOne(x => x.Notification).HasForeignKey(x => x.NotificationId);
            b.HasIndex(x => new { x.CustomerId, x.CreatedOn });
            b.HasIndex(x => new { x.Type, x.CreatedOn });
        });

        modelBuilder.Entity<CustomerNotificationDeliveryEntity>(b =>
        {
            b.ToTable("CustomerNotificationDeliveries");
            b.HasKey(x => x.Id);
            b.Property(x => x.DestinationMasked).HasMaxLength(120).IsRequired();
            b.Property(x => x.DestinationHash).HasMaxLength(256).IsRequired();
            b.Property(x => x.ProviderReference).HasMaxLength(128);
            b.Property(x => x.LastError).HasMaxLength(256);
            b.HasIndex(x => new { x.Status, x.CreatedOn });
            b.HasIndex(x => new { x.NotificationId, x.Channel }).IsUnique();
        });

        // v73 - Open Banking
        modelBuilder.Entity<OpenBankingClientEntity>(b =>
        {
            b.ToTable("OpenBankingClients");
            b.HasKey(x => x.Id);
            b.Property(x => x.ClientId).HasMaxLength(64).IsRequired();
            b.Property(x => x.Name).HasMaxLength(120).IsRequired();
            b.Property(x => x.SecretHash).HasMaxLength(256).IsRequired();
            b.Property(x => x.AllowedScopes).HasMaxLength(256).IsRequired();
            b.HasIndex(x => x.ClientId).IsUnique();
            b.HasMany(x => x.AccountAccesses).WithOne(x => x.Client).HasForeignKey(x => x.ClientEntityId);
        });

        modelBuilder.Entity<OpenBankingClientAccountAccessEntity>(b =>
        {
            b.ToTable("OpenBankingClientAccountAccesses");
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.ClientEntityId, x.AccountId }).IsUnique();
            b.HasIndex(x => x.AccountId);
        });

        // v65 - Accounting integration
        modelBuilder.Entity<LedgerAccountEntity>(b =>
        {
            b.ToTable("LedgerAccounts");
            b.HasKey(x => x.Id);
            b.Property(x => x.AccountCode).HasMaxLength(30).IsRequired();
            b.Property(x => x.AccountName).HasMaxLength(150).IsRequired();
            b.Property(x => x.AccountType).HasMaxLength(30).IsRequired();
            b.Property(x => x.CurrencyCode).HasMaxLength(10).IsRequired();
            b.Property(x => x.Status).HasMaxLength(20).IsRequired();
            b.HasIndex(x => x.AccountCode).IsUnique();
        });

        modelBuilder.Entity<AccountingMappingEntity>(b =>
        {
            b.ToTable("AccountingMappings");
            b.HasKey(x => x.Id);
            b.Property(x => x.EventType).HasMaxLength(50).IsRequired();
            b.Property(x => x.ProductCode).HasMaxLength(30).IsRequired();
            b.Property(x => x.DebitAccountCode).HasMaxLength(30).IsRequired();
            b.Property(x => x.CreditAccountCode).HasMaxLength(30).IsRequired();
            b.HasIndex(x => new { x.EventType, x.ProductCode, x.EffectiveDate });
        });

        modelBuilder.Entity<JournalEntryEntity>(b =>
        {
            b.ToTable("JournalEntries");
            b.HasKey(x => x.Id);
            b.Property(x => x.SourceModule).HasMaxLength(30).IsRequired();
            b.Property(x => x.SourceReference).HasMaxLength(100).IsRequired();
            b.Property(x => x.EventType).HasMaxLength(50).IsRequired();
            b.Property(x => x.Description).HasMaxLength(250).IsRequired();
            b.Property(x => x.Status).HasMaxLength(20).IsRequired();
            b.Property(x => x.TraceId).HasMaxLength(64);
            b.HasMany(x => x.Lines).WithOne(x => x.JournalEntry).HasForeignKey(x => x.JournalEntryId);
            b.HasIndex(x => new { x.SourceModule, x.SourceReference, x.EventType }).IsUnique();
            b.HasIndex(x => x.BusinessDate);
        });

        modelBuilder.Entity<JournalEntryLineEntity>(b =>
        {
            b.ToTable("JournalEntryLines");
            b.HasKey(x => x.Id);
            b.Property(x => x.CurrencyCode).HasMaxLength(10).IsRequired();
            b.Property(x => x.Description).HasMaxLength(250).IsRequired();
            b.HasOne(x => x.LedgerAccount).WithMany().HasForeignKey(x => x.LedgerAccountId);
            b.HasIndex(x => x.JournalEntryId);
        });

        modelBuilder.Entity<RewardProgramEntity>(b =>
        {
            b.ToTable("RewardPrograms");
            b.HasKey(x => x.Id);
            b.Property(x => x.ProductCode).HasMaxLength(64).IsRequired();
            b.Property(x => x.ProgramName).HasMaxLength(100).IsRequired();
            b.Property(x => x.CurrencyCode).HasMaxLength(10).IsRequired();
            b.HasIndex(x => new { x.ProductCode, x.EffectiveDate });
        });

        modelBuilder.Entity<LoyaltyBalanceEntity>(b =>
        {
            b.ToTable("LoyaltyBalances");
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.AccountId).IsUnique();
            b.HasMany(x => x.Entries).WithOne(x => x.LoyaltyBalance).HasForeignKey(x => x.LoyaltyBalanceId);
        });

        modelBuilder.Entity<LoyaltyEntryEntity>(b =>
        {
            b.ToTable("LoyaltyEntries");
            b.HasKey(x => x.Id);
            b.Property(x => x.SourceType).HasMaxLength(40).IsRequired();
            b.Property(x => x.SourceReference).HasMaxLength(100).IsRequired();
            b.Property(x => x.Description).HasMaxLength(250).IsRequired();
            b.HasIndex(x => new { x.AccountId, x.CreatedOn });
            b.HasIndex(x => new { x.AccountId, x.SourceType, x.SourceReference, x.EntryType }).IsUnique();
        });

        modelBuilder.Entity<RewardCatalogItemEntity>(b =>
        {
            b.ToTable("RewardCatalogItems");
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasMaxLength(40).IsRequired();
            b.Property(x => x.Name).HasMaxLength(120).IsRequired();
            b.Property(x => x.Description).HasMaxLength(300).IsRequired();
            b.HasIndex(x => x.Code).IsUnique();
        });

        modelBuilder.Entity<WalletTokenEntity>(b =>
        {
            b.ToTable("WalletTokens");
            b.HasKey(x => x.Id);
            b.Property(x => x.Provider).HasMaxLength(30).IsRequired();
            b.Property(x => x.DeviceReference).HasMaxLength(120).IsRequired();
            b.Property(x => x.WalletReference).HasMaxLength(120);
            b.Property(x => x.TokenReference).HasMaxLength(64).IsRequired();
            b.Property(x => x.AuthenticationMethod).HasMaxLength(30).IsRequired();
            b.Property(x => x.ActivationCodeHash).HasMaxLength(64);
            b.Property(x => x.ActivationHint).HasMaxLength(12);
            b.HasIndex(x => x.TokenReference).IsUnique();
            b.HasIndex(x => new { x.CardId, x.Provider, x.DeviceReference });
            b.HasMany(x => x.Authorizations).WithOne(x => x.WalletToken).HasForeignKey(x => x.WalletTokenId);
        });

        modelBuilder.Entity<WalletAuthorizationEntity>(b =>
        {
            b.ToTable("WalletAuthorizations");
            b.HasKey(x => x.Id);
            b.Property(x => x.TokenReference).HasMaxLength(64).IsRequired();
            b.Property(x => x.ClientTransactionId).HasMaxLength(80).IsRequired();
            b.Property(x => x.Provider).HasMaxLength(30).IsRequired();
            b.Property(x => x.MerchantId).HasMaxLength(32);
            b.Property(x => x.MerchantCategory).HasMaxLength(8);
            b.Property(x => x.CurrencyCode).HasMaxLength(10).IsRequired();
            b.Property(x => x.ResponseCode).HasMaxLength(4).IsRequired();
            b.Property(x => x.Reason).HasMaxLength(120);
            b.Property(x => x.TraceId).HasMaxLength(64);
            b.HasIndex(x => x.ClientTransactionId).IsUnique();
            b.HasIndex(x => new { x.AccountId, x.AuthorizedOn });
        });

        modelBuilder.Entity<OverlimitEventEntity>(b =>
        {
            b.ToTable("OverlimitEvents");
            b.HasKey(x => x.Id);
            b.Property(x => x.TraceId).HasMaxLength(64);
            b.HasIndex(x => new { x.AccountId, x.CreatedOn });
            b.HasIndex(x => x.HoldId).IsUnique();
        });

        modelBuilder.Entity<CreditLimitProposalEntity>(b =>
        {
            b.ToTable("CreditLimitProposals");
            b.HasKey(x => x.Id);
            b.Property(x => x.DecisionReason).HasMaxLength(200).IsRequired();
            b.HasIndex(x => new { x.AccountId, x.Status, x.CreatedOn });
        });

        // v76 - Early Delinquency (Mora Temprana)
        modelBuilder.Entity<DelinquencyRecordEntity>(b =>
        {
            b.ToTable("DelinquencyRecords");
            b.HasKey(x => x.Id);
            b.Property(x => x.OverdueAmount).HasPrecision(18, 2);
            // Compound index for the daily evaluation query: active records per account
            b.HasIndex(x => new { x.AccountId, x.Status });
            // Index for looking up the specific statement that triggered delinquency
            b.HasIndex(x => x.StatementId);
        });

        modelBuilder.Entity<CardVault.Infrastructure.Persistence.Vault.TokenVaultEntryEntity>(b =>
        {
            b.ToTable("TokenVault");
            b.HasKey(x => x.Id);
            b.Property(x => x.Token).HasMaxLength(32).IsRequired();
            b.Property(x => x.KeyId).HasMaxLength(64).IsRequired();
            b.Property(x => x.NonceB64).HasMaxLength(64).IsRequired();
            b.Property(x => x.CiphertextB64).IsRequired();
            b.Property(x => x.TagB64).HasMaxLength(64).IsRequired();
            b.Property(x => x.MaskedPan).HasMaxLength(19);
            b.Property(x => x.Bin).HasMaxLength(12);
            b.HasIndex(x => x.Token).IsUnique();
            b.HasIndex(x => x.MaskedPan);
        });

        modelBuilder.Entity<CardVault.Infrastructure.Persistence.Vault.VaultSettingsEntity>(b =>
        {
            b.ToTable("VaultSettings");
            b.HasKey(x => x.Id);
        });
    }
}
