using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IsoSwitch.Infrastructure.Persistence.Migrations
{
    public partial class IdempotencyCorrelation : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CorrelationId",
                table: "Transactions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "Transactions",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_IdempotencyKey_TxType",
                table: "Transactions",
                columns: new[] { "IdempotencyKey", "TxType" },
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Transactions_IdempotencyKey_TxType", table: "Transactions");
            migrationBuilder.DropColumn(name: "CorrelationId", table: "Transactions");
            migrationBuilder.DropColumn(name: "IdempotencyKey", table: "Transactions");
        }
    }
}