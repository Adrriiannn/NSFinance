using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NSFinance.Api.Persistence;

#nullable disable

namespace NSFinance.Api.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260714223000_ManualAccountProvenance")]
public partial class ManualAccountProvenance : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Source",
            table: "FinancialAccounts",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "manual");

        migrationBuilder.AddColumn<string>(
            name: "AnalyticsTreatment",
            table: "Transactions",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "ordinary");

        migrationBuilder.AddColumn<string>(
            name: "EntryKind",
            table: "Transactions",
            type: "character varying(48)",
            maxLength: 48,
            nullable: false,
            defaultValue: "ordinary");

        migrationBuilder.Sql(
            """
            UPDATE "FinancialAccounts" AS account
            SET "Source" = 'provider_projected'
            WHERE EXISTS (
                SELECT 1
                FROM "LinkedBankAccounts" AS linked
                WHERE linked."FinancialAccountId" = account."Id");

            UPDATE "Transactions" AS transaction
            SET "EntryKind" = 'opening_balance_adjustment',
                "AnalyticsTreatment" = 'balance_only',
                "DeterministicClassificationStatus" = 3,
                "DeterministicClassificationRuleKey" = 'provenance.opening_balance',
                "DeterministicReasonCode" = 'balance_only_entry',
                "DeterministicClassificationEvaluatedUtc" = transaction."CreatedUtc",
                "DeterministicClassificationTerminal" = TRUE,
                "DeterministicDeferredRetryEligible" = FALSE,
                "NeedsDeterministicReclassification" = FALSE
            FROM "FinancialAccounts" AS account
            WHERE transaction."FinancialAccountId" = account."Id"
              AND account."Source" = 'manual'
              AND transaction."Description" = 'Opening balance'
              AND transaction."CreatedUtc" = account."CreatedUtc"
              AND transaction."BookedAtUtc" = account."CreatedUtc"
              AND transaction."CategoryId" IS NULL
              AND transaction."TaxonomyDomainId" IS NULL
              AND transaction."TaxonomyCategoryId" IS NULL
              AND transaction."TaxonomySubcategoryId" IS NULL
              AND transaction."TransferKind" IS NULL
              AND transaction."LinkedTransferTransactionId" IS NULL;
            """);

        migrationBuilder.CreateIndex(
            name: "IX_FinancialAccounts_Source",
            table: "FinancialAccounts",
            column: "Source");

        migrationBuilder.CreateIndex(
            name: "IX_Transactions_AnalyticsTreatment",
            table: "Transactions",
            column: "AnalyticsTreatment");

        migrationBuilder.CreateIndex(
            name: "IX_Transactions_EntryKind",
            table: "Transactions",
            column: "EntryKind");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE "Transactions"
            SET "DeterministicClassificationStatus" = 0,
                "DeterministicClassificationRuleKey" = NULL,
                "DeterministicReasonCode" = NULL,
                "DeterministicClassificationEvaluatedUtc" = NULL,
                "DeterministicClassificationTerminal" = FALSE
            WHERE "EntryKind" = 'opening_balance_adjustment'
              AND "AnalyticsTreatment" = 'balance_only'
              AND "DeterministicClassificationRuleKey" = 'provenance.opening_balance';
            """);

        migrationBuilder.DropIndex(
            name: "IX_FinancialAccounts_Source",
            table: "FinancialAccounts");

        migrationBuilder.DropIndex(
            name: "IX_Transactions_AnalyticsTreatment",
            table: "Transactions");

        migrationBuilder.DropIndex(
            name: "IX_Transactions_EntryKind",
            table: "Transactions");

        migrationBuilder.DropColumn(
            name: "Source",
            table: "FinancialAccounts");

        migrationBuilder.DropColumn(
            name: "AnalyticsTreatment",
            table: "Transactions");

        migrationBuilder.DropColumn(
            name: "EntryKind",
            table: "Transactions");
    }
}
