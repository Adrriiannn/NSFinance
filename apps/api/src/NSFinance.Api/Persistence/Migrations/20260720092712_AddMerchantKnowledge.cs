using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSFinance.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMerchantKnowledge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MerchantKnowledge",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NormalizedPattern = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TaxonomyDomainId = table.Column<int>(type: "integer", nullable: true),
                    TaxonomyCategoryId = table.Column<int>(type: "integer", nullable: true),
                    TaxonomySubcategoryId = table.Column<int>(type: "integer", nullable: true),
                    DirectionExpectation = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    VerificationEvidenceJson = table.Column<string>(type: "jsonb", nullable: true),
                    Confidence = table.Column<double>(type: "double precision", nullable: false),
                    CharacteristicsVersion = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MerchantKnowledge", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MerchantKnowledge_IsActive_Source",
                table: "MerchantKnowledge",
                columns: new[] { "IsActive", "Source" });

            migrationBuilder.CreateIndex(
                name: "IX_MerchantKnowledge_NormalizedPattern",
                table: "MerchantKnowledge",
                column: "NormalizedPattern",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MerchantKnowledge");
        }
    }
}
