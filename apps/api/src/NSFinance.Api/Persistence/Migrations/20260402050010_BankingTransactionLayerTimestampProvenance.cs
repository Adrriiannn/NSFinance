using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSFinance.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BankingTransactionLayerTimestampProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TransferMatchConfidenceScore",
                table: "Transactions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TransferMatchConfidenceTier",
                table: "Transactions",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TransferMatchReason",
                table: "Transactions",
                type: "character varying(240)",
                maxLength: 240,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedProviderTransactionId",
                table: "RawBankTransactions",
                type: "character varying(180)",
                maxLength: 180,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderStatus",
                table: "RawBankTransactions",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderTimestampRaw",
                table: "RawBankTransactions",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceEndpoint",
                table: "RawBankTransactions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "legacy");

            migrationBuilder.AddColumn<string>(
                name: "StatusNormalizationReason",
                table: "RawBankTransactions",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TimestampNormalizationPolicyKey",
                table: "RawBankTransactions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TimestampPrecision",
                table: "RawBankTransactions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "unknown_needs_verification");

            migrationBuilder.AddColumn<string>(
                name: "TimestampSource",
                table: "RawBankTransactions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ValueAtUtc",
                table: "RawBankTransactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ValueTimestampRaw",
                table: "RawBankTransactions",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "NormalizedBankTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RawBankTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    LinkedBankAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    FinancialAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProjectedTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProviderTransactionId = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: true),
                    NormalizedProviderTransactionId = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: true),
                    DedupeKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    BookedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ValueAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    TransactionType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    TransactionStatus = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    SourceEndpoint = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProviderStatus = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    StatusNormalizationReason = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ProviderTimestampRaw = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ValueTimestampRaw = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    TimestampSource = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TimestampPrecision = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TimestampNormalizedByPolicy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    NormalizationPolicyKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    NormalizationPolicyFamily = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    InterpretationConfidenceScore = table.Column<int>(type: "integer", nullable: true),
                    InterpretationConfidenceTier = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    InterpretationReasonJson = table.Column<string>(type: "jsonb", nullable: true),
                    ImportedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastNormalizedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NormalizedBankTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NormalizedBankTransactions_FinancialAccounts_FinancialAccou~",
                        column: x => x.FinancialAccountId,
                        principalTable: "FinancialAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_NormalizedBankTransactions_LinkedBankAccounts_LinkedBankAcc~",
                        column: x => x.LinkedBankAccountId,
                        principalTable: "LinkedBankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NormalizedBankTransactions_RawBankTransactions_RawBankTrans~",
                        column: x => x.RawBankTransactionId,
                        principalTable: "RawBankTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NormalizedBankTransactions_Transactions_ProjectedTransactio~",
                        column: x => x.ProjectedTransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_TransferMatchConfidenceTier",
                table: "Transactions",
                column: "TransferMatchConfidenceTier");

            migrationBuilder.CreateIndex(
                name: "IX_RawBankTransactions_LinkedBankAccountId_NormalizedProviderT~",
                table: "RawBankTransactions",
                columns: new[] { "LinkedBankAccountId", "NormalizedProviderTransactionId" },
                filter: "\"NormalizedProviderTransactionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedBankTransactions_FinancialAccountId",
                table: "NormalizedBankTransactions",
                column: "FinancialAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedBankTransactions_LinkedBankAccountId",
                table: "NormalizedBankTransactions",
                column: "LinkedBankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedBankTransactions_LinkedBankAccountId_DedupeKey",
                table: "NormalizedBankTransactions",
                columns: new[] { "LinkedBankAccountId", "DedupeKey" });

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedBankTransactions_LinkedBankAccountId_ProviderTran~",
                table: "NormalizedBankTransactions",
                columns: new[] { "LinkedBankAccountId", "ProviderTransactionId" });

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedBankTransactions_ProjectedTransactionId",
                table: "NormalizedBankTransactions",
                column: "ProjectedTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedBankTransactions_RawBankTransactionId",
                table: "NormalizedBankTransactions",
                column: "RawBankTransactionId",
                unique: true);

            migrationBuilder.Sql(
                """
                INSERT INTO "NormalizedBankTransactions" (
                    "Id",
                    "RawBankTransactionId",
                    "LinkedBankAccountId",
                    "FinancialAccountId",
                    "ProjectedTransactionId",
                    "ProviderTransactionId",
                    "NormalizedProviderTransactionId",
                    "DedupeKey",
                    "Amount",
                    "Currency",
                    "BookedAtUtc",
                    "ValueAtUtc",
                    "Description",
                    "TransactionType",
                    "TransactionStatus",
                    "SourceEndpoint",
                    "ProviderStatus",
                    "StatusNormalizationReason",
                    "ProviderTimestampRaw",
                    "ValueTimestampRaw",
                    "TimestampSource",
                    "TimestampPrecision",
                    "TimestampNormalizedByPolicy",
                    "NormalizationPolicyKey",
                    "NormalizationPolicyFamily",
                    "InterpretationConfidenceScore",
                    "InterpretationConfidenceTier",
                    "InterpretationReasonJson",
                    "ImportedUtc",
                    "LastNormalizedUtc")
                SELECT
                    r."Id",
                    r."Id",
                    r."LinkedBankAccountId",
                    l."FinancialAccountId",
                    r."ProjectedTransactionId",
                    r."ProviderTransactionId",
                    r."NormalizedProviderTransactionId",
                    r."DedupeKey",
                    r."Amount",
                    r."Currency",
                    r."BookedAtUtc",
                    r."ValueAtUtc",
                    r."Description",
                    r."TransactionType",
                    r."TransactionStatus",
                    r."SourceEndpoint",
                    r."ProviderStatus",
                    r."StatusNormalizationReason",
                    r."ProviderTimestampRaw",
                    r."ValueTimestampRaw",
                    r."TimestampSource",
                    r."TimestampPrecision",
                    'unknown_needs_verification',
                    r."TimestampNormalizationPolicyKey",
                    NULL,
                    NULL,
                    NULL,
                    NULL,
                    r."ImportedUtc",
                    r."ImportedUtc"
                FROM "RawBankTransactions" r
                LEFT JOIN "LinkedBankAccounts" l ON l."Id" = r."LinkedBankAccountId"
                ON CONFLICT ("RawBankTransactionId") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NormalizedBankTransactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_TransferMatchConfidenceTier",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_RawBankTransactions_LinkedBankAccountId_NormalizedProviderT~",
                table: "RawBankTransactions");

            migrationBuilder.DropColumn(
                name: "TransferMatchConfidenceScore",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "TransferMatchConfidenceTier",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "TransferMatchReason",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "NormalizedProviderTransactionId",
                table: "RawBankTransactions");

            migrationBuilder.DropColumn(
                name: "ProviderStatus",
                table: "RawBankTransactions");

            migrationBuilder.DropColumn(
                name: "ProviderTimestampRaw",
                table: "RawBankTransactions");

            migrationBuilder.DropColumn(
                name: "SourceEndpoint",
                table: "RawBankTransactions");

            migrationBuilder.DropColumn(
                name: "StatusNormalizationReason",
                table: "RawBankTransactions");

            migrationBuilder.DropColumn(
                name: "TimestampNormalizationPolicyKey",
                table: "RawBankTransactions");

            migrationBuilder.DropColumn(
                name: "TimestampPrecision",
                table: "RawBankTransactions");

            migrationBuilder.DropColumn(
                name: "TimestampSource",
                table: "RawBankTransactions");

            migrationBuilder.DropColumn(
                name: "ValueAtUtc",
                table: "RawBankTransactions");

            migrationBuilder.DropColumn(
                name: "ValueTimestampRaw",
                table: "RawBankTransactions");
        }
    }
}
