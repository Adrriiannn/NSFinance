using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSFinance.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TransactionMetadataEnrichment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "MetadataUpdatedUtc",
                table: "Transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Transactions",
                type: "character varying(1200)",
                maxLength: 1200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                table: "Transactions",
                type: "character varying(140)",
                maxLength: 140,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TaxonomyCategoryId",
                table: "Transactions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TaxonomyDomainId",
                table: "Transactions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TaxonomySubcategoryId",
                table: "Transactions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_TaxonomyCategoryId",
                table: "Transactions",
                column: "TaxonomyCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_TaxonomySubcategoryId",
                table: "Transactions",
                column: "TaxonomySubcategoryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_TaxonomyCategoryId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_TaxonomySubcategoryId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "MetadataUpdatedUtc",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "Reason",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "TaxonomyCategoryId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "TaxonomyDomainId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "TaxonomySubcategoryId",
                table: "Transactions");
        }
    }
}
