using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CardVault.Infrastructure.Persistence.Migrations
{
    public partial class AddBillingLedger : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CreditPolicies",
                columns: table => new
                {
                    ProductCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MinPaymentPercent = table.Column<decimal>(type: "numeric", nullable: false),
                    MinPaymentAbsolute = table.Column<decimal>(type: "numeric", nullable: false),
                    GraceDays = table.Column<int>(type: "integer", nullable: false),
                    InterestApr = table.Column<decimal>(type: "numeric", nullable: false),
                    LateFee = table.Column<decimal>(type: "numeric", nullable: false),
                    UpdatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => { table.PrimaryKey("PK_CreditPolicies", x => x.ProductCode); });

            migrationBuilder.CreateTable(
                name: "Statements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CycleStart = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CycleEnd = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    StatementDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    DueDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    PreviousBalance = table.Column<decimal>(type: "numeric", nullable: false),
                    Purchases = table.Column<decimal>(type: "numeric", nullable: false),
                    Payments = table.Column<decimal>(type: "numeric", nullable: false),
                    Fees = table.Column<decimal>(type: "numeric", nullable: false),
                    Interest = table.Column<decimal>(type: "numeric", nullable: false),
                    NewBalance = table.Column<decimal>(type: "numeric", nullable: false),
                    MinimumPayment = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalPaymentDue = table.Column<decimal>(type: "numeric", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => { table.PrimaryKey("PK_Statements", x => x.Id); });

            migrationBuilder.CreateIndex(
                name: "IX_Statements_AccountId_StatementDate",
                table: "Statements",
                columns: new[] { "AccountId", "StatementDate" });

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

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_AccountId_PostedOn",
                table: "LedgerEntries",
                columns: new[] { "AccountId", "PostedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_StatementId",
                table: "LedgerEntries",
                column: "StatementId");

            migrationBuilder.CreateTable(
                name: "StatementLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StatementId = table.Column<Guid>(type: "uuid", nullable: false),
                    LedgerEntryId = table.Column<Guid>(type: "uuid", nullable: false),
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
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StatementLines_Statements_StatementId",
                        column: x => x.StatementId,
                        principalTable: "Statements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StatementLines_StatementId_PostedOn",
                table: "StatementLines",
                columns: new[] { "StatementId", "PostedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_StatementLines_LedgerEntryId",
                table: "StatementLines",
                column: "LedgerEntryId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "StatementLines");
            migrationBuilder.DropTable(name: "LedgerEntries");
            migrationBuilder.DropTable(name: "CreditPolicies");
            migrationBuilder.DropTable(name: "Statements");
        }
    }
}
