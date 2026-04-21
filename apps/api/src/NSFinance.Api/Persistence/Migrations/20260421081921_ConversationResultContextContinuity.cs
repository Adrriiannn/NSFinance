using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSFinance.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConversationResultContextContinuity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConversationResultContexts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationThreadId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentResultSetId = table.Column<Guid>(type: "uuid", nullable: true),
                    BranchRootResultSetId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    ActiveUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationResultContexts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConversationResultContexts_ConversationThreads_Conversation~",
                        column: x => x.ConversationThreadId,
                        principalTable: "ConversationThreads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationResultContexts_ConversationThreadId_CreatedUtc",
                table: "ConversationResultContexts",
                columns: new[] { "ConversationThreadId", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationResultContexts_ConversationThreadId_ExpiresUtc",
                table: "ConversationResultContexts",
                columns: new[] { "ConversationThreadId", "ExpiresUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConversationResultContexts");
        }
    }
}
