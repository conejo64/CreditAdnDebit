using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IsoSwitch.Infrastructure.Persistence.Migrations
{
    public partial class InitialIsoSwitch : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RoutingRulesCache",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    BinStart = table.Column<int>(type: "integer", nullable: false),
                    BinEnd = table.Column<int>(type: "integer", nullable: false),
                    ConnectorId = table.Column<string>(type: "text", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoutingRulesCache", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RoutingRulesCache_Priority",
                table: "RoutingRulesCache",
                column: "Priority");

            migrationBuilder.CreateIndex(
                name: "IX_RoutingRulesCache_BinStart_BinEnd",
                table: "RoutingRulesCache",
                columns: new[] { "BinStart", "BinEnd" });

            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TraceId = table.Column<string>(type: "text", nullable: false),
                    Stan = table.Column<string>(type: "text", nullable: false),
                    ConnectorId = table.Column<string>(type: "text", nullable: false),
                    RequestJson = table.Column<string>(type: "text", nullable: false),
                    ResponseJson = table.Column<string>(type: "text", nullable: true),
                    ResponseCode = table.Column<string>(type: "text", nullable: true),
                    Decision = table.Column<string>(type: "text", nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_TraceId",
                table: "Transactions",
                column: "TraceId",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Transactions");
            migrationBuilder.DropTable(name: "RoutingRulesCache");
        }
    }
}