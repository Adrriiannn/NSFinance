using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSFinance.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StatementsExportXlsx : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ConnectionId",
                table: "ExportRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConnectionLabel",
                table: "ExportRequests",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "EndDate",
                table: "ExportRequests",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "FileSizeBytes",
                table: "ExportRequests",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FinancialAccountId",
                table: "ExportRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Format",
                table: "ExportRequests",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PeriodPreset",
                table: "ExportRequests",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "StartDate",
                table: "ExportRequests",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConnectionId",
                table: "ExportRequests");

            migrationBuilder.DropColumn(
                name: "ConnectionLabel",
                table: "ExportRequests");

            migrationBuilder.DropColumn(
                name: "EndDate",
                table: "ExportRequests");

            migrationBuilder.DropColumn(
                name: "FileSizeBytes",
                table: "ExportRequests");

            migrationBuilder.DropColumn(
                name: "FinancialAccountId",
                table: "ExportRequests");

            migrationBuilder.DropColumn(
                name: "Format",
                table: "ExportRequests");

            migrationBuilder.DropColumn(
                name: "PeriodPreset",
                table: "ExportRequests");

            migrationBuilder.DropColumn(
                name: "StartDate",
                table: "ExportRequests");
        }
    }
}
