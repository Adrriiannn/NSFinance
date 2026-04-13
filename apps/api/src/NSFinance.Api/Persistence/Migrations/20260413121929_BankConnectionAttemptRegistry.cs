using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSFinance.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BankConnectionAttemptRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BankConnectionAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderName = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ProviderEnvironment = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    LaunchOriginPath = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    AppReturnUri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    CallbackState = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    PublicToken = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    ExpiresUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AuthLaunchedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CallbackHandledUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AppReturnInitiatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AppReturnConfirmedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    SupersededByAttemptId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankConnectionAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankConnectionAttempts_BankConnectionAttempts_SupersededByA~",
                        column: x => x.SupersededByAttemptId,
                        principalTable: "BankConnectionAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_BankConnectionAttempts_OpenBankingConnections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "OpenBankingConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BankConnectionAttempts_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankConnectionAttempts_CallbackState",
                table: "BankConnectionAttempts",
                column: "CallbackState",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankConnectionAttempts_ConnectionId",
                table: "BankConnectionAttempts",
                column: "ConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_BankConnectionAttempts_CreatedUtc",
                table: "BankConnectionAttempts",
                column: "CreatedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_BankConnectionAttempts_PublicToken",
                table: "BankConnectionAttempts",
                column: "PublicToken");

            migrationBuilder.CreateIndex(
                name: "IX_BankConnectionAttempts_SupersededByAttemptId",
                table: "BankConnectionAttempts",
                column: "SupersededByAttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_BankConnectionAttempts_UserId",
                table: "BankConnectionAttempts",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_BankConnectionAttempts_UserId_Status_ExpiresUtc",
                table: "BankConnectionAttempts",
                columns: new[] { "UserId", "Status", "ExpiresUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BankConnectionAttempts");
        }
    }
}
