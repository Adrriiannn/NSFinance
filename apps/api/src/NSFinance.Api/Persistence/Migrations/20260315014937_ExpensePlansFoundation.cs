using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSFinance.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExpensePlansFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExpensePlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatorDisplayNameSnapshot = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    CreatorTagSnapshot = table.Column<string>(type: "character varying(90)", maxLength: 90, nullable: false),
                    Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    PlanType = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    PlanOriginType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PlanVersion = table.Column<int>(type: "integer", nullable: false),
                    StartDateUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndDateUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ExpectedIncomeTotal = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ExpectedSpendTotal = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ExpectedRemainingTotal = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    TagsJson = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    StatusReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    ActivatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LockedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ArchivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastCalculatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SourcePlanId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsTemplate = table.Column<bool>(type: "boolean", nullable: false),
                    IsRecurring = table.Column<bool>(type: "boolean", nullable: false),
                    RecurrenceRuleJson = table.Column<string>(type: "jsonb", nullable: true),
                    IsShared = table.Column<bool>(type: "boolean", nullable: false),
                    SharingMode = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    SharedIdentity = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpensePlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExpensePlans_ExpensePlans_SourcePlanId",
                        column: x => x.SourcePlanId,
                        principalTable: "ExpensePlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExpensePlans_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExpensePlanLineItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaxonomyDomainId = table.Column<int>(type: "integer", nullable: false),
                    TaxonomyCategoryId = table.Column<int>(type: "integer", nullable: false),
                    TaxonomySubcategoryId = table.Column<int>(type: "integer", nullable: true),
                    DisplayNameSnapshot = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    HierarchyPathSnapshot = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    ExpectedAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Notes = table.Column<string>(type: "character varying(800)", maxLength: 800, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpensePlanLineItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExpensePlanLineItems_ExpensePlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "ExpensePlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExpensePlanLineItems_PlanId_SortOrder",
                table: "ExpensePlanLineItems",
                columns: new[] { "PlanId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpensePlanLineItems_TaxonomySubcategoryId",
                table: "ExpensePlanLineItems",
                column: "TaxonomySubcategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpensePlans_SharedIdentity",
                table: "ExpensePlans",
                column: "SharedIdentity");

            migrationBuilder.CreateIndex(
                name: "IX_ExpensePlans_SourcePlanId",
                table: "ExpensePlans",
                column: "SourcePlanId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpensePlans_UserId_Status_StartDateUtc",
                table: "ExpensePlans",
                columns: new[] { "UserId", "Status", "StartDateUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpensePlans_UserId_UpdatedAtUtc",
                table: "ExpensePlans",
                columns: new[] { "UserId", "UpdatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExpensePlanLineItems");

            migrationBuilder.DropTable(
                name: "ExpensePlans");
        }
    }
}
