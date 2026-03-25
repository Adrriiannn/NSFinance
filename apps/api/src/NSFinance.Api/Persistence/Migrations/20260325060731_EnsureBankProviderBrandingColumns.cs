using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSFinance.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnsureBankProviderBrandingColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "OpenBankingConnections"
                    ADD COLUMN IF NOT EXISTS "ProviderId" character varying(180);

                ALTER TABLE "OpenBankingConnections"
                    ADD COLUMN IF NOT EXISTS "ProviderIconUri" character varying(1024);

                ALTER TABLE "OpenBankingConnections"
                    ADD COLUMN IF NOT EXISTS "ProviderLogoUri" character varying(1024);

                ALTER TABLE "OpenBankingConnections"
                    ADD COLUMN IF NOT EXISTS "ProviderBrandBgColor" character varying(32);

                ALTER TABLE "OpenBankingConnections"
                    ADD COLUMN IF NOT EXISTS "BrandingLastSyncedAtUtc" timestamp with time zone;

                UPDATE "OpenBankingConnections"
                SET "ProviderId" = "ProviderConnectionReference"
                WHERE "ProviderId" IS NULL AND "ProviderConnectionReference" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "OpenBankingConnections"
                    DROP COLUMN IF EXISTS "BrandingLastSyncedAtUtc";

                ALTER TABLE "OpenBankingConnections"
                    DROP COLUMN IF EXISTS "ProviderBrandBgColor";

                ALTER TABLE "OpenBankingConnections"
                    DROP COLUMN IF EXISTS "ProviderLogoUri";

                ALTER TABLE "OpenBankingConnections"
                    DROP COLUMN IF EXISTS "ProviderIconUri";

                ALTER TABLE "OpenBankingConnections"
                    DROP COLUMN IF EXISTS "ProviderId";
                """);
        }
    }
}
