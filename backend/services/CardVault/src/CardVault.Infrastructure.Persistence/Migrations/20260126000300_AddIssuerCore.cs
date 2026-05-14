using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CardVault.Infrastructure.Persistence.Migrations
{
    public partial class AddIssuerCore : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => { table.PrimaryKey("PK_Customers", x => x.Id); });

            migrationBuilder.CreateIndex(name: "IX_Customers_CustomerNumber", table: "Customers", column: "CustomerNumber", unique: true);
            migrationBuilder.CreateIndex(name: "IX_Customers_DocumentId", table: "Customers", column: "DocumentId", unique: true);

            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountType = table.Column<int>(type: "integer", nullable: false),
                    ProductCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreditLimit = table.Column<decimal>(type: "numeric", nullable: false),
                    AvailableLimit = table.Column<decimal>(type: "numeric", nullable: false),
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

            migrationBuilder.CreateIndex(name: "IX_Accounts_CustomerId_AccountType", table: "Accounts", columns: new[] { "CustomerId", "AccountType" });

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

            migrationBuilder.CreateIndex(name: "IX_Cards_PanToken", table: "Cards", column: "PanToken", unique: true);
            migrationBuilder.CreateIndex(name: "IX_Cards_Last4", table: "Cards", column: "Last4");
            migrationBuilder.CreateIndex(name: "IX_Cards_AccountId", table: "Cards", column: "AccountId");

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

            migrationBuilder.CreateIndex(name: "IX_CardStatusHistory_ChangedOn", table: "CardStatusHistory", column: "ChangedOn");
            migrationBuilder.CreateIndex(name: "IX_CardStatusHistory_CardId", table: "CardStatusHistory", column: "CardId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "CardStatusHistory");
            migrationBuilder.DropTable(name: "Cards");
            migrationBuilder.DropTable(name: "Accounts");
            migrationBuilder.DropTable(name: "Customers");
        }
    }
}
