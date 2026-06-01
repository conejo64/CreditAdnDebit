using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CardVault.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRealNotificationChannels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextAttemptOn",
                table: "CustomerNotificationDeliveries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderId",
                table: "CustomerNotificationDeliveries",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SendingStartedOn",
                table: "CustomerNotificationDeliveries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "CustomerNotificationDeliveries",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_CustomerNotificationDeliveries_Status_NextAttemptOn",
                table: "CustomerNotificationDeliveries",
                columns: new[] { "Status", "NextAttemptOn" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerNotificationDeliveries_Status_SendingStartedOn",
                table: "CustomerNotificationDeliveries",
                columns: new[] { "Status", "SendingStartedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerNotificationDeliveries_TenantId",
                table: "CustomerNotificationDeliveries",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data migration: map new status values back to pre-existing ones before removing columns.
            // Sending (4) → Pending (1): rows in transit become eligible for a new attempt.
            // DeadLetter (5) → Failed (3): rows that exhausted retries are kept as Failed so they
            //   remain visible in the backlog rather than silently disappearing.
            migrationBuilder.Sql(@"
                UPDATE ""CustomerNotificationDeliveries""
                SET ""Status"" = 1
                WHERE ""Status"" = 4;
            ");
            migrationBuilder.Sql(@"
                UPDATE ""CustomerNotificationDeliveries""
                SET ""Status"" = 3
                WHERE ""Status"" = 5;
            ");

            migrationBuilder.DropIndex(
                name: "IX_CustomerNotificationDeliveries_Status_NextAttemptOn",
                table: "CustomerNotificationDeliveries");

            migrationBuilder.DropIndex(
                name: "IX_CustomerNotificationDeliveries_Status_SendingStartedOn",
                table: "CustomerNotificationDeliveries");

            migrationBuilder.DropIndex(
                name: "IX_CustomerNotificationDeliveries_TenantId",
                table: "CustomerNotificationDeliveries");

            migrationBuilder.DropColumn(
                name: "NextAttemptOn",
                table: "CustomerNotificationDeliveries");

            migrationBuilder.DropColumn(
                name: "ProviderId",
                table: "CustomerNotificationDeliveries");

            migrationBuilder.DropColumn(
                name: "SendingStartedOn",
                table: "CustomerNotificationDeliveries");

            // IMPORTANT — TenantId backfill note:
            // The Up migration defaults TenantId to Guid.Empty (00000000-...) for all existing rows.
            // This is the safe default for a single-tenant deployment.
            // For multi-tenant environments, backfilling the correct TenantId per existing row
            // is a SEPARATE operational step that MUST be performed before enabling multi-tenant
            // routing (Slice 2b). Do NOT guess a production tenant GUID here.
            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "CustomerNotificationDeliveries");
        }
    }
}
