using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NSFinance.Api.Persistence;

#nullable disable

namespace NSFinance.Api.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260715011500_StatementImportStaging")]
public partial class StatementImportStaging : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_ImportJobs_UserId",
            table: "ImportJobs");

        migrationBuilder.AddUniqueConstraint(
            name: "AK_FinancialAccounts_Id_UserId",
            table: "FinancialAccounts",
            columns: new[] { "Id", "UserId" });

        migrationBuilder.AddColumn<string>(
            name: "AccountCurrency",
            table: "ImportJobs",
            type: "character varying(3)",
            maxLength: 3,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "CommittedUtc",
            table: "ImportJobs",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "CommittedRowCount",
            table: "ImportJobs",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<DateTime>(
            name: "ExpiresUtc",
            table: "ImportJobs",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "FailureCode",
            table: "ImportJobs",
            type: "character varying(96)",
            maxLength: 96,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "FailedUtc",
            table: "ImportJobs",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "FileSizeBytes",
            table: "ImportJobs",
            type: "bigint",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "FinancialAccountId",
            table: "ImportJobs",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "ExactDuplicateRowCount",
            table: "ImportJobs",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<int>(
            name: "IncludedRowCount",
            table: "ImportJobs",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<int>(
            name: "InvalidRowCount",
            table: "ImportJobs",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<string>(
            name: "Kind",
            table: "ImportJobs",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "legacy");

        migrationBuilder.AddColumn<int>(
            name: "LikelyDuplicateRowCount",
            table: "ImportJobs",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<string>(
            name: "Locale",
            table: "ImportJobs",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "MappingFingerprint",
            table: "ImportJobs",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "MappingJson",
            table: "ImportJobs",
            type: "jsonb",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "MappingVersion",
            table: "ImportJobs",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ParserVersion",
            table: "ImportJobs",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "ReadyForReviewUtc",
            table: "ImportJobs",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "Revision",
            table: "ImportJobs",
            type: "integer",
            nullable: false,
            defaultValue: 1);

        migrationBuilder.AddColumn<string>(
            name: "SourceFingerprint",
            table: "ImportJobs",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "TimeZoneId",
            table: "ImportJobs",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "TotalRowCount",
            table: "ImportJobs",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<DateTime>(
            name: "UndoneUtc",
            table: "ImportJobs",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "UpdatedUtc",
            table: "ImportJobs",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "ValidRowCount",
            table: "ImportJobs",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.Sql(
            """
            UPDATE "ImportJobs"
            SET "UpdatedUtc" = "CreatedUtc"
            WHERE "UpdatedUtc" IS NULL;
            """);

        migrationBuilder.AlterColumn<DateTime>(
            name: "UpdatedUtc",
            table: "ImportJobs",
            type: "timestamp with time zone",
            nullable: false,
            defaultValueSql: "timezone('utc', now())",
            oldClrType: typeof(DateTime),
            oldType: "timestamp with time zone",
            oldNullable: true);

        migrationBuilder.CreateTable(
            name: "StatementImportRows",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ImportJobId = table.Column<Guid>(type: "uuid", nullable: false),
                RowNumber = table.Column<int>(type: "integer", nullable: false),
                RowFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                SourceReferenceFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                ValidationStatus = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                ValidationCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                DuplicateClassification = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                ReviewDisposition = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                DuplicateCandidateTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                SourceEvidenceJson = table.Column<string>(type: "jsonb", nullable: true),
                EvidenceExpiresUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                EffectiveDate = table.Column<DateOnly>(type: "date", nullable: true),
                EffectiveAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                TimestampPrecision = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                CommittedTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_StatementImportRows", x => x.Id);
                table.ForeignKey(
                    name: "FK_StatementImportRows_ImportJobs_ImportJobId",
                    column: x => x.ImportJobId,
                    principalTable: "ImportJobs",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_StatementImportRows_Transactions_CommittedTransactionId",
                    column: x => x.CommittedTransactionId,
                    principalTable: "Transactions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_StatementImportRows_Transactions_DuplicateCandidateTransactionId",
                    column: x => x.DuplicateCandidateTransactionId,
                    principalTable: "Transactions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ImportJobs_FinancialAccountId_CreatedUtc",
            table: "ImportJobs",
            columns: new[] { "FinancialAccountId", "CreatedUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_ImportJobs_FinancialAccountId_UserId",
            table: "ImportJobs",
            columns: new[] { "FinancialAccountId", "UserId" });

        migrationBuilder.CreateIndex(
            name: "IX_ImportJobs_Status_ExpiresUtc",
            table: "ImportJobs",
            columns: new[] { "Status", "ExpiresUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_ImportJobs_UserId_Kind_Status_UpdatedUtc",
            table: "ImportJobs",
            columns: new[] { "UserId", "Kind", "Status", "UpdatedUtc" });

        migrationBuilder.CreateIndex(
            name: "UX_ImportJobs_CommittedStatementSource",
            table: "ImportJobs",
            columns: new[] { "UserId", "FinancialAccountId", "SourceFingerprint" },
            unique: true,
            filter: "\"Kind\" = 'statement_csv' AND \"Status\" = 'committed'");

        migrationBuilder.CreateIndex(
            name: "UX_ImportJobs_StatementIdempotency",
            table: "ImportJobs",
            columns: new[]
            {
                "UserId",
                "FinancialAccountId",
                "Kind",
                "SourceFingerprint",
                "MappingFingerprint",
                "ParserVersion",
                "MappingVersion"
            },
            unique: true,
            filter: "\"Kind\" = 'statement_csv'");

        migrationBuilder.CreateIndex(
            name: "IX_StatementImportRows_DuplicateCandidateTransactionId",
            table: "StatementImportRows",
            column: "DuplicateCandidateTransactionId");

        migrationBuilder.CreateIndex(
            name: "IX_StatementImportRows_EvidenceExpiresUtc",
            table: "StatementImportRows",
            column: "EvidenceExpiresUtc");

        migrationBuilder.CreateIndex(
            name: "IX_StatementImportRows_ImportJobId_RowFingerprint",
            table: "StatementImportRows",
            columns: new[] { "ImportJobId", "RowFingerprint" });

        migrationBuilder.CreateIndex(
            name: "IX_StatementImportRows_ImportJobId_ValidationStatus_ReviewDisposition",
            table: "StatementImportRows",
            columns: new[] { "ImportJobId", "ValidationStatus", "ReviewDisposition" });

        migrationBuilder.CreateIndex(
            name: "UX_StatementImportRows_BatchRow",
            table: "StatementImportRows",
            columns: new[] { "ImportJobId", "RowNumber" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "UX_StatementImportRows_CommittedTransaction",
            table: "StatementImportRows",
            column: "CommittedTransactionId",
            unique: true,
            filter: "\"CommittedTransactionId\" IS NOT NULL");

        migrationBuilder.AddForeignKey(
            name: "FK_ImportJobs_FinancialAccounts_FinancialAccountId_UserId",
            table: "ImportJobs",
            columns: new[] { "FinancialAccountId", "UserId" },
            principalTable: "FinancialAccounts",
            principalColumns: new[] { "Id", "UserId" },
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "StatementImportRows");

        migrationBuilder.DropForeignKey(
            name: "FK_ImportJobs_FinancialAccounts_FinancialAccountId_UserId",
            table: "ImportJobs");

        migrationBuilder.DropIndex(name: "IX_ImportJobs_FinancialAccountId_CreatedUtc", table: "ImportJobs");
        migrationBuilder.DropIndex(name: "IX_ImportJobs_FinancialAccountId_UserId", table: "ImportJobs");
        migrationBuilder.DropIndex(name: "IX_ImportJobs_Status_ExpiresUtc", table: "ImportJobs");
        migrationBuilder.DropIndex(name: "IX_ImportJobs_UserId_Kind_Status_UpdatedUtc", table: "ImportJobs");
        migrationBuilder.DropIndex(name: "UX_ImportJobs_CommittedStatementSource", table: "ImportJobs");
        migrationBuilder.DropIndex(name: "UX_ImportJobs_StatementIdempotency", table: "ImportJobs");

        migrationBuilder.DropUniqueConstraint(
            name: "AK_FinancialAccounts_Id_UserId",
            table: "FinancialAccounts");

        migrationBuilder.DropColumn(name: "AccountCurrency", table: "ImportJobs");
        migrationBuilder.DropColumn(name: "CommittedRowCount", table: "ImportJobs");
        migrationBuilder.DropColumn(name: "CommittedUtc", table: "ImportJobs");
        migrationBuilder.DropColumn(name: "ExactDuplicateRowCount", table: "ImportJobs");
        migrationBuilder.DropColumn(name: "ExpiresUtc", table: "ImportJobs");
        migrationBuilder.DropColumn(name: "FailedUtc", table: "ImportJobs");
        migrationBuilder.DropColumn(name: "FailureCode", table: "ImportJobs");
        migrationBuilder.DropColumn(name: "FileSizeBytes", table: "ImportJobs");
        migrationBuilder.DropColumn(name: "FinancialAccountId", table: "ImportJobs");
        migrationBuilder.DropColumn(name: "IncludedRowCount", table: "ImportJobs");
        migrationBuilder.DropColumn(name: "InvalidRowCount", table: "ImportJobs");
        migrationBuilder.DropColumn(name: "Kind", table: "ImportJobs");
        migrationBuilder.DropColumn(name: "LikelyDuplicateRowCount", table: "ImportJobs");
        migrationBuilder.DropColumn(name: "Locale", table: "ImportJobs");
        migrationBuilder.DropColumn(name: "MappingFingerprint", table: "ImportJobs");
        migrationBuilder.DropColumn(name: "MappingJson", table: "ImportJobs");
        migrationBuilder.DropColumn(name: "MappingVersion", table: "ImportJobs");
        migrationBuilder.DropColumn(name: "ParserVersion", table: "ImportJobs");
        migrationBuilder.DropColumn(name: "ReadyForReviewUtc", table: "ImportJobs");
        migrationBuilder.DropColumn(name: "Revision", table: "ImportJobs");
        migrationBuilder.DropColumn(name: "SourceFingerprint", table: "ImportJobs");
        migrationBuilder.DropColumn(name: "TimeZoneId", table: "ImportJobs");
        migrationBuilder.DropColumn(name: "TotalRowCount", table: "ImportJobs");
        migrationBuilder.DropColumn(name: "UndoneUtc", table: "ImportJobs");
        migrationBuilder.DropColumn(name: "UpdatedUtc", table: "ImportJobs");
        migrationBuilder.DropColumn(name: "ValidRowCount", table: "ImportJobs");

        migrationBuilder.CreateIndex(
            name: "IX_ImportJobs_UserId",
            table: "ImportJobs",
            column: "UserId");
    }
}
