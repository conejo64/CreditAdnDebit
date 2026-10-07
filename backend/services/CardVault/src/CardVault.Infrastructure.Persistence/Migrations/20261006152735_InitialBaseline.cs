using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CardVault.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountingMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ProductCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    DebitAccountCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreditAccountCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountingMappings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AccountLimits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    DailyAtmLimit = table.Column<decimal>(type: "numeric", nullable: false),
                    DailyPosLimit = table.Column<decimal>(type: "numeric", nullable: false),
                    DailyEcommerceLimit = table.Column<decimal>(type: "numeric", nullable: false),
                    DailyAtmAuculated = table.Column<decimal>(type: "numeric", nullable: false),
                    DailyPosAccumulated = table.Column<decimal>(type: "numeric", nullable: false),
                    DailyEcommerceAccumulated = table.Column<decimal>(type: "numeric", nullable: false),
                    LastResetDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountLimits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AntifraudRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    TargetValue = table.Column<string>(type: "text", nullable: false),
                    RiskScore = table.Column<decimal>(type: "numeric", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AntifraudRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Service = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EventType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TraceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    OccurredOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    PayloadSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuthorizationHolds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Network = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Stan = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Rrn = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OriginalDataElements90 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    CapturedAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    MerchantId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    MerchantCategory = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AuthorizedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CapturedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReleasedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    HoldLedgerEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    CaptureLedgerEntryId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthorizationHolds", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BinRanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BinStart = table.Column<int>(type: "integer", nullable: false),
                    BinEnd = table.Column<int>(type: "integer", nullable: false),
                    Brand = table.Column<string>(type: "text", nullable: false),
                    Product = table.Column<string>(type: "text", nullable: false),
                    IssuerName = table.Column<string>(type: "text", nullable: true),
                    CountryCode = table.Column<string>(type: "text", nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BinRanges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CardProducts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Brand = table.Column<string>(type: "text", nullable: false),
                    ProductType = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultInstallmentApr = table.Column<decimal>(type: "numeric", nullable: true),
                    MaxInstallmentApr = table.Column<decimal>(type: "numeric", nullable: true),
                    UpdatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardProducts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContactAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DelinquencyRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<int>(type: "integer", nullable: false),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    AttemptedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AttemptedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContactAttempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Countries",
                columns: table => new
                {
                    Code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    NumericCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Countries", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "CreditLimitProposals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentLimit = table.Column<decimal>(type: "numeric", nullable: false),
                    ProposedIncreaseAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    ProposedLimit = table.Column<decimal>(type: "numeric", nullable: false),
                    OnTimePaymentRatio = table.Column<decimal>(type: "numeric", nullable: false),
                    AverageUtilizationRatio = table.Column<decimal>(type: "numeric", nullable: false),
                    StatementsReviewed = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    DecisionReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AppliedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditLimitProposals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CreditPolicies",
                columns: table => new
                {
                    ProductCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MinPaymentPercent = table.Column<decimal>(type: "numeric", nullable: false),
                    MinPaymentAbsolute = table.Column<decimal>(type: "numeric", nullable: false),
                    GraceDays = table.Column<int>(type: "integer", nullable: false),
                    HoldTtlHours = table.Column<int>(type: "integer", nullable: false),
                    FloorLimit = table.Column<decimal>(type: "numeric", nullable: false),
                    AllowOverlimit = table.Column<bool>(type: "boolean", nullable: false),
                    OverlimitBufferAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    InterestApr = table.Column<decimal>(type: "numeric", nullable: false),
                    PurchaseApr = table.Column<decimal>(type: "numeric", nullable: false),
                    CashAdvanceApr = table.Column<decimal>(type: "numeric", nullable: false),
                    PenaltyApr = table.Column<decimal>(type: "numeric", nullable: false),
                    PurchaseGraceDays = table.Column<int>(type: "integer", nullable: false),
                    LateFee = table.Column<decimal>(type: "numeric", nullable: false),
                    OverlimitFee = table.Column<decimal>(type: "numeric", nullable: false),
                    OverlimitFeeOncePerDay = table.Column<bool>(type: "boolean", nullable: false),
                    AutoIncreasePercent = table.Column<decimal>(type: "numeric", nullable: false),
                    AutoIncreaseMinStatements = table.Column<int>(type: "integer", nullable: false),
                    AutoIncreaseMinOnTimeRatio = table.Column<decimal>(type: "numeric", nullable: false),
                    AutoIncreaseMinUtilization = table.Column<decimal>(type: "numeric", nullable: false),
                    AnnualFee = table.Column<decimal>(type: "numeric", nullable: false),
                    CashAdvanceFeeFixed = table.Column<decimal>(type: "numeric", nullable: false),
                    CashAdvanceFeePercent = table.Column<decimal>(type: "numeric", nullable: false),
                    UpdatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditPolicies", x => x.ProductCode);
                });

            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FullName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    DocumentId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Email = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DocumentType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Gender = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BillingAddress = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StatementAddress = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ResidenceCity = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StatementCity = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CardDeliveryCity = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DelinquencyNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DelinquencyRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    Content = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DelinquencyNotes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DelinquencyRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    StatementId = table.Column<Guid>(type: "uuid", nullable: false),
                    OverdueAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DaysInArrears = table.Column<int>(type: "integer", nullable: false),
                    Bucket = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DelinquencyRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DisputeCases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalTxnJournalId = table.Column<Guid>(type: "uuid", nullable: true),
                    Network = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Stan = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Rrn = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OriginalAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ProvisionalCreditLedgerEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    OpenedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DisputeCases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DisputeEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DisputeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Notes = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DisputeEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FeeAssessments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    FeeType = table.Column<int>(type: "integer", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    LedgerEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeeAssessments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InstallmentPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalInstallments = table.Column<int>(type: "integer", nullable: false),
                    RemainingInstallments = table.Column<int>(type: "integer", nullable: false),
                    InterestApr = table.Column<decimal>(type: "numeric", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    OriginalLedgerEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstallmentPlans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InterestAccrualRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccrualDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Segment = table.Column<int>(type: "integer", nullable: false),
                    BalanceBase = table.Column<decimal>(type: "numeric", nullable: false),
                    Apr = table.Column<decimal>(type: "numeric", nullable: false),
                    DailyRate = table.Column<decimal>(type: "numeric", nullable: false),
                    InterestAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    LedgerEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InterestAccrualRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JournalEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SourceModule = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SourceReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EventType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TraceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PostedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LedgerAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    AccountName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    AccountType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LoyaltyBalances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CashbackBalance = table.Column<decimal>(type: "numeric", nullable: false),
                    PointsBalance = table.Column<decimal>(type: "numeric", nullable: false),
                    UpdatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoyaltyBalances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MccRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Mcc = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    IsBlocked = table.Column<bool>(type: "boolean", nullable: false),
                    PerTxnLimit = table.Column<decimal>(type: "numeric", nullable: true),
                    Description = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MccRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MinimumPaymentPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    FloorAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    PrincipalPercent = table.Column<decimal>(type: "numeric", nullable: false),
                    CeilingAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    IncludeInterest = table.Column<bool>(type: "boolean", nullable: false),
                    IncludeFees = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MinimumPaymentPolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OpenBankingClients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    SecretHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AllowedScopes = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    AllowAllAccounts = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastTokenIssuedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenBankingClients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Topic = table.Column<string>(type: "text", nullable: false),
                    Key = table.Column<string>(type: "text", nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    ProcessedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OverlimitEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    HoldId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    AvailableCreditBefore = table.Column<decimal>(type: "numeric", nullable: false),
                    OverlimitAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    TraceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OverlimitEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentAllocationPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Order = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentAllocationPolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RefundRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Network = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Rrn = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    Stan = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    LedgerEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    PostedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefundRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RewardCatalogItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    PointsCost = table.Column<decimal>(type: "numeric", nullable: false),
                    CashbackCost = table.Column<decimal>(type: "numeric", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RewardCatalogItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RewardPrograms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProgramName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CashbackRate = table.Column<decimal>(type: "numeric", nullable: false),
                    PointsPerCurrencyUnit = table.Column<decimal>(type: "numeric", nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RewardPrograms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RoutingRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    BinStart = table.Column<int>(type: "integer", nullable: false),
                    BinEnd = table.Column<int>(type: "integer", nullable: false),
                    Country = table.Column<string>(type: "text", nullable: true),
                    Mcc = table.Column<string>(type: "text", nullable: true),
                    MerchantId = table.Column<string>(type: "text", nullable: true),
                    AmountMin = table.Column<decimal>(type: "numeric", nullable: true),
                    AmountMax = table.Column<decimal>(type: "numeric", nullable: true),
                    ConnectorId = table.Column<string>(type: "text", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoutingRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SettlementBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Network = table.Column<int>(type: "integer", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TxnCount = table.Column<int>(type: "integer", nullable: false),
                    GrossAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SettlementBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Statements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CycleStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CycleEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StatementDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PreviousBalance = table.Column<decimal>(type: "numeric", nullable: false),
                    Purchases = table.Column<decimal>(type: "numeric", nullable: false),
                    Payments = table.Column<decimal>(type: "numeric", nullable: false),
                    Fees = table.Column<decimal>(type: "numeric", nullable: false),
                    Interest = table.Column<decimal>(type: "numeric", nullable: false),
                    InterestAccrued = table.Column<decimal>(type: "numeric", nullable: false),
                    StatementBalance = table.Column<decimal>(type: "numeric", nullable: false),
                    NewBalance = table.Column<decimal>(type: "numeric", nullable: false),
                    MinimumPayment = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalPaymentDue = table.Column<decimal>(type: "numeric", nullable: false),
                    AverageDailyBalance = table.Column<decimal>(type: "numeric", nullable: false),
                    InterestApr = table.Column<decimal>(type: "numeric", nullable: false),
                    InterestDays = table.Column<int>(type: "integer", nullable: false),
                    PrincipalDue = table.Column<decimal>(type: "numeric", nullable: false),
                    InterestDue = table.Column<decimal>(type: "numeric", nullable: false),
                    FeesDue = table.Column<decimal>(type: "numeric", nullable: false),
                    PaidAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    PaidToPrincipal = table.Column<decimal>(type: "numeric", nullable: false),
                    PaidToInterest = table.Column<decimal>(type: "numeric", nullable: false),
                    PaidToFees = table.Column<decimal>(type: "numeric", nullable: false),
                    LateFeeAppliedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LateFeeAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Statements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TokenVault",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Token = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    KeyId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NonceB64 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CiphertextB64 = table.Column<string>(type: "text", nullable: false),
                    TagB64 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MaskedPan = table.Column<string>(type: "character varying(19)", maxLength: 19, nullable: true),
                    Bin = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastAccessedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TokenVault", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TxnJournal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Network = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Mti = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    Stan = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    Rrn = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    TxnType = table.Column<int>(type: "integer", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    LedgerEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PostedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TxnJournal", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VaultSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActiveKeyId = table.Column<string>(type: "text", nullable: false),
                    UpdatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastReencryptRunOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastReencryptUpdated = table.Column<int>(type: "integer", nullable: false),
                    LastReencryptStatus = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VaultSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VelocityRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    WindowMinutes = table.Column<int>(type: "integer", nullable: false),
                    MaxCount = table.Column<int>(type: "integer", nullable: false),
                    MaxAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    Description = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VelocityRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WalletTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CardId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    DeviceReference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    WalletReference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    TokenReference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AuthenticationMethod = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ActivationCodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ActivationHint = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    ActivationExpiresOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ActivatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastUsedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletTokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AccountType = table.Column<int>(type: "integer", nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ProductCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreditLimit = table.Column<decimal>(type: "numeric", nullable: false),
                    AvailableLimit = table.Column<decimal>(type: "numeric", nullable: false),
                    LedgerBalance = table.Column<decimal>(type: "numeric", nullable: false),
                    HoldBalance = table.Column<decimal>(type: "numeric", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Accounts_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CustomerNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    CardId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Severity = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    Message = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: true),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    MerchantName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    SourceEvent = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TraceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReadOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerNotifications_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AmortizationSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    InstallmentNumber = table.Column<int>(type: "integer", nullable: false),
                    PrincipalAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    InterestAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalInstallmentAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    DueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    BilledStatementId = table.Column<Guid>(type: "uuid", nullable: true),
                    BilledOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PaidOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AmortizationSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AmortizationSchedules_InstallmentPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "InstallmentPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JournalEntryLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    LedgerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    DebitAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    CreditAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Description = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalEntryLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JournalEntryLines_JournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_JournalEntryLines_LedgerAccounts_LedgerAccountId",
                        column: x => x.LedgerAccountId,
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LoyaltyEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LoyaltyBalanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntryType = table.Column<int>(type: "integer", nullable: false),
                    CashbackAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    PointsAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    SourceType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SourceReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoyaltyEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LoyaltyEntries_LoyaltyBalances_LoyaltyBalanceId",
                        column: x => x.LoyaltyBalanceId,
                        principalTable: "LoyaltyBalances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OpenBankingClientAccountAccesses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientEntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenBankingClientAccountAccesses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpenBankingClientAccountAccesses_OpenBankingClients_ClientE~",
                        column: x => x.ClientEntityId,
                        principalTable: "OpenBankingClients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SettlementItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    LedgerEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    NetworkRef = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PostedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SettlementItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SettlementItems_SettlementBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "SettlementBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LedgerEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PostedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StatementId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_Statements_StatementId",
                        column: x => x.StatementId,
                        principalTable: "Statements",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "WalletAuthorizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WalletTokenId = table.Column<Guid>(type: "uuid", nullable: true),
                    TokenReference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ClientTransactionId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    CardId = table.Column<Guid>(type: "uuid", nullable: true),
                    Provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    MerchantId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    MerchantCategory = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    DeviceAuthenticated = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ResponseCode = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    Reason = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    TraceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    HoldId = table.Column<Guid>(type: "uuid", nullable: true),
                    AuthorizedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletAuthorizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WalletAuthorizations_WalletTokens_WalletTokenId",
                        column: x => x.WalletTokenId,
                        principalTable: "WalletTokens",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Cards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Bin = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    PanToken = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MaskedPan = table.Column<string>(type: "character varying(19)", maxLength: 19, nullable: false),
                    ExpiryYyMm = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    Last4 = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PinHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    PinRetryCount = table.Column<int>(type: "integer", nullable: false),
                    PinBlockedUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PinHashAlgorithm = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    PinHashParams = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    PinSalt = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Cards_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CustomerNotificationDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NotificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<int>(type: "integer", nullable: false),
                    DestinationMasked = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    DestinationHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    ProviderReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    LastError = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    LastAttemptOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeliveredOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SendingStartedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NextAttemptOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProviderId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationKeyId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DestinationNonceB64 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DestinationCipherB64 = table.Column<string>(type: "text", nullable: true),
                    DestinationTagB64 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerNotificationDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerNotificationDeliveries_CustomerNotifications_Notifi~",
                        column: x => x.NotificationId,
                        principalTable: "CustomerNotifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StatementLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StatementId = table.Column<Guid>(type: "uuid", nullable: false),
                    LedgerEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    PostedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatementLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StatementLines_LedgerEntries_LedgerEntryId",
                        column: x => x.LedgerEntryId,
                        principalTable: "LedgerEntries",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StatementLines_Statements_StatementId",
                        column: x => x.StatementId,
                        principalTable: "Statements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CardStatusHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CardId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStatus = table.Column<int>(type: "integer", nullable: false),
                    ToStatus = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ChangedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardStatusHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CardStatusHistory_Cards_CardId",
                        column: x => x.CardId,
                        principalTable: "Cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ThreeDsChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CardId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    MaskedPan = table.Column<string>(type: "character varying(19)", maxLength: 19, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    MerchantId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MerchantName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    MerchantCountry = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    BrowserIpCountry = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    DeviceChannel = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    RiskScore = table.Column<int>(type: "integer", nullable: false),
                    RiskReasonsJson = table.Column<string>(type: "text", nullable: false),
                    ContactHint = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    OtpHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OtpSalt = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OtpAttempts = table.Column<int>(type: "integer", nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Decision = table.Column<int>(type: "integer", nullable: false),
                    DecisionReason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RequestedBy = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    TraceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AuthenticatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ThreeDsChallenges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ThreeDsChallenges_Cards_CardId",
                        column: x => x.CardId,
                        principalTable: "Cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountingMappings_EventType_ProductCode_EffectiveDate",
                table: "AccountingMappings",
                columns: new[] { "EventType", "ProductCode", "EffectiveDate" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountLimits_AccountId",
                table: "AccountLimits",
                column: "AccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_CustomerId_AccountType",
                table: "Accounts",
                columns: new[] { "CustomerId", "AccountType" });

            migrationBuilder.CreateIndex(
                name: "IX_AmortizationSchedules_PlanId_InstallmentNumber",
                table: "AmortizationSchedules",
                columns: new[] { "PlanId", "InstallmentNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AmortizationSchedules_Status_DueDate",
                table: "AmortizationSchedules",
                columns: new[] { "Status", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_EventType",
                table: "AuditEvents",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_OccurredOn",
                table: "AuditEvents",
                column: "OccurredOn");

            migrationBuilder.CreateIndex(
                name: "IX_BinRanges_BinStart_BinEnd",
                table: "BinRanges",
                columns: new[] { "BinStart", "BinEnd" });

            migrationBuilder.CreateIndex(
                name: "IX_BinRanges_Brand",
                table: "BinRanges",
                column: "Brand");

            migrationBuilder.CreateIndex(
                name: "IX_CardProducts_Code",
                table: "CardProducts",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cards_AccountId",
                table: "Cards",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Cards_Last4",
                table: "Cards",
                column: "Last4");

            migrationBuilder.CreateIndex(
                name: "IX_Cards_PanToken",
                table: "Cards",
                column: "PanToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CardStatusHistory_CardId",
                table: "CardStatusHistory",
                column: "CardId");

            migrationBuilder.CreateIndex(
                name: "IX_CardStatusHistory_ChangedOn",
                table: "CardStatusHistory",
                column: "ChangedOn");

            migrationBuilder.CreateIndex(
                name: "IX_ContactAttempts_DelinquencyRecordId",
                table: "ContactAttempts",
                column: "DelinquencyRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditLimitProposals_AccountId_Status_CreatedOn",
                table: "CreditLimitProposals",
                columns: new[] { "AccountId", "Status", "CreatedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_CreditPolicies_ProductCode",
                table: "CreditPolicies",
                column: "ProductCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerNotificationDeliveries_NotificationId_Channel",
                table: "CustomerNotificationDeliveries",
                columns: new[] { "NotificationId", "Channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerNotificationDeliveries_Status_CreatedOn",
                table: "CustomerNotificationDeliveries",
                columns: new[] { "Status", "CreatedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerNotificationDeliveries_Status_NextAttemptOn",
                table: "CustomerNotificationDeliveries",
                columns: new[] { "Status", "NextAttemptOn" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerNotificationDeliveries_Status_SendingStartedOn",
                table: "CustomerNotificationDeliveries",
                columns: new[] { "Status", "SendingStartedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerNotificationDeliveries_TenantId",
                table: "CustomerNotificationDeliveries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerNotifications_CustomerId_CreatedOn",
                table: "CustomerNotifications",
                columns: new[] { "CustomerId", "CreatedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerNotifications_Type_CreatedOn",
                table: "CustomerNotifications",
                columns: new[] { "Type", "CreatedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_CustomerNumber",
                table: "Customers",
                column: "CustomerNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_DocumentId",
                table: "Customers",
                column: "DocumentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DelinquencyNotes_DelinquencyRecordId",
                table: "DelinquencyNotes",
                column: "DelinquencyRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_DelinquencyRecords_AccountId_Status",
                table: "DelinquencyRecords",
                columns: new[] { "AccountId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_DelinquencyRecords_StatementId",
                table: "DelinquencyRecords",
                column: "StatementId");

            migrationBuilder.CreateIndex(
                name: "IX_DisputeCases_AccountId",
                table: "DisputeCases",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_DisputeCases_Network_Rrn",
                table: "DisputeCases",
                columns: new[] { "Network", "Rrn" });

            migrationBuilder.CreateIndex(
                name: "IX_DisputeEvents_DisputeId_CreatedOn",
                table: "DisputeEvents",
                columns: new[] { "DisputeId", "CreatedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_FeeAssessments_AccountId_FeeType_BusinessDate",
                table: "FeeAssessments",
                columns: new[] { "AccountId", "FeeType", "BusinessDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InstallmentPlans_AccountId",
                table: "InstallmentPlans",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_InterestAccrualRecords_AccountId_AccrualDate_Segment",
                table: "InterestAccrualRecords",
                columns: new[] { "AccountId", "AccrualDate", "Segment" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_BusinessDate",
                table: "JournalEntries",
                column: "BusinessDate");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_SourceModule_SourceReference_EventType",
                table: "JournalEntries",
                columns: new[] { "SourceModule", "SourceReference", "EventType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLines_JournalEntryId",
                table: "JournalEntryLines",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLines_LedgerAccountId",
                table: "JournalEntryLines",
                column: "LedgerAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerAccounts_AccountCode",
                table: "LedgerAccounts",
                column: "AccountCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_AccountId_PostedOn",
                table: "LedgerEntries",
                columns: new[] { "AccountId", "PostedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_StatementId",
                table: "LedgerEntries",
                column: "StatementId");

            migrationBuilder.CreateIndex(
                name: "IX_LoyaltyBalances_AccountId",
                table: "LoyaltyBalances",
                column: "AccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LoyaltyEntries_AccountId_CreatedOn",
                table: "LoyaltyEntries",
                columns: new[] { "AccountId", "CreatedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_LoyaltyEntries_AccountId_SourceType_SourceReference_EntryTy~",
                table: "LoyaltyEntries",
                columns: new[] { "AccountId", "SourceType", "SourceReference", "EntryType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LoyaltyEntries_LoyaltyBalanceId",
                table: "LoyaltyEntries",
                column: "LoyaltyBalanceId");

            migrationBuilder.CreateIndex(
                name: "IX_MccRules_Mcc",
                table: "MccRules",
                column: "Mcc",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MinimumPaymentPolicies_Code",
                table: "MinimumPaymentPolicies",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpenBankingClientAccountAccesses_AccountId",
                table: "OpenBankingClientAccountAccesses",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_OpenBankingClientAccountAccesses_ClientEntityId_AccountId",
                table: "OpenBankingClientAccountAccesses",
                columns: new[] { "ClientEntityId", "AccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpenBankingClients_ClientId",
                table: "OpenBankingClients",
                column: "ClientId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_ProcessedOn",
                table: "OutboxMessages",
                column: "ProcessedOn");

            migrationBuilder.CreateIndex(
                name: "IX_OverlimitEvents_AccountId_CreatedOn",
                table: "OverlimitEvents",
                columns: new[] { "AccountId", "CreatedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_OverlimitEvents_HoldId",
                table: "OverlimitEvents",
                column: "HoldId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAllocationPolicies_Code",
                table: "PaymentAllocationPolicies",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefundRecords_AccountId",
                table: "RefundRecords",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_RefundRecords_Network_Rrn_Stan",
                table: "RefundRecords",
                columns: new[] { "Network", "Rrn", "Stan" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RewardCatalogItems_Code",
                table: "RewardCatalogItems",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RewardPrograms_ProductCode_EffectiveDate",
                table: "RewardPrograms",
                columns: new[] { "ProductCode", "EffectiveDate" });

            migrationBuilder.CreateIndex(
                name: "IX_RoutingRules_BinStart_BinEnd",
                table: "RoutingRules",
                columns: new[] { "BinStart", "BinEnd" });

            migrationBuilder.CreateIndex(
                name: "IX_RoutingRules_Priority",
                table: "RoutingRules",
                column: "Priority");

            migrationBuilder.CreateIndex(
                name: "IX_SettlementBatches_Network_BusinessDate",
                table: "SettlementBatches",
                columns: new[] { "Network", "BusinessDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SettlementItems_BatchId_PostedOn",
                table: "SettlementItems",
                columns: new[] { "BatchId", "PostedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_StatementLines_LedgerEntryId",
                table: "StatementLines",
                column: "LedgerEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_StatementLines_StatementId_PostedOn",
                table: "StatementLines",
                columns: new[] { "StatementId", "PostedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_Statements_AccountId_StatementDate",
                table: "Statements",
                columns: new[] { "AccountId", "StatementDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ThreeDsChallenges_CardId_CreatedOn",
                table: "ThreeDsChallenges",
                columns: new[] { "CardId", "CreatedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_ThreeDsChallenges_Status_CreatedOn",
                table: "ThreeDsChallenges",
                columns: new[] { "Status", "CreatedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_ThreeDsChallenges_TraceId",
                table: "ThreeDsChallenges",
                column: "TraceId");

            migrationBuilder.CreateIndex(
                name: "IX_TokenVault_MaskedPan",
                table: "TokenVault",
                column: "MaskedPan");

            migrationBuilder.CreateIndex(
                name: "IX_TokenVault_Token",
                table: "TokenVault",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TxnJournal_AccountId",
                table: "TxnJournal",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_TxnJournal_Network_Mti_Stan_Rrn",
                table: "TxnJournal",
                columns: new[] { "Network", "Mti", "Stan", "Rrn" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VelocityRules_ProductCode",
                table: "VelocityRules",
                column: "ProductCode");

            migrationBuilder.CreateIndex(
                name: "IX_WalletAuthorizations_AccountId_AuthorizedOn",
                table: "WalletAuthorizations",
                columns: new[] { "AccountId", "AuthorizedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_WalletAuthorizations_ClientTransactionId",
                table: "WalletAuthorizations",
                column: "ClientTransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WalletAuthorizations_WalletTokenId",
                table: "WalletAuthorizations",
                column: "WalletTokenId");

            migrationBuilder.CreateIndex(
                name: "IX_WalletTokens_CardId_Provider_DeviceReference",
                table: "WalletTokens",
                columns: new[] { "CardId", "Provider", "DeviceReference" });

            migrationBuilder.CreateIndex(
                name: "IX_WalletTokens_TokenReference",
                table: "WalletTokens",
                column: "TokenReference",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountingMappings");

            migrationBuilder.DropTable(
                name: "AccountLimits");

            migrationBuilder.DropTable(
                name: "AmortizationSchedules");

            migrationBuilder.DropTable(
                name: "AntifraudRules");

            migrationBuilder.DropTable(
                name: "AuditEvents");

            migrationBuilder.DropTable(
                name: "AuthorizationHolds");

            migrationBuilder.DropTable(
                name: "BinRanges");

            migrationBuilder.DropTable(
                name: "CardProducts");

            migrationBuilder.DropTable(
                name: "CardStatusHistory");

            migrationBuilder.DropTable(
                name: "ContactAttempts");

            migrationBuilder.DropTable(
                name: "Countries");

            migrationBuilder.DropTable(
                name: "CreditLimitProposals");

            migrationBuilder.DropTable(
                name: "CreditPolicies");

            migrationBuilder.DropTable(
                name: "CustomerNotificationDeliveries");

            migrationBuilder.DropTable(
                name: "DelinquencyNotes");

            migrationBuilder.DropTable(
                name: "DelinquencyRecords");

            migrationBuilder.DropTable(
                name: "DisputeCases");

            migrationBuilder.DropTable(
                name: "DisputeEvents");

            migrationBuilder.DropTable(
                name: "FeeAssessments");

            migrationBuilder.DropTable(
                name: "InterestAccrualRecords");

            migrationBuilder.DropTable(
                name: "JournalEntryLines");

            migrationBuilder.DropTable(
                name: "LoyaltyEntries");

            migrationBuilder.DropTable(
                name: "MccRules");

            migrationBuilder.DropTable(
                name: "MinimumPaymentPolicies");

            migrationBuilder.DropTable(
                name: "OpenBankingClientAccountAccesses");

            migrationBuilder.DropTable(
                name: "OutboxMessages");

            migrationBuilder.DropTable(
                name: "OverlimitEvents");

            migrationBuilder.DropTable(
                name: "PaymentAllocationPolicies");

            migrationBuilder.DropTable(
                name: "RefundRecords");

            migrationBuilder.DropTable(
                name: "RewardCatalogItems");

            migrationBuilder.DropTable(
                name: "RewardPrograms");

            migrationBuilder.DropTable(
                name: "RoutingRules");

            migrationBuilder.DropTable(
                name: "SettlementItems");

            migrationBuilder.DropTable(
                name: "StatementLines");

            migrationBuilder.DropTable(
                name: "ThreeDsChallenges");

            migrationBuilder.DropTable(
                name: "TokenVault");

            migrationBuilder.DropTable(
                name: "TxnJournal");

            migrationBuilder.DropTable(
                name: "VaultSettings");

            migrationBuilder.DropTable(
                name: "VelocityRules");

            migrationBuilder.DropTable(
                name: "WalletAuthorizations");

            migrationBuilder.DropTable(
                name: "InstallmentPlans");

            migrationBuilder.DropTable(
                name: "CustomerNotifications");

            migrationBuilder.DropTable(
                name: "JournalEntries");

            migrationBuilder.DropTable(
                name: "LedgerAccounts");

            migrationBuilder.DropTable(
                name: "LoyaltyBalances");

            migrationBuilder.DropTable(
                name: "OpenBankingClients");

            migrationBuilder.DropTable(
                name: "SettlementBatches");

            migrationBuilder.DropTable(
                name: "LedgerEntries");

            migrationBuilder.DropTable(
                name: "Cards");

            migrationBuilder.DropTable(
                name: "WalletTokens");

            migrationBuilder.DropTable(
                name: "Statements");

            migrationBuilder.DropTable(
                name: "Accounts");

            migrationBuilder.DropTable(
                name: "Customers");
        }
    }
}
