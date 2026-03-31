using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSFinance.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RawBankTransactionProjectionLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProjectedTransactionId",
                table: "RawBankTransactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RawBankTransactions_ProjectedTransactionId",
                table: "RawBankTransactions",
                column: "ProjectedTransactionId");

            migrationBuilder.AddForeignKey(
                name: "FK_RawBankTransactions_Transactions_ProjectedTransactionId",
                table: "RawBankTransactions",
                column: "ProjectedTransactionId",
                principalTable: "Transactions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RawBankTransactions_Transactions_ProjectedTransactionId",
                table: "RawBankTransactions");

            migrationBuilder.DropIndex(
                name: "IX_RawBankTransactions_ProjectedTransactionId",
                table: "RawBankTransactions");

            migrationBuilder.DropColumn(
                name: "ProjectedTransactionId",
                table: "RawBankTransactions");
        }
    }
}
