using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSFinance.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PersistentChatConversationMemoryLayer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConversationThreads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    LastMessageUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    LastContextRefreshUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ActiveSummaryVersion = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationThreads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConversationThreads_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ConversationContextBuildLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationThreadId = table.Column<Guid>(type: "uuid", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    TaskType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ModelClass = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    IncludedRecentMessageCount = table.Column<int>(type: "integer", nullable: false),
                    IncludedSummaryVersion = table.Column<int>(type: "integer", nullable: true),
                    IncludedStateVersion = table.Column<int>(type: "integer", nullable: true),
                    EstimatedPromptTokenCount = table.Column<int>(type: "integer", nullable: false),
                    TrimReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationContextBuildLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConversationContextBuildLogs_ConversationThreads_Conversati~",
                        column: x => x.ConversationThreadId,
                        principalTable: "ConversationThreads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ConversationMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationThreadId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<string>(type: "character varying(6000)", maxLength: 6000, nullable: false),
                    MessageOrder = table.Column<int>(type: "integer", nullable: false),
                    Topic = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    ModelUsed = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    TaskType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    IsResolved = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    WasTrimEligible = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    WasSummaryDerived = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CorrelationId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConversationMessages_ConversationThreads_ConversationThread~",
                        column: x => x.ConversationThreadId,
                        principalTable: "ConversationThreads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ConversationStateSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationThreadId = table.Column<Guid>(type: "uuid", nullable: false),
                    StateJson = table.Column<string>(type: "jsonb", nullable: false),
                    StateVersion = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<int>(type: "integer", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationStateSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConversationStateSnapshots_ConversationThreads_Conversation~",
                        column: x => x.ConversationThreadId,
                        principalTable: "ConversationThreads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ConversationSummaries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationThreadId = table.Column<Guid>(type: "uuid", nullable: false),
                    SummaryText = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    SummaryScope = table.Column<int>(type: "integer", nullable: false),
                    MessageStartOrder = table.Column<int>(type: "integer", nullable: false),
                    MessageEndOrder = table.Column<int>(type: "integer", nullable: false),
                    SummaryVersion = table.Column<int>(type: "integer", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationSummaries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConversationSummaries_ConversationThreads_ConversationThrea~",
                        column: x => x.ConversationThreadId,
                        principalTable: "ConversationThreads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationContextBuildLogs_ConversationThreadId",
                table: "ConversationContextBuildLogs",
                column: "ConversationThreadId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationContextBuildLogs_CorrelationId",
                table: "ConversationContextBuildLogs",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationContextBuildLogs_CreatedUtc",
                table: "ConversationContextBuildLogs",
                column: "CreatedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMessages_ConversationThreadId",
                table: "ConversationMessages",
                column: "ConversationThreadId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMessages_ConversationThreadId_MessageOrder",
                table: "ConversationMessages",
                columns: new[] { "ConversationThreadId", "MessageOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMessages_CreatedUtc",
                table: "ConversationMessages",
                column: "CreatedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationStateSnapshots_ConversationThreadId",
                table: "ConversationStateSnapshots",
                column: "ConversationThreadId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationStateSnapshots_ConversationThreadId_StateVersion",
                table: "ConversationStateSnapshots",
                columns: new[] { "ConversationThreadId", "StateVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConversationStateSnapshots_CreatedUtc",
                table: "ConversationStateSnapshots",
                column: "CreatedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationSummaries_ConversationThreadId",
                table: "ConversationSummaries",
                column: "ConversationThreadId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationSummaries_ConversationThreadId_SummaryVersion",
                table: "ConversationSummaries",
                columns: new[] { "ConversationThreadId", "SummaryVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConversationSummaries_CreatedUtc",
                table: "ConversationSummaries",
                column: "CreatedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationThreads_LastMessageUtc",
                table: "ConversationThreads",
                column: "LastMessageUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationThreads_UpdatedUtc",
                table: "ConversationThreads",
                column: "UpdatedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationThreads_UserId",
                table: "ConversationThreads",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationThreads_UserId_Status",
                table: "ConversationThreads",
                columns: new[] { "UserId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConversationContextBuildLogs");

            migrationBuilder.DropTable(
                name: "ConversationMessages");

            migrationBuilder.DropTable(
                name: "ConversationStateSnapshots");

            migrationBuilder.DropTable(
                name: "ConversationSummaries");

            migrationBuilder.DropTable(
                name: "ConversationThreads");
        }
    }
}
