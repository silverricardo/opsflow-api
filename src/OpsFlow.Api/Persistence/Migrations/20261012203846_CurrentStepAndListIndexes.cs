using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpsFlow.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CurrentStepAndListIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ApprovalSteps_AssigneeId_Pending",
                table: "ApprovalSteps");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalSteps_Kind_PendingUnassigned",
                table: "ApprovalSteps");

            migrationBuilder.AddColumn<bool>(
                name: "IsCurrent",
                table: "ApprovalSteps",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Backfill: the current step is the first pending step of the current round of a request in approval.
            // Set-based, one statement, so it is safe on large tables (runs within the migration's long timeout).
            migrationBuilder.Sql("""
                UPDATE s SET s.[IsCurrent] = 1
                FROM [ApprovalSteps] AS s
                JOIN [Requests] AS r ON r.[Id] = s.[RequestId]
                WHERE r.[Status] = 'InApproval'
                  AND s.[Round] = r.[CurrentRound]
                  AND s.[Status] = 'Pending'
                  AND NOT EXISTS (
                      SELECT 1 FROM [ApprovalSteps] AS e
                      WHERE e.[RequestId] = s.[RequestId] AND e.[Round] = s.[Round]
                        AND e.[Status] = 'Pending' AND e.[StepOrder] < s.[StepOrder]);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Requests_Amount",
                table: "Requests",
                columns: new[] { "Amount", "Id" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_Requests_CreatedAt",
                table: "Requests",
                columns: new[] { "CreatedAt", "Id" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalSteps_Current_Assignee",
                table: "ApprovalSteps",
                column: "AssigneeId",
                filter: "[IsCurrent] = 1")
                .Annotation("SqlServer:Include", new[] { "RequestId", "Round", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalSteps_Current_Pool",
                table: "ApprovalSteps",
                column: "Kind",
                filter: "[IsCurrent] = 1 AND [AssigneeId] IS NULL")
                .Annotation("SqlServer:Include", new[] { "RequestId", "Round" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ApprovalSteps_CurrentIsPending",
                table: "ApprovalSteps",
                sql: "[IsCurrent] = 0 OR [Status] = 'Pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Requests_Amount",
                table: "Requests");

            migrationBuilder.DropIndex(
                name: "IX_Requests_CreatedAt",
                table: "Requests");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalSteps_Current_Assignee",
                table: "ApprovalSteps");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalSteps_Current_Pool",
                table: "ApprovalSteps");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ApprovalSteps_CurrentIsPending",
                table: "ApprovalSteps");

            migrationBuilder.DropColumn(
                name: "IsCurrent",
                table: "ApprovalSteps");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalSteps_AssigneeId_Pending",
                table: "ApprovalSteps",
                column: "AssigneeId",
                filter: "[Status] = 'Pending'")
                .Annotation("SqlServer:Include", new[] { "RequestId", "Round", "StepOrder", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalSteps_Kind_PendingUnassigned",
                table: "ApprovalSteps",
                column: "Kind",
                filter: "[Status] = 'Pending' AND [AssigneeId] IS NULL")
                .Annotation("SqlServer:Include", new[] { "RequestId", "Round", "StepOrder" });
        }
    }
}
