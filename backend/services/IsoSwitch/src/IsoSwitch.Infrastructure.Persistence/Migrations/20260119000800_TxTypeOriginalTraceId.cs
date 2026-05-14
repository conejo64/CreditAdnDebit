using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IsoSwitch.Infrastructure.Persistence.Migrations
{
    public partial class TxTypeOriginalTraceId : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TxType",
                table: "Transactions",
                type: "text",
                nullable: false,
                defaultValue: "AUTH");

            migrationBuilder.AddColumn<string>(
                name: "OriginalTraceId",
                table: "Transactions",
                type: "text",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "TxType", table: "Transactions");
            migrationBuilder.DropColumn(name: "OriginalTraceId", table: "Transactions");
        }
    }
}