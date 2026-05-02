using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSFinance.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlacesIntelligenceV2Persistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlaceRegistry",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProviderPlaceId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    FirstSeenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastRefreshedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    InternalTagsJson = table.Column<string>(type: "jsonb", nullable: false),
                    InternalMetricsJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaceRegistry", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlacesShortLivedCache",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PlaceId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    FieldMaskHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlacesShortLivedCache", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlaceRegistry_LastSeenAtUtc",
                table: "PlaceRegistry",
                column: "LastSeenAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PlaceRegistry_Provider_ProviderPlaceId",
                table: "PlaceRegistry",
                columns: new[] { "Provider", "ProviderPlaceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlacesShortLivedCache_ExpiresAtUtc",
                table: "PlacesShortLivedCache",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PlacesShortLivedCache_Provider_PlaceId_FieldMaskHash",
                table: "PlacesShortLivedCache",
                columns: new[] { "Provider", "PlaceId", "FieldMaskHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlaceRegistry");

            migrationBuilder.DropTable(
                name: "PlacesShortLivedCache");
        }
    }
}
