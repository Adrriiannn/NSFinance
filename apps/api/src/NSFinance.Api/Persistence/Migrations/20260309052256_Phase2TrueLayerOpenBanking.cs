using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSFinance.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase2TrueLayerOpenBanking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OpenBankingConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderName = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ProviderEnvironment = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ProviderConnectionReference = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: true),
                    ProviderDisplayName = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: true),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    LastSuccessfulSyncUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSyncAttemptedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastErrorCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    LastErrorReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    AuthStateNonce = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    AuthStateExpiresUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenBankingConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpenBankingConnections_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BankConnectionTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    EncryptedRefreshToken = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    AccessTokenExpiresUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TokenObtainedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsRevoked = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    RevokedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankConnectionTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankConnectionTokens_OpenBankingConnections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "OpenBankingConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LinkedBankAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderAccountId = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    AccountType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    AccountSubType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    AccountNumberMetadataJson = table.Column<string>(type: "jsonb", nullable: true),
                    CurrentConnectionHealth = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RawPayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    FinancialAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LinkedBankAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LinkedBankAccounts_FinancialAccounts_FinancialAccountId",
                        column: x => x.FinancialAccountId,
                        principalTable: "FinancialAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_LinkedBankAccounts_OpenBankingConnections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "OpenBankingConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BankBalanceSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LinkedBankAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Available = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    Current = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    Overdraft = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CapturedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RawPayloadJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankBalanceSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankBalanceSnapshots_LinkedBankAccounts_LinkedBankAccountId",
                        column: x => x.LinkedBankAccountId,
                        principalTable: "LinkedBankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RawBankTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LinkedBankAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderTransactionId = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: true),
                    DedupeKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    BookedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    TransactionType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    TransactionStatus = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    RawPayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    ImportedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawBankTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RawBankTransactions_LinkedBankAccounts_LinkedBankAccountId",
                        column: x => x.LinkedBankAccountId,
                        principalTable: "LinkedBankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankBalanceSnapshots_LinkedBankAccountId",
                table: "BankBalanceSnapshots",
                column: "LinkedBankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_BankBalanceSnapshots_LinkedBankAccountId_CapturedUtc",
                table: "BankBalanceSnapshots",
                columns: new[] { "LinkedBankAccountId", "CapturedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BankConnectionTokens_ConnectionId",
                table: "BankConnectionTokens",
                column: "ConnectionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LinkedBankAccounts_ConnectionId",
                table: "LinkedBankAccounts",
                column: "ConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_LinkedBankAccounts_ConnectionId_ProviderAccountId",
                table: "LinkedBankAccounts",
                columns: new[] { "ConnectionId", "ProviderAccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LinkedBankAccounts_FinancialAccountId",
                table: "LinkedBankAccounts",
                column: "FinancialAccountId",
                unique: true,
                filter: "\"FinancialAccountId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OpenBankingConnections_AuthStateNonce",
                table: "OpenBankingConnections",
                column: "AuthStateNonce",
                unique: true,
                filter: "\"AuthStateNonce\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OpenBankingConnections_UserId",
                table: "OpenBankingConnections",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_OpenBankingConnections_UserId_ProviderName_ProviderEnvironm~",
                table: "OpenBankingConnections",
                columns: new[] { "UserId", "ProviderName", "ProviderEnvironment" });

            migrationBuilder.CreateIndex(
                name: "IX_RawBankTransactions_LinkedBankAccountId",
                table: "RawBankTransactions",
                column: "LinkedBankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_RawBankTransactions_LinkedBankAccountId_DedupeKey",
                table: "RawBankTransactions",
                columns: new[] { "LinkedBankAccountId", "DedupeKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RawBankTransactions_LinkedBankAccountId_ProviderTransaction~",
                table: "RawBankTransactions",
                columns: new[] { "LinkedBankAccountId", "ProviderTransactionId" },
                unique: true,
                filter: "\"ProviderTransactionId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BankBalanceSnapshots");

            migrationBuilder.DropTable(
                name: "BankConnectionTokens");

            migrationBuilder.DropTable(
                name: "RawBankTransactions");

            migrationBuilder.DropTable(
                name: "LinkedBankAccounts");

            migrationBuilder.DropTable(
                name: "OpenBankingConnections");
        }
    }
}
