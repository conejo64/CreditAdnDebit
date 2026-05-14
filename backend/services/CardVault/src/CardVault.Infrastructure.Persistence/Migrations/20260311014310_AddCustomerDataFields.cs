using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CardVault.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerDataFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "NumericCode",
                table: "Countries",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(3)",
                oldMaxLength: 3,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Countries",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Currency",
                table: "Countries",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(3)",
                oldMaxLength: 3,
                oldNullable: true);

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
                    InterestApr = table.Column<decimal>(type: "numeric", nullable: false),
                    PurchaseApr = table.Column<decimal>(type: "numeric", nullable: false),
                    CashAdvanceApr = table.Column<decimal>(type: "numeric", nullable: false),
                    PenaltyApr = table.Column<decimal>(type: "numeric", nullable: false),
                    PurchaseGraceDays = table.Column<int>(type: "integer", nullable: false),
                    LateFee = table.Column<decimal>(type: "numeric", nullable: false),
                    OverlimitFee = table.Column<decimal>(type: "numeric", nullable: false),
                    OverlimitFeeOncePerDay = table.Column<bool>(type: "boolean", nullable: false),
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
                name: "TokenVaultEntryEntity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Token = table.Column<string>(type: "text", nullable: false),
                    KeyId = table.Column<string>(type: "text", nullable: false),
                    NonceB64 = table.Column<string>(type: "text", nullable: false),
                    CiphertextB64 = table.Column<string>(type: "text", nullable: false),
                    TagB64 = table.Column<string>(type: "text", nullable: false),
                    MaskedPan = table.Column<string>(type: "text", nullable: true),
                    Bin = table.Column<string>(type: "text", nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastAccessedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TokenVaultEntryEntity", x => x.Id);
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
                name: "IX_CreditPolicies_ProductCode",
                table: "CreditPolicies",
                column: "ProductCode",
                unique: true);

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
                name: "IX_LedgerEntries_AccountId_PostedOn",
                table: "LedgerEntries",
                columns: new[] { "AccountId", "PostedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_StatementId",
                table: "LedgerEntries",
                column: "StatementId");

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
                name: "CardStatusHistory");

            migrationBuilder.DropTable(
                name: "CreditPolicies");

            migrationBuilder.DropTable(
                name: "DisputeCases");

            migrationBuilder.DropTable(
                name: "DisputeEvents");

            migrationBuilder.DropTable(
                name: "FeeAssessments");

            migrationBuilder.DropTable(
                name: "InterestAccrualRecords");

            migrationBuilder.DropTable(
                name: "MccRules");

            migrationBuilder.DropTable(
                name: "MinimumPaymentPolicies");

            migrationBuilder.DropTable(
                name: "PaymentAllocationPolicies");

            migrationBuilder.DropTable(
                name: "RefundRecords");

            migrationBuilder.DropTable(
                name: "SettlementItems");

            migrationBuilder.DropTable(
                name: "StatementLines");

            migrationBuilder.DropTable(
                name: "TokenVaultEntryEntity");

            migrationBuilder.DropTable(
                name: "TxnJournal");

            migrationBuilder.DropTable(
                name: "VaultSettings");

            migrationBuilder.DropTable(
                name: "VelocityRules");

            migrationBuilder.DropTable(
                name: "InstallmentPlans");

            migrationBuilder.DropTable(
                name: "Cards");

            migrationBuilder.DropTable(
                name: "SettlementBatches");

            migrationBuilder.DropTable(
                name: "LedgerEntries");

            migrationBuilder.DropTable(
                name: "Accounts");

            migrationBuilder.DropTable(
                name: "Statements");

            migrationBuilder.DropTable(
                name: "Customers");

            migrationBuilder.AlterColumn<string>(
                name: "NumericCode",
                table: "Countries",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(3)",
                oldMaxLength: 3);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Countries",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);

            migrationBuilder.AlterColumn<string>(
                name: "Currency",
                table: "Countries",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(3)",
                oldMaxLength: 3);
        }
    }
}
