using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSFinance.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMerchantCategorizationEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CategorizationCharacteristicsVersion",
                table: "Transactions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CategorizationRuleKey",
                table: "Transactions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CategorizationSignal",
                table: "Transactions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CategorizedUtc",
                table: "Transactions",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CategorizationCharacteristicsVersion",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "CategorizationRuleKey",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "CategorizationSignal",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "CategorizedUtc",
                table: "Transactions");
        }
    }
}
