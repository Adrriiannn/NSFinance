using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSFinance.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AIAndMerchantOperationalResilienceHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "InvestigationAttemptCount",
                table: "UnresolvedMerchants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "LastInvestigationFailureCode",
                table: "UnresolvedMerchants",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastInvestigationFailureUtc",
                table: "UnresolvedMerchants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextEligibleInvestigationUtc",
                table: "UnresolvedMerchants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastValidatedUtc",
                table: "Merchants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastValidationResultCode",
                table: "Merchants",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextValidationDueUtc",
                table: "Merchants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ValidationAttemptCount",
                table: "Merchants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "LifecycleReason",
                table: "MerchantAliases",
                type: "character varying(240)",
                maxLength: 240,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SupersededByAliasId",
                table: "MerchantAliases",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SupersededUtc",
                table: "MerchantAliases",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TrustLevel",
                table: "MerchantAliases",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "MerchantAliasConflicts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NormalizedAliasText = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    AliasType = table.Column<int>(type: "integer", nullable: false),
                    ExistingMerchantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProposedMerchantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProposedSource = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ProposedTrustLevel = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    OccurrenceCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    LastSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    Notes = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MerchantAliasConflicts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MerchantAliasConflicts_Merchants_ExistingMerchantId",
                        column: x => x.ExistingMerchantId,
                        principalTable: "Merchants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MerchantAliasConflicts_Merchants_ProposedMerchantId",
                        column: x => x.ProposedMerchantId,
                        principalTable: "Merchants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OperationalFailureRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Area = table.Column<int>(type: "integer", nullable: false),
                    Severity = table.Column<int>(type: "integer", nullable: false),
                    FailureType = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SubjectKey = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    FailureMessage = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: true),
                    DetailsJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    OccurrenceCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    FirstOccurredUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    LastOccurredUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationalFailureRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UnresolvedMerchants_NextEligibleInvestigationUtc",
                table: "UnresolvedMerchants",
                column: "NextEligibleInvestigationUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Merchants_NextValidationDueUtc",
                table: "Merchants",
                column: "NextValidationDueUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MerchantAliases_NormalizedAliasText_AliasType_TrustLevel",
                table: "MerchantAliases",
                columns: new[] { "NormalizedAliasText", "AliasType", "TrustLevel" });

            migrationBuilder.CreateIndex(
                name: "IX_MerchantAliasConflicts_ExistingMerchantId",
                table: "MerchantAliasConflicts",
                column: "ExistingMerchantId");

            migrationBuilder.CreateIndex(
                name: "IX_MerchantAliasConflicts_LastSeenUtc",
                table: "MerchantAliasConflicts",
                column: "LastSeenUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MerchantAliasConflicts_NormalizedAliasText_AliasType_Existi~",
                table: "MerchantAliasConflicts",
                columns: new[] { "NormalizedAliasText", "AliasType", "ExistingMerchantId", "ProposedMerchantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MerchantAliasConflicts_ProposedMerchantId",
                table: "MerchantAliasConflicts",
                column: "ProposedMerchantId");

            migrationBuilder.CreateIndex(
                name: "IX_MerchantAliasConflicts_Status",
                table: "MerchantAliasConflicts",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_OperationalFailureRecords_Area_FailureType_Fingerprint",
                table: "OperationalFailureRecords",
                columns: new[] { "Area", "FailureType", "Fingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OperationalFailureRecords_LastOccurredUtc",
                table: "OperationalFailureRecords",
                column: "LastOccurredUtc");

            migrationBuilder.CreateIndex(
                name: "IX_OperationalFailureRecords_Severity",
                table: "OperationalFailureRecords",
                column: "Severity");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MerchantAliasConflicts");

            migrationBuilder.DropTable(
                name: "OperationalFailureRecords");

            migrationBuilder.DropIndex(
                name: "IX_UnresolvedMerchants_NextEligibleInvestigationUtc",
                table: "UnresolvedMerchants");

            migrationBuilder.DropIndex(
                name: "IX_Merchants_NextValidationDueUtc",
                table: "Merchants");

            migrationBuilder.DropIndex(
                name: "IX_MerchantAliases_NormalizedAliasText_AliasType_TrustLevel",
                table: "MerchantAliases");

            migrationBuilder.DropColumn(
                name: "InvestigationAttemptCount",
                table: "UnresolvedMerchants");

            migrationBuilder.DropColumn(
                name: "LastInvestigationFailureCode",
                table: "UnresolvedMerchants");

            migrationBuilder.DropColumn(
                name: "LastInvestigationFailureUtc",
                table: "UnresolvedMerchants");

            migrationBuilder.DropColumn(
                name: "NextEligibleInvestigationUtc",
                table: "UnresolvedMerchants");

            migrationBuilder.DropColumn(
                name: "LastValidatedUtc",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "LastValidationResultCode",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "NextValidationDueUtc",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "ValidationAttemptCount",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "LifecycleReason",
                table: "MerchantAliases");

            migrationBuilder.DropColumn(
                name: "SupersededByAliasId",
                table: "MerchantAliases");

            migrationBuilder.DropColumn(
                name: "SupersededUtc",
                table: "MerchantAliases");

            migrationBuilder.DropColumn(
                name: "TrustLevel",
                table: "MerchantAliases");
        }
    }
}
