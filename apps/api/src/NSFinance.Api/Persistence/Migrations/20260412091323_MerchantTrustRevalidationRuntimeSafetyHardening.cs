using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSFinance.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MerchantTrustRevalidationRuntimeSafetyHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MerchantRevalidationRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MerchantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    TriggerReason = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    DecisionCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    PreviousStatus = table.Column<int>(type: "integer", nullable: false),
                    NewStatus = table.Column<int>(type: "integer", nullable: false),
                    StatusChanged = table.Column<bool>(type: "boolean", nullable: false),
                    AliasTrustChanges = table.Column<int>(type: "integer", nullable: false),
                    RequiresUnresolvedReview = table.Column<bool>(type: "boolean", nullable: false),
                    ContradictionDetected = table.Column<bool>(type: "boolean", nullable: false),
                    LeadingEvidenceSummary = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: true),
                    ResultCode = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    DetailsJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MerchantRevalidationRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MerchantRevalidationRecords_Merchants_MerchantId",
                        column: x => x.MerchantId,
                        principalTable: "Merchants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MerchantRevalidationRecords_AttemptedUtc",
                table: "MerchantRevalidationRecords",
                column: "AttemptedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MerchantRevalidationRecords_MerchantId",
                table: "MerchantRevalidationRecords",
                column: "MerchantId");

            migrationBuilder.CreateIndex(
                name: "IX_MerchantRevalidationRecords_MerchantId_AttemptedUtc",
                table: "MerchantRevalidationRecords",
                columns: new[] { "MerchantId", "AttemptedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MerchantRevalidationRecords_Outcome",
                table: "MerchantRevalidationRecords",
                column: "Outcome");

            migrationBuilder.CreateIndex(
                name: "IX_MerchantRevalidationRecords_ResultCode",
                table: "MerchantRevalidationRecords",
                column: "ResultCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MerchantRevalidationRecords");
        }
    }
}
