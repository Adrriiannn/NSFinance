using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSFinance.Api.Persistence.Migrations
{
    public partial class BankProviderBrandingMetadata : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "BrandingLastSyncedAtUtc",
                table: "OpenBankingConnections",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderBrandBgColor",
                table: "OpenBankingConnections",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderIconUri",
                table: "OpenBankingConnections",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderId",
                table: "OpenBankingConnections",
                type: "character varying(180)",
                maxLength: 180,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderLogoUri",
                table: "OpenBankingConnections",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "OpenBankingConnections"
                SET "ProviderId" = "ProviderConnectionReference"
                WHERE "ProviderId" IS NULL AND "ProviderConnectionReference" IS NOT NULL;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BrandingLastSyncedAtUtc",
                table: "OpenBankingConnections");

            migrationBuilder.DropColumn(
                name: "ProviderBrandBgColor",
                table: "OpenBankingConnections");

            migrationBuilder.DropColumn(
                name: "ProviderIconUri",
                table: "OpenBankingConnections");

            migrationBuilder.DropColumn(
                name: "ProviderId",
                table: "OpenBankingConnections");

            migrationBuilder.DropColumn(
                name: "ProviderLogoUri",
                table: "OpenBankingConnections");
        }
    }
}

