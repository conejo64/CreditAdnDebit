using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CardVault.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDelinquencyRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_TokenVaultEntryEntity",
                table: "TokenVaultEntryEntity");

            migrationBuilder.RenameTable(
                name: "TokenVaultEntryEntity",
                newName: "TokenVault");

            migrationBuilder.AlterColumn<string>(
                name: "Token",
                table: "TokenVault",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "TagB64",
                table: "TokenVault",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "NonceB64",
                table: "TokenVault",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "MaskedPan",
                table: "TokenVault",
                type: "character varying(19)",
                maxLength: 19,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "KeyId",
                table: "TokenVault",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Bin",
                table: "TokenVault",
                type: "character varying(12)",
                maxLength: 12,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_TokenVault",
                table: "TokenVault",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "DelinquencyRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    StatementId = table.Column<Guid>(type: "uuid", nullable: false),
                    OverdueAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DaysInArrears = table.Column<int>(type: "integer", nullable: false),
                    Bucket = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DelinquencyRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TokenVault_MaskedPan",
                table: "TokenVault",
                column: "MaskedPan");

            migrationBuilder.CreateIndex(
                name: "IX_TokenVault_Token",
                table: "TokenVault",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DelinquencyRecords_AccountId_Status",
                table: "DelinquencyRecords",
                columns: new[] { "AccountId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_DelinquencyRecords_StatementId",
                table: "DelinquencyRecords",
                column: "StatementId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DelinquencyRecords");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TokenVault",
                table: "TokenVault");

            migrationBuilder.DropIndex(
                name: "IX_TokenVault_MaskedPan",
                table: "TokenVault");

            migrationBuilder.DropIndex(
                name: "IX_TokenVault_Token",
                table: "TokenVault");

            migrationBuilder.RenameTable(
                name: "TokenVault",
                newName: "TokenVaultEntryEntity");

            migrationBuilder.AlterColumn<string>(
                name: "Token",
                table: "TokenVaultEntryEntity",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "TagB64",
                table: "TokenVaultEntryEntity",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "NonceB64",
                table: "TokenVaultEntryEntity",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "MaskedPan",
                table: "TokenVaultEntryEntity",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(19)",
                oldMaxLength: 19,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "KeyId",
                table: "TokenVaultEntryEntity",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "Bin",
                table: "TokenVaultEntryEntity",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(12)",
                oldMaxLength: 12,
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_TokenVaultEntryEntity",
                table: "TokenVaultEntryEntity",
                column: "Id");
        }
    }
}
