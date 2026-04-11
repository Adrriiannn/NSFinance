using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSFinance.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChatTurnLifecycleHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ConversationTurnId",
                table: "ConversationMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ConversationTurnId",
                table: "ConversationContextBuildLogs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ConversationTurns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationThreadId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientRequestId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TaskType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ModelClass = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ModelUsed = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ModelDeployment = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ContextSource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EstimatedPromptTokenCount = table.Column<int>(type: "integer", nullable: true),
                    ResponseLatencyMs = table.Column<long>(type: "bigint", nullable: true),
                    UserMessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssistantMessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    WasDeduplicated = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    StartedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    CompletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TimedOutUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationTurns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConversationTurns_ConversationThreads_ConversationThreadId",
                        column: x => x.ConversationThreadId,
                        principalTable: "ConversationThreads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMessages_ConversationTurnId",
                table: "ConversationMessages",
                column: "ConversationTurnId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationContextBuildLogs_ConversationTurnId",
                table: "ConversationContextBuildLogs",
                column: "ConversationTurnId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationTurns_ConversationThreadId",
                table: "ConversationTurns",
                column: "ConversationThreadId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationTurns_ConversationThreadId_ClientRequestId",
                table: "ConversationTurns",
                columns: new[] { "ConversationThreadId", "ClientRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConversationTurns_ConversationThreadId_Status",
                table: "ConversationTurns",
                columns: new[] { "ConversationThreadId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationTurns_UpdatedUtc",
                table: "ConversationTurns",
                column: "UpdatedUtc");

            migrationBuilder.AddForeignKey(
                name: "FK_ConversationContextBuildLogs_ConversationTurns_Conversation~",
                table: "ConversationContextBuildLogs",
                column: "ConversationTurnId",
                principalTable: "ConversationTurns",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ConversationMessages_ConversationTurns_ConversationTurnId",
                table: "ConversationMessages",
                column: "ConversationTurnId",
                principalTable: "ConversationTurns",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ConversationContextBuildLogs_ConversationTurns_Conversation~",
                table: "ConversationContextBuildLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_ConversationMessages_ConversationTurns_ConversationTurnId",
                table: "ConversationMessages");

            migrationBuilder.DropTable(
                name: "ConversationTurns");

            migrationBuilder.DropIndex(
                name: "IX_ConversationMessages_ConversationTurnId",
                table: "ConversationMessages");

            migrationBuilder.DropIndex(
                name: "IX_ConversationContextBuildLogs_ConversationTurnId",
                table: "ConversationContextBuildLogs");

            migrationBuilder.DropColumn(
                name: "ConversationTurnId",
                table: "ConversationMessages");

            migrationBuilder.DropColumn(
                name: "ConversationTurnId",
                table: "ConversationContextBuildLogs");
        }
    }
}
