using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CardVault.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEcommerceThreeDs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ThreeDsChallenges");
        }
    }
}
