using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSFinance.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMerchantKnowledgeSeedRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MerchantKnowledgeSeedRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CharacteristicsVersion = table.Column<int>(type: "integer", nullable: false),
                    PlanHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    InsertedCount = table.Column<int>(type: "integer", nullable: false),
                    RetargetedCount = table.Column<int>(type: "integer", nullable: false),
                    DeactivatedCount = table.Column<int>(type: "integer", nullable: false),
                    ReopenedCount = table.Column<int>(type: "integer", nullable: false),
                    SkippedDuplicateCount = table.Column<int>(type: "integer", nullable: false),
                    ChangesJson = table.Column<string>(type: "jsonb", nullable: false),
                    AppliedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevertedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MerchantKnowledgeSeedRuns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MerchantKnowledgeSeedRuns_CharacteristicsVersion",
                table: "MerchantKnowledgeSeedRuns",
                column: "CharacteristicsVersion",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MerchantKnowledgeSeedRuns");
        }
    }
}
