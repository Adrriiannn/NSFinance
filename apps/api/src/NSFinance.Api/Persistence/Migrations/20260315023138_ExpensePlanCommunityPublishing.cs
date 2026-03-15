using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSFinance.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExpensePlanCommunityPublishing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ImportedFromPublicPlanId",
                table: "ExpensePlans",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ExpensePlanPublications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourcePlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourcePlanVersion = table.Column<int>(type: "integer", nullable: false),
                    CreatorDisplayNameSnapshot = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    CreatorTagSnapshot = table.Column<string>(type: "character varying(90)", maxLength: 90, nullable: false),
                    PublicTitle = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    PublicDescription = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    TagsJson = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    PublicationStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ModerationStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ModerationSummary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PlanSnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    PlanType = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ExpectedSpendTotal = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    IsTemplate = table.Column<bool>(type: "boolean", nullable: false),
                    IsRecurring = table.Column<bool>(type: "boolean", nullable: false),
                    LikeCount = table.Column<int>(type: "integer", nullable: false),
                    DownloadCount = table.Column<int>(type: "integer", nullable: false),
                    ReportCount = table.Column<int>(type: "integer", nullable: false),
                    TrendingScore = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastModeratedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastRescannedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastReportedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UnpublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RemovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpensePlanPublications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExpensePlanPublications_ExpensePlans_SourcePlanId",
                        column: x => x.SourcePlanId,
                        principalTable: "ExpensePlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExpensePlanPublications_Users_CreatorUserId",
                        column: x => x.CreatorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExpensePlanPublicationDownloads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpensePlanPublicationDownloads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExpensePlanPublicationDownloads_ExpensePlanPublications_Pub~",
                        column: x => x.PublicationId,
                        principalTable: "ExpensePlanPublications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExpensePlanPublicationDownloads_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExpensePlanPublicationLikes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpensePlanPublicationLikes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExpensePlanPublicationLikes_ExpensePlanPublications_Publica~",
                        column: x => x.PublicationId,
                        principalTable: "ExpensePlanPublications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExpensePlanPublicationLikes_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExpensePlanPublicationModerationEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TriggerType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ResultStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    MatchedRulesJson = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpensePlanPublicationModerationEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExpensePlanPublicationModerationEvents_ExpensePlanPublicati~",
                        column: x => x.PublicationId,
                        principalTable: "ExpensePlanPublications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExpensePlanPublicationReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReporterUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    Notes = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: true),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpensePlanPublicationReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExpensePlanPublicationReports_ExpensePlanPublications_Publi~",
                        column: x => x.PublicationId,
                        principalTable: "ExpensePlanPublications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExpensePlanPublicationReports_Users_ReporterUserId",
                        column: x => x.ReporterUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExpensePlans_ImportedFromPublicPlanId",
                table: "ExpensePlans",
                column: "ImportedFromPublicPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpensePlanPublicationDownloads_PublicationId_CreatedAtUtc",
                table: "ExpensePlanPublicationDownloads",
                columns: new[] { "PublicationId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpensePlanPublicationDownloads_UserId_CreatedAtUtc",
                table: "ExpensePlanPublicationDownloads",
                columns: new[] { "UserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpensePlanPublicationLikes_PublicationId_UserId",
                table: "ExpensePlanPublicationLikes",
                columns: new[] { "PublicationId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExpensePlanPublicationLikes_UserId",
                table: "ExpensePlanPublicationLikes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpensePlanPublicationModerationEvents_PublicationId_Create~",
                table: "ExpensePlanPublicationModerationEvents",
                columns: new[] { "PublicationId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpensePlanPublicationReports_PublicationId_ReporterUserId_~",
                table: "ExpensePlanPublicationReports",
                columns: new[] { "PublicationId", "ReporterUserId", "Reason" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpensePlanPublicationReports_ReporterUserId",
                table: "ExpensePlanPublicationReports",
                column: "ReporterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpensePlanPublicationReports_Status_CreatedAtUtc",
                table: "ExpensePlanPublicationReports",
                columns: new[] { "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpensePlanPublications_CreatorUserId_CreatedAtUtc",
                table: "ExpensePlanPublications",
                columns: new[] { "CreatorUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpensePlanPublications_PlanType_PublicationStatus",
                table: "ExpensePlanPublications",
                columns: new[] { "PlanType", "PublicationStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpensePlanPublications_PublicationStatus_PublishedAtUtc",
                table: "ExpensePlanPublications",
                columns: new[] { "PublicationStatus", "PublishedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpensePlanPublications_SourcePlanId",
                table: "ExpensePlanPublications",
                column: "SourcePlanId");

            migrationBuilder.AddForeignKey(
                name: "FK_ExpensePlans_ExpensePlanPublications_ImportedFromPublicPlan~",
                table: "ExpensePlans",
                column: "ImportedFromPublicPlanId",
                principalTable: "ExpensePlanPublications",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExpensePlans_ExpensePlanPublications_ImportedFromPublicPlan~",
                table: "ExpensePlans");

            migrationBuilder.DropTable(
                name: "ExpensePlanPublicationDownloads");

            migrationBuilder.DropTable(
                name: "ExpensePlanPublicationLikes");

            migrationBuilder.DropTable(
                name: "ExpensePlanPublicationModerationEvents");

            migrationBuilder.DropTable(
                name: "ExpensePlanPublicationReports");

            migrationBuilder.DropTable(
                name: "ExpensePlanPublications");

            migrationBuilder.DropIndex(
                name: "IX_ExpensePlans_ImportedFromPublicPlanId",
                table: "ExpensePlans");

            migrationBuilder.DropColumn(
                name: "ImportedFromPublicPlanId",
                table: "ExpensePlans");
        }
    }
}
