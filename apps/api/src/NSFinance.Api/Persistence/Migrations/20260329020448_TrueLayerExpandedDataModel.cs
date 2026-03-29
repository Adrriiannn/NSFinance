using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSFinance.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TrueLayerExpandedDataModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GrantedScopesCsv",
                table: "OpenBankingConnections",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SupportsCards",
                table: "OpenBankingConnections",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SupportsDirectDebits",
                table: "OpenBankingConnections",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SupportsInfo",
                table: "OpenBankingConnections",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SupportsStandingOrders",
                table: "OpenBankingConnections",
                type: "boolean",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BankConnectionIdentityInfos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: true),
                    Email = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: true),
                    Phone = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    DateOfBirth = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    RawPayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    FetchedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankConnectionIdentityInfos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankConnectionIdentityInfos_OpenBankingConnections_Connecti~",
                        column: x => x.ConnectionId,
                        principalTable: "OpenBankingConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BankDirectDebits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LinkedBankAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderDirectDebitId = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    Status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    MandateType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Reference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    MerchantName = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: true),
                    PreviousPaymentDateUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PreviousPaymentAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    PreviousPaymentCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    NextPaymentDateUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextPaymentAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    NextPaymentCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    RawPayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankDirectDebits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankDirectDebits_LinkedBankAccounts_LinkedBankAccountId",
                        column: x => x.LinkedBankAccountId,
                        principalTable: "LinkedBankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BankStandingOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LinkedBankAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderStandingOrderId = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    Status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Frequency = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Reference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    PayeeName = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: true),
                    FirstPaymentDateUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextPaymentDateUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinalPaymentDateUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextPaymentAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    NextPaymentCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    PayeeAccountMetadataJson = table.Column<string>(type: "jsonb", nullable: true),
                    RawPayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankStandingOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankStandingOrders_LinkedBankAccounts_LinkedBankAccountId",
                        column: x => x.LinkedBankAccountId,
                        principalTable: "LinkedBankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LinkedBankCards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderCardId = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    ProviderAccountId = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CardType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    CardNetwork = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    CardNumberLastFour = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    NameOnCard = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: true),
                    ValidFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ValidToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CurrentConnectionHealth = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RawPayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LinkedBankCards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LinkedBankCards_OpenBankingConnections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "OpenBankingConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BankCardBalanceSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LinkedBankCardId = table.Column<Guid>(type: "uuid", nullable: false),
                    Available = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    Current = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    Limit = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    Outstanding = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CapturedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RawPayloadJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankCardBalanceSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankCardBalanceSnapshots_LinkedBankCards_LinkedBankCardId",
                        column: x => x.LinkedBankCardId,
                        principalTable: "LinkedBankCards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RawBankCardTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LinkedBankCardId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_RawBankCardTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RawBankCardTransactions_LinkedBankCards_LinkedBankCardId",
                        column: x => x.LinkedBankCardId,
                        principalTable: "LinkedBankCards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankCardBalanceSnapshots_LinkedBankCardId",
                table: "BankCardBalanceSnapshots",
                column: "LinkedBankCardId");

            migrationBuilder.CreateIndex(
                name: "IX_BankCardBalanceSnapshots_LinkedBankCardId_CapturedUtc",
                table: "BankCardBalanceSnapshots",
                columns: new[] { "LinkedBankCardId", "CapturedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BankConnectionIdentityInfos_ConnectionId",
                table: "BankConnectionIdentityInfos",
                column: "ConnectionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankDirectDebits_LinkedBankAccountId",
                table: "BankDirectDebits",
                column: "LinkedBankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_BankDirectDebits_LinkedBankAccountId_ProviderDirectDebitId",
                table: "BankDirectDebits",
                columns: new[] { "LinkedBankAccountId", "ProviderDirectDebitId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankStandingOrders_LinkedBankAccountId",
                table: "BankStandingOrders",
                column: "LinkedBankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_BankStandingOrders_LinkedBankAccountId_ProviderStandingOrde~",
                table: "BankStandingOrders",
                columns: new[] { "LinkedBankAccountId", "ProviderStandingOrderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LinkedBankCards_ConnectionId",
                table: "LinkedBankCards",
                column: "ConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_LinkedBankCards_ConnectionId_ProviderCardId",
                table: "LinkedBankCards",
                columns: new[] { "ConnectionId", "ProviderCardId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RawBankCardTransactions_LinkedBankCardId",
                table: "RawBankCardTransactions",
                column: "LinkedBankCardId");

            migrationBuilder.CreateIndex(
                name: "IX_RawBankCardTransactions_LinkedBankCardId_DedupeKey",
                table: "RawBankCardTransactions",
                columns: new[] { "LinkedBankCardId", "DedupeKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RawBankCardTransactions_LinkedBankCardId_ProviderTransactio~",
                table: "RawBankCardTransactions",
                columns: new[] { "LinkedBankCardId", "ProviderTransactionId" },
                unique: true,
                filter: "\"ProviderTransactionId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BankCardBalanceSnapshots");

            migrationBuilder.DropTable(
                name: "BankConnectionIdentityInfos");

            migrationBuilder.DropTable(
                name: "BankDirectDebits");

            migrationBuilder.DropTable(
                name: "BankStandingOrders");

            migrationBuilder.DropTable(
                name: "RawBankCardTransactions");

            migrationBuilder.DropTable(
                name: "LinkedBankCards");

            migrationBuilder.DropColumn(
                name: "GrantedScopesCsv",
                table: "OpenBankingConnections");

            migrationBuilder.DropColumn(
                name: "SupportsCards",
                table: "OpenBankingConnections");

            migrationBuilder.DropColumn(
                name: "SupportsDirectDebits",
                table: "OpenBankingConnections");

            migrationBuilder.DropColumn(
                name: "SupportsInfo",
                table: "OpenBankingConnections");

            migrationBuilder.DropColumn(
                name: "SupportsStandingOrders",
                table: "OpenBankingConnections");
        }
    }
}
