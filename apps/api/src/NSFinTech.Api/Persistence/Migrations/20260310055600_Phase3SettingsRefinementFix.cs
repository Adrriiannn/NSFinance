using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSFinTech.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase3SettingsRefinementFix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Subcategory",
                table: "SupportRequests",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Subcategory",
                table: "SupportRequests");
        }
    }
}
