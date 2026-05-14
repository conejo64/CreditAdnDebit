using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IsoSwitch.Infrastructure.Persistence.Migrations
{
    public partial class TxIsoCachedFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "ProcessingCode", table: "Transactions", type: "text", nullable: true);
            migrationBuilder.AddColumn<string>(name: "Amount12", table: "Transactions", type: "text", nullable: true);
            migrationBuilder.AddColumn<string>(name: "Currency", table: "Transactions", type: "text", nullable: true);
            migrationBuilder.AddColumn<string>(name: "TerminalId", table: "Transactions", type: "text", nullable: true);
            migrationBuilder.AddColumn<string>(name: "MerchantId", table: "Transactions", type: "text", nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ProcessingCode", table: "Transactions");
            migrationBuilder.DropColumn(name: "Amount12", table: "Transactions");
            migrationBuilder.DropColumn(name: "Currency", table: "Transactions");
            migrationBuilder.DropColumn(name: "TerminalId", table: "Transactions");
            migrationBuilder.DropColumn(name: "MerchantId", table: "Transactions");
        }
    }
}