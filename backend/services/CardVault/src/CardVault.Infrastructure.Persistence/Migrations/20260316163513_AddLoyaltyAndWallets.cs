using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CardVault.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLoyaltyAndWallets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                name: "IX_RewardCatalogItems_Code",
                table: "RewardCatalogItems",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RewardPrograms_ProductCode_EffectiveDate",
                table: "RewardPrograms",
                columns: new[] { "ProductCode", "EffectiveDate" });

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
                name: "LoyaltyEntries");

            migrationBuilder.DropTable(
                name: "RewardCatalogItems");

            migrationBuilder.DropTable(
                name: "RewardPrograms");

            migrationBuilder.DropTable(
                name: "WalletAuthorizations");

            migrationBuilder.DropTable(
                name: "LoyaltyBalances");

            migrationBuilder.DropTable(
                name: "WalletTokens");
        }
    }
}
