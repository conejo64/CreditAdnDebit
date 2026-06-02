using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CardVault.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEncryptedNotificationDestination : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DestinationCipherB64",
                table: "CustomerNotificationDeliveries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DestinationKeyId",
                table: "CustomerNotificationDeliveries",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DestinationNonceB64",
                table: "CustomerNotificationDeliveries",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DestinationTagB64",
                table: "CustomerNotificationDeliveries",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DestinationCipherB64",
                table: "CustomerNotificationDeliveries");

            migrationBuilder.DropColumn(
                name: "DestinationKeyId",
                table: "CustomerNotificationDeliveries");

            migrationBuilder.DropColumn(
                name: "DestinationNonceB64",
                table: "CustomerNotificationDeliveries");

            migrationBuilder.DropColumn(
                name: "DestinationTagB64",
                table: "CustomerNotificationDeliveries");
        }
    }
}
