using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSFinance.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LinkedInternalTransferSemantics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LinkedTransferMatchedUtc",
                table: "Transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LinkedTransferTransactionId",
                table: "Transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TransferKind",
                table: "Transactions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_LinkedTransferTransactionId",
                table: "Transactions",
                column: "LinkedTransferTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_TransferKind",
                table: "Transactions",
                column: "TransferKind");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_LinkedTransferTransactionId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_TransferKind",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "LinkedTransferMatchedUtc",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "LinkedTransferTransactionId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "TransferKind",
                table: "Transactions");
        }
    }
}
