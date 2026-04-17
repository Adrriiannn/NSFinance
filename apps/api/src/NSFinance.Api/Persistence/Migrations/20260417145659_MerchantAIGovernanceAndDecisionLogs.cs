using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSFinance.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MerchantAIGovernanceAndDecisionLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AmbiguityFlags",
                table: "Merchants",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CanonicalMerchantName",
                table: "Merchants",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CategoryCandidates",
                table: "Merchants",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Confidence",
                table: "Merchants",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "CountryCode",
                table: "Merchants",
                type: "character varying(2)",
                maxLength: 2,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<double>(
                name: "EvidenceQuality",
                table: "Merchants",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "FailureCount",
                table: "Merchants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "GoodsServicesType",
                table: "Merchants",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "InvestigatedAtUtc",
                table: "Merchants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "InvestigationCooldownUntilUtc",
                table: "Merchants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvestigationModel",
                table: "Merchants",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastFailureUtc",
                table: "Merchants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastUsedAtUtc",
                table: "Merchants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MerchantSummary",
                table: "Merchants",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MerchantVertical",
                table: "Merchants",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedMerchantKey",
                table: "Merchants",
                type: "character varying(320)",
                maxLength: 320,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "TopCategoryCode",
                table: "Merchants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TopDomainCode",
                table: "Merchants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TopSubcategoryCode",
                table: "Merchants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WebsiteDomain",
                table: "Merchants",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MerchantAIDecisionLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    NormalizedTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SyncRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    Descriptor = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    NormalizedDescriptor = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    MerchantKey = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    DomainCandidates = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TriggerMode = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    DeterministicResult = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    RegistryResult = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    AIGateDecision = table.Column<bool>(type: "boolean", nullable: false),
                    AISkipReason = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    BudgetState = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CooldownState = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ModelUsed = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    FinalState = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AICallExecuted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MerchantAIDecisionLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Merchants_InvestigatedAtUtc",
                table: "Merchants",
                column: "InvestigatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Merchants_InvestigationCooldownUntilUtc",
                table: "Merchants",
                column: "InvestigationCooldownUntilUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Merchants_LastFailureUtc",
                table: "Merchants",
                column: "LastFailureUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Merchants_NormalizedMerchantKey",
                table: "Merchants",
                column: "NormalizedMerchantKey");

            migrationBuilder.CreateIndex(
                name: "IX_MerchantAIDecisionLogs_AICallExecuted",
                table: "MerchantAIDecisionLogs",
                column: "AICallExecuted");

            migrationBuilder.CreateIndex(
                name: "IX_MerchantAIDecisionLogs_ConnectionId_CreatedUtc",
                table: "MerchantAIDecisionLogs",
                columns: new[] { "ConnectionId", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MerchantAIDecisionLogs_MerchantKey_CreatedUtc",
                table: "MerchantAIDecisionLogs",
                columns: new[] { "MerchantKey", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MerchantAIDecisionLogs_SyncRunId_CreatedUtc",
                table: "MerchantAIDecisionLogs",
                columns: new[] { "SyncRunId", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MerchantAIDecisionLogs_UserId_CreatedUtc",
                table: "MerchantAIDecisionLogs",
                columns: new[] { "UserId", "CreatedUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MerchantAIDecisionLogs");

            migrationBuilder.DropIndex(
                name: "IX_Merchants_InvestigatedAtUtc",
                table: "Merchants");

            migrationBuilder.DropIndex(
                name: "IX_Merchants_InvestigationCooldownUntilUtc",
                table: "Merchants");

            migrationBuilder.DropIndex(
                name: "IX_Merchants_LastFailureUtc",
                table: "Merchants");

            migrationBuilder.DropIndex(
                name: "IX_Merchants_NormalizedMerchantKey",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "AmbiguityFlags",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "CanonicalMerchantName",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "CategoryCandidates",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "Confidence",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "CountryCode",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "EvidenceQuality",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "FailureCount",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "GoodsServicesType",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "InvestigatedAtUtc",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "InvestigationCooldownUntilUtc",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "InvestigationModel",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "LastFailureUtc",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "LastUsedAtUtc",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "MerchantSummary",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "MerchantVertical",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "NormalizedMerchantKey",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "TopCategoryCode",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "TopDomainCode",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "TopSubcategoryCode",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "WebsiteDomain",
                table: "Merchants");
        }
    }
}
