using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSFinance.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase3ProfileSettingsAndPolicyContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CountryRegion",
                table: "Users",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateOfBirth",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmploymentStatus",
                table: "Users",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FinancialFocusJson",
                table: "Users",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FullName",
                table: "Users",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Handle",
                table: "Users",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IncomeStability",
                table: "Users",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhoneNumber",
                table: "Users",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrimaryFinancialConcern",
                table: "Users",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfileImageUrl",
                table: "Users",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfileSubtitle",
                table: "Users",
                type: "character varying(180)",
                maxLength: 180,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TwoFactorEnabled",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ConnectionId",
                table: "SupportRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactEmail",
                table: "SupportRequests",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DiagnosticsJson",
                table: "SupportRequests",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");

            migrationBuilder.AddColumn<Guid>(
                name: "LinkedBankAccountId",
                table: "SupportRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScreenshotReference",
                table: "SupportRequests",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "SupportRequests",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ContentMarkdown",
                table: "PolicyVersions",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(
                """
                UPDATE "Users"
                SET "FullName" = COALESCE(NULLIF("DisplayName", ''), "PrimaryEmail")
                WHERE "FullName" = '';
                """);

            migrationBuilder.Sql(
                """
                UPDATE "SupportRequests"
                SET "DiagnosticsJson" = '{}'::jsonb
                WHERE "DiagnosticsJson" IS NULL;
                """);

            migrationBuilder.Sql(
                """
                UPDATE "SupportRequests"
                SET "Title" = LEFT(COALESCE(NULLIF("Category", ''), 'Support request'), 160)
                WHERE "Title" = '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CountryRegion",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DateOfBirth",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "EmploymentStatus",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "FinancialFocusJson",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "FullName",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Handle",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IncomeStability",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PhoneNumber",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PrimaryFinancialConcern",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ProfileImageUrl",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ProfileSubtitle",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TwoFactorEnabled",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ConnectionId",
                table: "SupportRequests");

            migrationBuilder.DropColumn(
                name: "ContactEmail",
                table: "SupportRequests");

            migrationBuilder.DropColumn(
                name: "DiagnosticsJson",
                table: "SupportRequests");

            migrationBuilder.DropColumn(
                name: "LinkedBankAccountId",
                table: "SupportRequests");

            migrationBuilder.DropColumn(
                name: "ScreenshotReference",
                table: "SupportRequests");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "SupportRequests");

            migrationBuilder.DropColumn(
                name: "ContentMarkdown",
                table: "PolicyVersions");
        }
    }
}
