using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IsoSwitch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinalMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Service = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EventType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TraceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    OccurredOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    PayloadSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BinRangesCache",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BinStart = table.Column<int>(type: "integer", nullable: false),
                    BinEnd = table.Column<int>(type: "integer", nullable: false),
                    Brand = table.Column<string>(type: "text", nullable: false),
                    Product = table.Column<string>(type: "text", nullable: false),
                    IssuerName = table.Column<string>(type: "text", nullable: true),
                    CountryCode = table.Column<string>(type: "text", nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BinRangesCache", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CardProductsCache",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Brand = table.Column<string>(type: "text", nullable: false),
                    ProductType = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardProductsCache", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CountriesCache",
                columns: table => new
                {
                    Code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    NumericCode = table.Column<string>(type: "text", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CountriesCache", x => x.Code);
                });

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
                constraints: table =>
                {
                    table.PrimaryKey("PK_CurrenciesCache", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "iso_message_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TraceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Direction = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Mti = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    FieldsJson = table.Column<string>(type: "text", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_iso_message_logs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NetworksCache",
                columns: table => new
                {
                    Code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NetworksCache", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "ParticipantsCache",
                columns: table => new
                {
                    ParticipantId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    CountryCode = table.Column<string>(type: "text", nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParticipantsCache", x => x.ParticipantId);
                });

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
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoutingRulesV2", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TraceId = table.Column<string>(type: "text", nullable: false),
                    CorrelationId = table.Column<string>(type: "text", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "text", nullable: true),
                    RequestMti = table.Column<string>(type: "text", nullable: false),
                    Stan = table.Column<string>(type: "text", nullable: false),
                    TxType = table.Column<string>(type: "text", nullable: false),
                    OriginalTraceId = table.Column<string>(type: "text", nullable: true),
                    ReversalState = table.Column<string>(type: "text", nullable: true),
                    ReversalConfirmedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConnectorId = table.Column<string>(type: "text", nullable: false),
                    RequestJson = table.Column<string>(type: "text", nullable: false),
                    ResponseJson = table.Column<string>(type: "text", nullable: true),
                    ResponseCode = table.Column<string>(type: "text", nullable: true),
                    ProcessingCode = table.Column<string>(type: "text", nullable: true),
                    Amount12 = table.Column<string>(type: "text", nullable: true),
                    Currency = table.Column<string>(type: "text", nullable: true),
                    TerminalId = table.Column<string>(type: "text", nullable: true),
                    MerchantId = table.Column<string>(type: "text", nullable: true),
                    Decision = table.Column<string>(type: "text", nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    InDoubt = table.Column<bool>(type: "boolean", nullable: false),
                    ReversalScheduledOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReversalAttemptedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReversalStatus = table.Column<string>(type: "text", nullable: true),
                    ReversalAttempts = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_EventType",
                table: "AuditEvents",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_OccurredOn",
                table: "AuditEvents",
                column: "OccurredOn");

            migrationBuilder.CreateIndex(
                name: "IX_iso_message_logs_CreatedOn",
                table: "iso_message_logs",
                column: "CreatedOn");

            migrationBuilder.CreateIndex(
                name: "IX_iso_message_logs_Mti",
                table: "iso_message_logs",
                column: "Mti");

            migrationBuilder.CreateIndex(
                name: "IX_iso_message_logs_TraceId_Direction",
                table: "iso_message_logs",
                columns: new[] { "TraceId", "Direction" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoutingRulesCache_BinStart_BinEnd",
                table: "RoutingRulesCache",
                columns: new[] { "BinStart", "BinEnd" });

            migrationBuilder.CreateIndex(
                name: "IX_RoutingRulesCache_Priority",
                table: "RoutingRulesCache",
                column: "Priority");

            migrationBuilder.CreateIndex(
                name: "IX_RoutingRulesV2_BinStart_BinEnd",
                table: "RoutingRulesV2",
                columns: new[] { "BinStart", "BinEnd" });

            migrationBuilder.CreateIndex(
                name: "IX_RoutingRulesV2_CountryCode",
                table: "RoutingRulesV2",
                column: "CountryCode");

            migrationBuilder.CreateIndex(
                name: "IX_RoutingRulesV2_Network",
                table: "RoutingRulesV2",
                column: "Network");

            migrationBuilder.CreateIndex(
                name: "IX_RoutingRulesV2_Priority",
                table: "RoutingRulesV2",
                column: "Priority");

            migrationBuilder.CreateIndex(
                name: "IX_RoutingRulesV2_TxType",
                table: "RoutingRulesV2",
                column: "TxType");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_TraceId",
                table: "Transactions",
                column: "TraceId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEvents");

            migrationBuilder.DropTable(
                name: "BinRangesCache");

            migrationBuilder.DropTable(
                name: "CardProductsCache");

            migrationBuilder.DropTable(
                name: "CountriesCache");

            migrationBuilder.DropTable(
                name: "CurrenciesCache");

            migrationBuilder.DropTable(
                name: "iso_message_logs");

            migrationBuilder.DropTable(
                name: "NetworksCache");

            migrationBuilder.DropTable(
                name: "ParticipantsCache");

            migrationBuilder.DropTable(
                name: "RoutingRulesCache");

            migrationBuilder.DropTable(
                name: "RoutingRulesV2");

            migrationBuilder.DropTable(
                name: "Transactions");
        }
    }
}
