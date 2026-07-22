using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSFinance.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCurationUsageAndIssues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastMatchedUtc",
                table: "MerchantKnowledge",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MatchCount",
                table: "MerchantKnowledge",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "MerchantKnowledgeCurationIssues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    KnowledgeId = table.Column<Guid>(type: "uuid", nullable: false),
                    IssueType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EvidenceJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MerchantKnowledgeCurationIssues", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MerchantKnowledgeCurationIssues_KnowledgeId_IssueType",
                table: "MerchantKnowledgeCurationIssues",
                columns: new[] { "KnowledgeId", "IssueType" });

            migrationBuilder.CreateIndex(
                name: "IX_MerchantKnowledgeCurationIssues_Status",
                table: "MerchantKnowledgeCurationIssues",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MerchantKnowledgeCurationIssues");

            migrationBuilder.DropColumn(
                name: "LastMatchedUtc",
                table: "MerchantKnowledge");

            migrationBuilder.DropColumn(
                name: "MatchCount",
                table: "MerchantKnowledge");
        }
    }
}
