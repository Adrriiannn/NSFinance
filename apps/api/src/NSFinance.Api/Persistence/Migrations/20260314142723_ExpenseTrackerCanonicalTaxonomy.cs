using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSFinance.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExpenseTrackerCanonicalTaxonomy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LinkedOriginalEntryId",
                table: "ExpenseTrackerEntries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LinkedOriginalOffsetAmount",
                table: "ExpenseTrackerEntries",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TaxonomyCategoryId",
                table: "ExpenseTrackerEntries",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TaxonomyDomainId",
                table: "ExpenseTrackerEntries",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TaxonomySubcategoryId",
                table: "ExpenseTrackerEntries",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseTrackerEntries_LinkedOriginalEntryId",
                table: "ExpenseTrackerEntries",
                column: "LinkedOriginalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseTrackerEntries_TaxonomySubcategoryId",
                table: "ExpenseTrackerEntries",
                column: "TaxonomySubcategoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_ExpenseTrackerEntries_ExpenseTrackerEntries_LinkedOriginalE~",
                table: "ExpenseTrackerEntries",
                column: "LinkedOriginalEntryId",
                principalTable: "ExpenseTrackerEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExpenseTrackerEntries_ExpenseTrackerEntries_LinkedOriginalE~",
                table: "ExpenseTrackerEntries");

            migrationBuilder.DropIndex(
                name: "IX_ExpenseTrackerEntries_LinkedOriginalEntryId",
                table: "ExpenseTrackerEntries");

            migrationBuilder.DropIndex(
                name: "IX_ExpenseTrackerEntries_TaxonomySubcategoryId",
                table: "ExpenseTrackerEntries");

            migrationBuilder.DropColumn(
                name: "LinkedOriginalEntryId",
                table: "ExpenseTrackerEntries");

            migrationBuilder.DropColumn(
                name: "LinkedOriginalOffsetAmount",
                table: "ExpenseTrackerEntries");

            migrationBuilder.DropColumn(
                name: "TaxonomyCategoryId",
                table: "ExpenseTrackerEntries");

            migrationBuilder.DropColumn(
                name: "TaxonomyDomainId",
                table: "ExpenseTrackerEntries");

            migrationBuilder.DropColumn(
                name: "TaxonomySubcategoryId",
                table: "ExpenseTrackerEntries");
        }
    }
}
