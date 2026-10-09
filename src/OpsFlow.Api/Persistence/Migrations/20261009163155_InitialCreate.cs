using System;
using Microsoft.EntityFrameworkCore.Migrations;
using OpsFlow.Api.Persistence.Configurations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace OpsFlow.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApprovalPolicyRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    StepOrder = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    AmountGreaterThan = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalPolicyRules", x => x.Id);
                    table.CheckConstraint("CK_ApprovalPolicyRules_AmountGreaterThan", "[AmountGreaterThan] >= 0");
                    table.CheckConstraint("CK_ApprovalPolicyRules_Kind", "[Kind] IN ('Manager', 'Finance', 'Director')");
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PasswordHash = table.Column<string>(type: "varchar(512)", unicode: false, maxLength: 512, nullable: false),
                    Role = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    ManagerId = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.CheckConstraint("CK_Users_Role", "[Role] IN ('Employee', 'Manager', 'Finance', 'Director', 'Admin')");
                    table.ForeignKey(
                        name: "FK_Users_Users_ManagerId",
                        column: x => x.ManagerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Requests",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Reference = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false, computedColumnSql: "'REQ-' + CASE WHEN [Id] < 1000000 THEN RIGHT('00000' + CAST([Id] AS varchar(19)), 6) ELSE CAST([Id] AS varchar(19)) END", stored: true),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Category = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    RequesterId = table.Column<int>(type: "int", nullable: false),
                    CurrentRound = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Requests", x => x.Id);
                    table.CheckConstraint("CK_Requests_Amount", "[Amount] > 0 AND [Amount] <= 1000000");
                    table.CheckConstraint("CK_Requests_Category", "[Category] IN ('Purchase', 'Expense')");
                    table.CheckConstraint("CK_Requests_CurrentRound", "[CurrentRound] >= 0");
                    table.CheckConstraint("CK_Requests_Status", "[Status] IN ('Draft', 'InApproval', 'ChangesRequested', 'Approved', 'Rejected', 'Cancelled', 'Fulfilled')");
                    table.ForeignKey(
                        name: "FK_Requests_Users_RequesterId",
                        column: x => x.RequesterId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalSteps",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestId = table.Column<long>(type: "bigint", nullable: false),
                    Round = table.Column<int>(type: "int", nullable: false),
                    StepOrder = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    AssigneeId = table.Column<int>(type: "int", nullable: true),
                    DecidedById = table.Column<int>(type: "int", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalSteps", x => x.Id);
                    table.CheckConstraint("CK_ApprovalSteps_Kind", "[Kind] IN ('Manager', 'Finance', 'Director')");
                    table.CheckConstraint("CK_ApprovalSteps_Round", "[Round] >= 1");
                    table.CheckConstraint("CK_ApprovalSteps_Status", "[Status] IN ('Pending', 'Approved', 'Rejected', 'ChangesRequested', 'Skipped')");
                    table.ForeignKey(
                        name: "FK_ApprovalSteps_Requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "Requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ApprovalSteps_Users_AssigneeId",
                        column: x => x.AssigneeId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ApprovalSteps_Users_DecidedById",
                        column: x => x.DecidedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AuditEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestId = table.Column<long>(type: "bigint", nullable: false),
                    ActorId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    FromStatus = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: true),
                    ToStatus = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: true),
                    Details = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    CorrelationId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => x.Id);
                    table.CheckConstraint("CK_AuditEntries_FromStatus", "[FromStatus] IN ('Draft', 'InApproval', 'ChangesRequested', 'Approved', 'Rejected', 'Cancelled', 'Fulfilled')");
                    table.CheckConstraint("CK_AuditEntries_ToStatus", "[ToStatus] IN ('Draft', 'InApproval', 'ChangesRequested', 'Approved', 'Rejected', 'Cancelled', 'Fulfilled')");
                    table.ForeignKey(
                        name: "FK_AuditEntries_Requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "Requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AuditEntries_Users_ActorId",
                        column: x => x.ActorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "ApprovalPolicyRules",
                columns: new[] { "Id", "AmountGreaterThan", "Kind", "StepOrder" },
                values: new object[,]
                {
                    { 1, 0m, "Manager", 1 },
                    { 2, 5000m, "Finance", 2 },
                    { 3, 50000m, "Director", 3 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalPolicyRules_StepOrder",
                table: "ApprovalPolicyRules",
                column: "StepOrder",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalSteps_AssigneeId_Pending",
                table: "ApprovalSteps",
                column: "AssigneeId",
                filter: "[Status] = 'Pending'")
                .Annotation("SqlServer:Include", new[] { "RequestId", "Round", "StepOrder", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalSteps_DecidedById",
                table: "ApprovalSteps",
                column: "DecidedById");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalSteps_Kind_PendingUnassigned",
                table: "ApprovalSteps",
                column: "Kind",
                filter: "[Status] = 'Pending' AND [AssigneeId] IS NULL")
                .Annotation("SqlServer:Include", new[] { "RequestId", "Round", "StepOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalSteps_RequestId_Round_StepOrder",
                table: "ApprovalSteps",
                columns: new[] { "RequestId", "Round", "StepOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_ActorId",
                table: "AuditEntries",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_RequestId_OccurredAt",
                table: "AuditEntries",
                columns: new[] { "RequestId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Requests_Reference",
                table: "Requests",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Requests_RequesterId_Status_CreatedAt",
                table: "Requests",
                columns: new[] { "RequesterId", "Status", "CreatedAt" },
                descending: new[] { false, false, true })
                .Annotation("SqlServer:Include", new[] { "Reference", "Title", "Category", "Amount", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Requests_Status_CreatedAt",
                table: "Requests",
                columns: new[] { "Status", "CreatedAt" },
                descending: new[] { false, true })
                .Annotation("SqlServer:Include", new[] { "Reference", "Title", "Category", "Amount", "RequesterId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_ManagerId",
                table: "Users",
                column: "ManagerId");

            // BR-52: the audit trail is append-only, whatever login or tool touches the table.
            migrationBuilder.Sql($"""
                CREATE TRIGGER [dbo].[{AuditEntryConfiguration.AppendOnlyTrigger}] ON [dbo].[AuditEntries]
                INSTEAD OF UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 50001, 'AuditEntries is append-only: updates and deletes are not allowed.', 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"DROP TRIGGER IF EXISTS [dbo].[{AuditEntryConfiguration.AppendOnlyTrigger}];");

            migrationBuilder.DropTable(
                name: "ApprovalPolicyRules");

            migrationBuilder.DropTable(
                name: "ApprovalSteps");

            migrationBuilder.DropTable(
                name: "AuditEntries");

            migrationBuilder.DropTable(
                name: "Requests");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
