using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CardVault.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCardProductInstallmentApr : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DefaultInstallmentApr",
                table: "CardProducts",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxInstallmentApr",
                table: "CardProducts",
                type: "numeric",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultInstallmentApr",
                table: "CardProducts");

            migrationBuilder.DropColumn(
                name: "MaxInstallmentApr",
                table: "CardProducts");
        }
    }
}
