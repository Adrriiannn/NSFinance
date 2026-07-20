using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSFinance.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMerchantKnowledgeUserScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MerchantKnowledge_NormalizedPattern",
                table: "MerchantKnowledge");

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "MerchantKnowledge",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MerchantKnowledge_UserId_NormalizedPattern",
                table: "MerchantKnowledge",
                columns: new[] { "UserId", "NormalizedPattern" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MerchantKnowledge_UserId_NormalizedPattern",
                table: "MerchantKnowledge");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "MerchantKnowledge");

            migrationBuilder.CreateIndex(
                name: "IX_MerchantKnowledge_NormalizedPattern",
                table: "MerchantKnowledge",
                column: "NormalizedPattern",
                unique: true);
        }
    }
}
