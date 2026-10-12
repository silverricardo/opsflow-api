using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpsFlow.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIdempotencyRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IdempotencyRecords",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Key = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    Endpoint = table.Column<string>(type: "varchar(300)", unicode: false, maxLength: 300, nullable: false),
                    RequestHash = table.Column<byte[]>(type: "binary(32)", fixedLength: true, maxLength: 32, nullable: false),
                    StatusCode = table.Column<int>(type: "int", nullable: true),
                    ResponseContentType = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    ResponseBody = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    ResponseETag = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    ResponseLocation = table.Column<string>(type: "varchar(300)", unicode: false, maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyRecords", x => new { x.UserId, x.Key });
                    table.ForeignKey(
                        name: "FK_IdempotencyRecords_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_ExpiresAt",
                table: "IdempotencyRecords",
                column: "ExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IdempotencyRecords");
        }
    }
}
