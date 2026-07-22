using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSFinance.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMerchantKnowledgeFindings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MerchantKnowledgeFindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateId = table.Column<Guid>(type: "uuid", nullable: false),
                    KnowledgeId = table.Column<Guid>(type: "uuid", nullable: true),
                    CanonicalName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AcceptanceDecision = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    OutcomeCode = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    FindingsJson = table.Column<string>(type: "jsonb", nullable: false),
                    CharacteristicsVersion = table.Column<int>(type: "integer", nullable: false),
                    FindingVersion = table.Column<int>(type: "integer", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MerchantKnowledgeFindings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MerchantKnowledgeFindings_CandidateId_FindingVersion",
                table: "MerchantKnowledgeFindings",
                columns: new[] { "CandidateId", "FindingVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MerchantKnowledgeFindings_KnowledgeId",
                table: "MerchantKnowledgeFindings",
                column: "KnowledgeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MerchantKnowledgeFindings");
        }
    }
}
