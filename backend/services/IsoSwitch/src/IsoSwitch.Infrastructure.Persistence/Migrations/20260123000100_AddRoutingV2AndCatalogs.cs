using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IsoSwitch.Infrastructure.Persistence.Migrations
{
    public partial class AddRoutingV2AndCatalogs : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CurrenciesCache",
                columns: table => new
                {
                    Code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Exponent = table.Column<int>(type: "integer", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => { table.PrimaryKey("PK_CurrenciesCache", x => x.Code); });

            migrationBuilder.CreateTable(
                name: "NetworksCache",
                columns: table => new
                {
                    Code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => { table.PrimaryKey("PK_NetworksCache", x => x.Code); });

            migrationBuilder.CreateTable(
                name: "ParticipantsCache",
                columns: table => new
                {
                    ParticipantId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    CountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => { table.PrimaryKey("PK_ParticipantsCache", x => x.ParticipantId); });

            migrationBuilder.CreateTable(
                name: "RoutingRulesV2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    BinStart = table.Column<int>(type: "integer", nullable: false),
                    BinEnd = table.Column<int>(type: "integer", nullable: false),
                    CountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    Network = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    TxType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    ConnectorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => { table.PrimaryKey("PK_RoutingRulesV2", x => x.Id); });

            migrationBuilder.CreateIndex(name: "IX_RoutingRulesV2_Priority", table: "RoutingRulesV2", column: "Priority");
            migrationBuilder.CreateIndex(name: "IX_RoutingRulesV2_BinStart_BinEnd", table: "RoutingRulesV2", columns: new[] { "BinStart", "BinEnd" });
            migrationBuilder.CreateIndex(name: "IX_RoutingRulesV2_CountryCode", table: "RoutingRulesV2", column: "CountryCode");
            migrationBuilder.CreateIndex(name: "IX_RoutingRulesV2_Network", table: "RoutingRulesV2", column: "Network");
            migrationBuilder.CreateIndex(name: "IX_RoutingRulesV2_TxType", table: "RoutingRulesV2", column: "TxType");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "RoutingRulesV2");
            migrationBuilder.DropTable(name: "ParticipantsCache");
            migrationBuilder.DropTable(name: "NetworksCache");
            migrationBuilder.DropTable(name: "CurrenciesCache");
        }
    }
}