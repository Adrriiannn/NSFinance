using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSFinance.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BankingTransactionHistoryContinuity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EarliestImportedTransactionUtc",
                table: "OpenBankingConnections",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "InitialBackfillCompletedUtc",
                table: "OpenBankingConnections",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "InitialBackfillStartedUtc",
                table: "OpenBankingConnections",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "InitialBackfillWindowStartUtc",
                table: "OpenBankingConnections",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LatestImportedTransactionUtc",
                table: "OpenBankingConnections",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "OpenBankingConnections" AS c
                SET
                    "EarliestImportedTransactionUtc" = b."MinBookedAtUtc",
                    "LatestImportedTransactionUtc" = b."MaxBookedAtUtc",
                    "InitialBackfillWindowStartUtc" = COALESCE(c."InitialBackfillWindowStartUtc", b."MinBookedAtUtc"),
                    "InitialBackfillStartedUtc" = COALESCE(c."InitialBackfillStartedUtc", c."CreatedUtc"),
                    "InitialBackfillCompletedUtc" = COALESCE(c."InitialBackfillCompletedUtc", c."LastSuccessfulSyncUtc", c."UpdatedUtc")
                FROM (
                    SELECT
                        l."ConnectionId" AS "ConnectionId",
                        MIN(r."BookedAtUtc") AS "MinBookedAtUtc",
                        MAX(r."BookedAtUtc") AS "MaxBookedAtUtc"
                    FROM "RawBankTransactions" AS r
                    INNER JOIN "LinkedBankAccounts" AS l
                        ON l."Id" = r."LinkedBankAccountId"
                    GROUP BY l."ConnectionId"
                ) AS b
                WHERE b."ConnectionId" = c."Id";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EarliestImportedTransactionUtc",
                table: "OpenBankingConnections");

            migrationBuilder.DropColumn(
                name: "InitialBackfillCompletedUtc",
                table: "OpenBankingConnections");

            migrationBuilder.DropColumn(
                name: "InitialBackfillStartedUtc",
                table: "OpenBankingConnections");

            migrationBuilder.DropColumn(
                name: "InitialBackfillWindowStartUtc",
                table: "OpenBankingConnections");

            migrationBuilder.DropColumn(
                name: "LatestImportedTransactionUtc",
                table: "OpenBankingConnections");
        }
    }
}
