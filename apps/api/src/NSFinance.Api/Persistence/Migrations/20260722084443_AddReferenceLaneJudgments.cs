using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSFinance.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReferenceLaneJudgments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReferenceLaneJudgments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DefinitionKey = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Confidence = table.Column<double>(type: "double precision", nullable: false),
                    OutcomeCode = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    SummaryJson = table.Column<string>(type: "jsonb", nullable: true),
                    CharacteristicsVersion = table.Column<int>(type: "integer", nullable: false),
                    JudgedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReferenceLaneJudgments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReferenceLaneJudgments_TransactionId_CharacteristicsVersion",
                table: "ReferenceLaneJudgments",
                columns: new[] { "TransactionId", "CharacteristicsVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReferenceLaneJudgments_UserId_JudgedUtc",
                table: "ReferenceLaneJudgments",
                columns: new[] { "UserId", "JudgedUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReferenceLaneJudgments");
        }
    }
}
