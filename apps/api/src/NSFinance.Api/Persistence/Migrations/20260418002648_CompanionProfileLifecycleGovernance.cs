using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSFinance.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompanionProfileLifecycleGovernance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExplicitSignalsJson",
                table: "UserFinancialContextProfiles",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "FreshnessState",
                table: "UserFinancialContextProfiles",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "fresh");

            migrationBuilder.AddColumn<string>(
                name: "InferredSignalsJson",
                table: "UserFinancialContextProfiles",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastRefreshedUtc",
                table: "UserFinancialContextProfiles",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "timezone('utc', now())");

            migrationBuilder.AddColumn<int>(
                name: "ProfileSchemaVersion",
                table: "UserFinancialContextProfiles",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "SignalMetadataJson",
                table: "UserFinancialContextProfiles",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExplicitSignalsJson",
                table: "UserFinancialContextProfiles");

            migrationBuilder.DropColumn(
                name: "FreshnessState",
                table: "UserFinancialContextProfiles");

            migrationBuilder.DropColumn(
                name: "InferredSignalsJson",
                table: "UserFinancialContextProfiles");

            migrationBuilder.DropColumn(
                name: "LastRefreshedUtc",
                table: "UserFinancialContextProfiles");

            migrationBuilder.DropColumn(
                name: "ProfileSchemaVersion",
                table: "UserFinancialContextProfiles");

            migrationBuilder.DropColumn(
                name: "SignalMetadataJson",
                table: "UserFinancialContextProfiles");
        }
    }
}
