using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSFinance.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMerchantKnowledgeCandidates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MerchantKnowledgeCandidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NormalizedDescriptor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RawDescriptorSample = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ObservedOccurrences = table.Column<int>(type: "integer", nullable: false),
                    ObservedSpendAbs = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ObservedDirection = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastAttemptUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextEligibleUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastOutcomeCode = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    InvestigationSummaryJson = table.Column<string>(type: "jsonb", nullable: true),
                    ProposedTaxonomyDomainId = table.Column<int>(type: "integer", nullable: true),
                    ProposedTaxonomyCategoryId = table.Column<int>(type: "integer", nullable: true),
                    ProposedTaxonomySubcategoryId = table.Column<int>(type: "integer", nullable: true),
                    ProposedConfidence = table.Column<double>(type: "double precision", nullable: true),
                    PromotedKnowledgeId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MerchantKnowledgeCandidates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MerchantKnowledgeCandidates_NormalizedDescriptor",
                table: "MerchantKnowledgeCandidates",
                column: "NormalizedDescriptor",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MerchantKnowledgeCandidates_Status_NextEligibleUtc",
                table: "MerchantKnowledgeCandidates",
                columns: new[] { "Status", "NextEligibleUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MerchantKnowledgeCandidates");
        }
    }
}
