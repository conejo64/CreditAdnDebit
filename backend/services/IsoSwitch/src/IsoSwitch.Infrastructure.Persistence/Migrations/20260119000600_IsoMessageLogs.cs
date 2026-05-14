using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IsoSwitch.Infrastructure.Persistence.Migrations
{
    public partial class IsoMessageLogs : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IsoMessageLogs",
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
                    table.PrimaryKey("PK_IsoMessageLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IsoMessageLogs_TraceId",
                table: "IsoMessageLogs",
                column: "TraceId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "IsoMessageLogs");
        }
    }
}