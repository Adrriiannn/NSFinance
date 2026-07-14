using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSFinance.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DurableBankingOperationJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BankingOperationJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LeaseId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LeaseExpiresUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastFailureCode = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    CompletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankingOperationJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankingOperationJobs_OpenBankingConnections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "OpenBankingConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankingOperationJobs_ConnectionId_OperationType",
                table: "BankingOperationJobs",
                columns: new[] { "ConnectionId", "OperationType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankingOperationJobs_OperationType_Status_NextAttemptUtc",
                table: "BankingOperationJobs",
                columns: new[] { "OperationType", "Status", "NextAttemptUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BankingOperationJobs_Status_LeaseExpiresUtc",
                table: "BankingOperationJobs",
                columns: new[] { "Status", "LeaseExpiresUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BankingOperationJobs_UserId",
                table: "BankingOperationJobs",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BankingOperationJobs");
        }
    }
}
