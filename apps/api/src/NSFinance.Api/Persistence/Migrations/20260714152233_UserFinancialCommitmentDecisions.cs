using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSFinance.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UserFinancialCommitmentDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserFinancialCommitments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetCommitmentId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OriginType = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    State = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    DecisionMode = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    LastAction = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    SnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    OverrideJson = table.Column<string>(type: "jsonb", nullable: true),
                    EffectiveAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    EffectiveNextDateUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    ConfirmedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DismissedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserFinancialCommitments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserFinancialCommitments_FinancialAccounts_EffectiveAccount~",
                        column: x => x.EffectiveAccountId,
                        principalTable: "FinancialAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_UserFinancialCommitments_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserFinancialCommitments_EffectiveAccountId",
                table: "UserFinancialCommitments",
                column: "EffectiveAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_UserFinancialCommitments_UserId_State_EffectiveNextDateUtc",
                table: "UserFinancialCommitments",
                columns: new[] { "UserId", "State", "EffectiveNextDateUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UserFinancialCommitments_UserId_TargetCommitmentId",
                table: "UserFinancialCommitments",
                columns: new[] { "UserId", "TargetCommitmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserFinancialCommitments_UserId_UpdatedUtc",
                table: "UserFinancialCommitments",
                columns: new[] { "UserId", "UpdatedUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserFinancialCommitments");
        }
    }
}
