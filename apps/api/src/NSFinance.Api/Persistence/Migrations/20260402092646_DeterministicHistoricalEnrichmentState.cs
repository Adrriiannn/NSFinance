using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSFinance.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeterministicHistoricalEnrichmentState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DeterministicEnrichmentVersion",
                table: "Transactions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastDeterministicEnrichedUtc",
                table: "Transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "HistoricalEnrichmentCheckpointUtc",
                table: "OpenBankingConnections",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "HistoricalEnrichmentCompletedUtc",
                table: "OpenBankingConnections",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "HistoricalEnrichmentStartedUtc",
                table: "OpenBankingConnections",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HistoricalEnrichmentVersion",
                table: "OpenBankingConnections",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NeedsHistoricalReclassification",
                table: "OpenBankingConnections",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_DeterministicEnrichmentVersion",
                table: "Transactions",
                column: "DeterministicEnrichmentVersion");

            migrationBuilder.CreateIndex(
                name: "IX_OpenBankingConnections_UserId_NeedsHistoricalReclassificati~",
                table: "OpenBankingConnections",
                columns: new[] { "UserId", "NeedsHistoricalReclassification" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_DeterministicEnrichmentVersion",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_OpenBankingConnections_UserId_NeedsHistoricalReclassificati~",
                table: "OpenBankingConnections");

            migrationBuilder.DropColumn(
                name: "DeterministicEnrichmentVersion",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "LastDeterministicEnrichedUtc",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "HistoricalEnrichmentCheckpointUtc",
                table: "OpenBankingConnections");

            migrationBuilder.DropColumn(
                name: "HistoricalEnrichmentCompletedUtc",
                table: "OpenBankingConnections");

            migrationBuilder.DropColumn(
                name: "HistoricalEnrichmentStartedUtc",
                table: "OpenBankingConnections");

            migrationBuilder.DropColumn(
                name: "HistoricalEnrichmentVersion",
                table: "OpenBankingConnections");

            migrationBuilder.DropColumn(
                name: "NeedsHistoricalReclassification",
                table: "OpenBankingConnections");
        }
    }
}
