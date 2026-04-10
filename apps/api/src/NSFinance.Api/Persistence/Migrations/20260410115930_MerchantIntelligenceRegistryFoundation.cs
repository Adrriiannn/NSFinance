using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSFinance.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MerchantIntelligenceRegistryFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Merchants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CanonicalName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    NormalizedCanonicalName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    MerchantStatus = table.Column<int>(type: "integer", nullable: false),
                    MerchantType = table.Column<int>(type: "integer", nullable: false),
                    MerchantUsageType = table.Column<int>(type: "integer", nullable: false),
                    PrimaryCountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    OfficialWebsite = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    DescriptionSummary = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ParentMerchantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Merchants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Merchants_Merchants_ParentMerchantId",
                        column: x => x.ParentMerchantId,
                        principalTable: "Merchants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "UnresolvedMerchants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RawDescriptor = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    NormalizedDescriptor = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    FirstSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    LastSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    OccurrenceCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    LastInvestigationUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnresolvedMerchants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MerchantAliases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MerchantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AliasText = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    NormalizedAliasText = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    AliasType = table.Column<int>(type: "integer", nullable: false),
                    Confidence = table.Column<double>(type: "double precision", nullable: false),
                    IsExactMatchPreferred = table.Column<bool>(type: "boolean", nullable: false),
                    FirstSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    LastSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    Source = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MerchantAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MerchantAliases_Merchants_MerchantId",
                        column: x => x.MerchantId,
                        principalTable: "Merchants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MerchantBehaviorProfiles",
                columns: table => new
                {
                    MerchantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupportsSubscriptions = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsRecurringPayments = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsOneTimePurchases = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsMarketplacePayments = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsInAppPurchases = table.Column<bool>(type: "boolean", nullable: false),
                    AnnualRenewalsCommon = table.Column<bool>(type: "boolean", nullable: false),
                    RefundsCommon = table.Column<bool>(type: "boolean", nullable: false),
                    MixedUseRisk = table.Column<bool>(type: "boolean", nullable: false),
                    PaymentBehaviorConfidence = table.Column<double>(type: "double precision", nullable: false),
                    BehaviorSummary = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MerchantBehaviorProfiles", x => x.MerchantId);
                    table.ForeignKey(
                        name: "FK_MerchantBehaviorProfiles_Merchants_MerchantId",
                        column: x => x.MerchantId,
                        principalTable: "Merchants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MerchantCategoryHints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MerchantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DomainId = table.Column<int>(type: "integer", nullable: false),
                    CategoryId = table.Column<int>(type: "integer", nullable: false),
                    SubcategoryId = table.Column<int>(type: "integer", nullable: true),
                    Confidence = table.Column<double>(type: "double precision", nullable: false),
                    HintStrength = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MerchantCategoryHints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MerchantCategoryHints_Merchants_MerchantId",
                        column: x => x.MerchantId,
                        principalTable: "Merchants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MerchantEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MerchantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceType = table.Column<int>(type: "integer", nullable: false),
                    EvidenceSummary = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: false),
                    Confidence = table.Column<double>(type: "double precision", nullable: false),
                    SourceReference = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CapturedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MerchantEvidence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MerchantEvidence_Merchants_MerchantId",
                        column: x => x.MerchantId,
                        principalTable: "Merchants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MerchantAliases_MerchantId",
                table: "MerchantAliases",
                column: "MerchantId");

            migrationBuilder.CreateIndex(
                name: "IX_MerchantAliases_MerchantId_IsExactMatchPreferred_IsActive",
                table: "MerchantAliases",
                columns: new[] { "MerchantId", "IsExactMatchPreferred", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_MerchantAliases_MerchantId_NormalizedAliasText_AliasType",
                table: "MerchantAliases",
                columns: new[] { "MerchantId", "NormalizedAliasText", "AliasType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MerchantAliases_NormalizedAliasText",
                table: "MerchantAliases",
                column: "NormalizedAliasText");

            migrationBuilder.CreateIndex(
                name: "IX_MerchantAliases_NormalizedAliasText_IsActive",
                table: "MerchantAliases",
                columns: new[] { "NormalizedAliasText", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_MerchantCategoryHints_DomainId_CategoryId_SubcategoryId",
                table: "MerchantCategoryHints",
                columns: new[] { "DomainId", "CategoryId", "SubcategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_MerchantCategoryHints_MerchantId",
                table: "MerchantCategoryHints",
                column: "MerchantId");

            migrationBuilder.CreateIndex(
                name: "IX_MerchantCategoryHints_MerchantId_DomainId_CategoryId_Subcat~",
                table: "MerchantCategoryHints",
                columns: new[] { "MerchantId", "DomainId", "CategoryId", "SubcategoryId", "Source" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MerchantCategoryHints_MerchantId_IsActive",
                table: "MerchantCategoryHints",
                columns: new[] { "MerchantId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_MerchantEvidence_CapturedUtc",
                table: "MerchantEvidence",
                column: "CapturedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MerchantEvidence_EvidenceType",
                table: "MerchantEvidence",
                column: "EvidenceType");

            migrationBuilder.CreateIndex(
                name: "IX_MerchantEvidence_MerchantId",
                table: "MerchantEvidence",
                column: "MerchantId");

            migrationBuilder.CreateIndex(
                name: "IX_Merchants_MerchantStatus",
                table: "Merchants",
                column: "MerchantStatus");

            migrationBuilder.CreateIndex(
                name: "IX_Merchants_MerchantType",
                table: "Merchants",
                column: "MerchantType");

            migrationBuilder.CreateIndex(
                name: "IX_Merchants_MerchantUsageType",
                table: "Merchants",
                column: "MerchantUsageType");

            migrationBuilder.CreateIndex(
                name: "IX_Merchants_NormalizedCanonicalName",
                table: "Merchants",
                column: "NormalizedCanonicalName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Merchants_ParentMerchantId",
                table: "Merchants",
                column: "ParentMerchantId");

            migrationBuilder.CreateIndex(
                name: "IX_UnresolvedMerchants_LastInvestigationUtc",
                table: "UnresolvedMerchants",
                column: "LastInvestigationUtc");

            migrationBuilder.CreateIndex(
                name: "IX_UnresolvedMerchants_LastSeenUtc",
                table: "UnresolvedMerchants",
                column: "LastSeenUtc");

            migrationBuilder.CreateIndex(
                name: "IX_UnresolvedMerchants_NormalizedDescriptor",
                table: "UnresolvedMerchants",
                column: "NormalizedDescriptor",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UnresolvedMerchants_Status",
                table: "UnresolvedMerchants",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MerchantAliases");

            migrationBuilder.DropTable(
                name: "MerchantBehaviorProfiles");

            migrationBuilder.DropTable(
                name: "MerchantCategoryHints");

            migrationBuilder.DropTable(
                name: "MerchantEvidence");

            migrationBuilder.DropTable(
                name: "UnresolvedMerchants");

            migrationBuilder.DropTable(
                name: "Merchants");
        }
    }
}
