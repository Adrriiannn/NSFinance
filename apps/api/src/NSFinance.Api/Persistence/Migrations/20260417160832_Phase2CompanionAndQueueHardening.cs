using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSFinance.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase2CompanionAndQueueHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "InvestigationInProgress",
                table: "UnresolvedMerchants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "InvestigationLockAcquiredUtc",
                table: "UnresolvedMerchants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "InvestigationLockId",
                table: "UnresolvedMerchants",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastBudgetSkipUtc",
                table: "UnresolvedMerchants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastCooldownSkipUtc",
                table: "UnresolvedMerchants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "QueueEnqueuedAtUtc",
                table: "UnresolvedMerchants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "QueueLastScoredUtc",
                table: "UnresolvedMerchants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "QueueNextRetryUtc",
                table: "UnresolvedMerchants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "QueuePriorityScore",
                table: "UnresolvedMerchants",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "QueueRetryCount",
                table: "UnresolvedMerchants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalObservedSpendAbs",
                table: "UnresolvedMerchants",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "CompanionAIInteractionLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Intent = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ToolsUsed = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    TokensInput = table.Column<int>(type: "integer", nullable: false),
                    TokensOutput = table.Column<int>(type: "integer", nullable: false),
                    Model = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ResponseTimeMs = table.Column<long>(type: "bigint", nullable: false),
                    Succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanionAIInteractionLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserFinancialContextProfiles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Country = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    MonthlyIncomeRange = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    KnownObligationsJson = table.Column<string>(type: "jsonb", nullable: false),
                    BudgetStructureJson = table.Column<string>(type: "jsonb", nullable: false),
                    ActivePlansJson = table.Column<string>(type: "jsonb", nullable: false),
                    SpendingTendenciesJson = table.Column<string>(type: "jsonb", nullable: false),
                    CategoryFlexibilityMarkersJson = table.Column<string>(type: "jsonb", nullable: false),
                    AdviceStylePreference = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserFinancialContextProfiles", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_UserFinancialContextProfiles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UnresolvedMerchants_InvestigationInProgress",
                table: "UnresolvedMerchants",
                column: "InvestigationInProgress");

            migrationBuilder.CreateIndex(
                name: "IX_UnresolvedMerchants_QueueEnqueuedAtUtc",
                table: "UnresolvedMerchants",
                column: "QueueEnqueuedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_UnresolvedMerchants_QueueNextRetryUtc",
                table: "UnresolvedMerchants",
                column: "QueueNextRetryUtc");

            migrationBuilder.CreateIndex(
                name: "IX_UnresolvedMerchants_QueuePriorityScore",
                table: "UnresolvedMerchants",
                column: "QueuePriorityScore");

            migrationBuilder.CreateIndex(
                name: "IX_CompanionAIInteractionLogs_Intent",
                table: "CompanionAIInteractionLogs",
                column: "Intent");

            migrationBuilder.CreateIndex(
                name: "IX_CompanionAIInteractionLogs_SessionId_CreatedUtc",
                table: "CompanionAIInteractionLogs",
                columns: new[] { "SessionId", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CompanionAIInteractionLogs_UserId_CreatedUtc",
                table: "CompanionAIInteractionLogs",
                columns: new[] { "UserId", "CreatedUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompanionAIInteractionLogs");

            migrationBuilder.DropTable(
                name: "UserFinancialContextProfiles");

            migrationBuilder.DropIndex(
                name: "IX_UnresolvedMerchants_InvestigationInProgress",
                table: "UnresolvedMerchants");

            migrationBuilder.DropIndex(
                name: "IX_UnresolvedMerchants_QueueEnqueuedAtUtc",
                table: "UnresolvedMerchants");

            migrationBuilder.DropIndex(
                name: "IX_UnresolvedMerchants_QueueNextRetryUtc",
                table: "UnresolvedMerchants");

            migrationBuilder.DropIndex(
                name: "IX_UnresolvedMerchants_QueuePriorityScore",
                table: "UnresolvedMerchants");

            migrationBuilder.DropColumn(
                name: "InvestigationInProgress",
                table: "UnresolvedMerchants");

            migrationBuilder.DropColumn(
                name: "InvestigationLockAcquiredUtc",
                table: "UnresolvedMerchants");

            migrationBuilder.DropColumn(
                name: "InvestigationLockId",
                table: "UnresolvedMerchants");

            migrationBuilder.DropColumn(
                name: "LastBudgetSkipUtc",
                table: "UnresolvedMerchants");

            migrationBuilder.DropColumn(
                name: "LastCooldownSkipUtc",
                table: "UnresolvedMerchants");

            migrationBuilder.DropColumn(
                name: "QueueEnqueuedAtUtc",
                table: "UnresolvedMerchants");

            migrationBuilder.DropColumn(
                name: "QueueLastScoredUtc",
                table: "UnresolvedMerchants");

            migrationBuilder.DropColumn(
                name: "QueueNextRetryUtc",
                table: "UnresolvedMerchants");

            migrationBuilder.DropColumn(
                name: "QueuePriorityScore",
                table: "UnresolvedMerchants");

            migrationBuilder.DropColumn(
                name: "QueueRetryCount",
                table: "UnresolvedMerchants");

            migrationBuilder.DropColumn(
                name: "TotalObservedSpendAbs",
                table: "UnresolvedMerchants");
        }
    }
}
