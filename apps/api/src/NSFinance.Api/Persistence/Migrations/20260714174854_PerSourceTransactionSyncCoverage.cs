using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSFinance.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PerSourceTransactionSyncCoverage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "TransactionSyncCoverageUtc",
                table: "LinkedBankCards",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TransactionSyncCoverageUtc",
                table: "LinkedBankAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "LinkedBankAccounts" AS account
                SET "TransactionSyncCoverageUtc" = COALESCE(
                    connection."LastSuccessfulSyncUtc",
                    connection."InitialBackfillCompletedUtc")
                FROM "OpenBankingConnections" AS connection
                WHERE account."ConnectionId" = connection."Id"
                  AND account."TransactionSyncCoverageUtc" IS NULL
                  AND (connection."LastSuccessfulSyncUtc" IS NOT NULL
                    OR connection."InitialBackfillCompletedUtc" IS NOT NULL);

                UPDATE "LinkedBankCards" AS card
                SET "TransactionSyncCoverageUtc" = COALESCE(
                    connection."LastSuccessfulSyncUtc",
                    connection."InitialBackfillCompletedUtc")
                FROM "OpenBankingConnections" AS connection
                WHERE card."ConnectionId" = connection."Id"
                  AND card."TransactionSyncCoverageUtc" IS NULL
                  AND (connection."LastSuccessfulSyncUtc" IS NOT NULL
                    OR connection."InitialBackfillCompletedUtc" IS NOT NULL);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TransactionSyncCoverageUtc",
                table: "LinkedBankCards");

            migrationBuilder.DropColumn(
                name: "TransactionSyncCoverageUtc",
                table: "LinkedBankAccounts");
        }
    }
}
