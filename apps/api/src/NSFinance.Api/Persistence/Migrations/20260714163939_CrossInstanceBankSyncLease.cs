using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSFinance.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CrossInstanceBankSyncLease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SyncLeaseExpiresUtc",
                table: "OpenBankingConnections",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SyncLeaseId",
                table: "OpenBankingConnections",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpenBankingConnections_SyncLeaseExpiresUtc",
                table: "OpenBankingConnections",
                column: "SyncLeaseExpiresUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OpenBankingConnections_SyncLeaseExpiresUtc",
                table: "OpenBankingConnections");

            migrationBuilder.DropColumn(
                name: "SyncLeaseExpiresUtc",
                table: "OpenBankingConnections");

            migrationBuilder.DropColumn(
                name: "SyncLeaseId",
                table: "OpenBankingConnections");
        }
    }
}
