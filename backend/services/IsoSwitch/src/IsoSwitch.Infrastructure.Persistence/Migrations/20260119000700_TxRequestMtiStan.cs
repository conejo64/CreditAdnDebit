using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IsoSwitch.Infrastructure.Persistence.Migrations
{
    public partial class TxRequestMtiStan : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RequestMti",
                table: "Transactions",
                type: "text",
                nullable: false,
                defaultValue: "0100");

            migrationBuilder.AddColumn<string>(
                name: "Stan",
                table: "Transactions",
                type: "text",
                nullable: false,
                defaultValue: "000000");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "RequestMti", table: "Transactions");
            migrationBuilder.DropColumn(name: "Stan", table: "Transactions");
        }
    }
}