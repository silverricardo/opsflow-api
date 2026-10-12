using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpsFlow.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VisibilityIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ApprovalSteps_DecidedById",
                table: "ApprovalSteps");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalSteps_AssigneeId",
                table: "ApprovalSteps",
                column: "AssigneeId")
                .Annotation("SqlServer:Include", new[] { "RequestId" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalSteps_DecidedById",
                table: "ApprovalSteps",
                column: "DecidedById")
                .Annotation("SqlServer:Include", new[] { "RequestId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ApprovalSteps_AssigneeId",
                table: "ApprovalSteps");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalSteps_DecidedById",
                table: "ApprovalSteps");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalSteps_DecidedById",
                table: "ApprovalSteps",
                column: "DecidedById");
        }
    }
}
