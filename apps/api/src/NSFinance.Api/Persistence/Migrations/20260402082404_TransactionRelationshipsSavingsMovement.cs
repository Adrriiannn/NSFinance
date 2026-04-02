using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSFinance.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TransactionRelationshipsSavingsMovement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TransactionRelationships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RelationshipKey = table.Column<string>(type: "character varying(220)", maxLength: 220, nullable: false),
                    RelationshipType = table.Column<int>(type: "integer", nullable: false),
                    RelationshipStatus = table.Column<int>(type: "integer", nullable: false),
                    RelationshipDirection = table.Column<int>(type: "integer", nullable: false),
                    SourceTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceRawBankTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetRawBankTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceFinancialAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetFinancialAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConfidenceScore = table.Column<int>(type: "integer", nullable: false),
                    ConfidenceTier = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    MatchReasonsJson = table.Column<string>(type: "jsonb", nullable: true),
                    ProviderPolicyKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AnalyticsTreatment = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    VirtualDestinationLabel = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionRelationships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransactionRelationships_FinancialAccounts_SourceFinancialA~",
                        column: x => x.SourceFinancialAccountId,
                        principalTable: "FinancialAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TransactionRelationships_FinancialAccounts_TargetFinancialA~",
                        column: x => x.TargetFinancialAccountId,
                        principalTable: "FinancialAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TransactionRelationships_RawBankTransactions_SourceRawBankT~",
                        column: x => x.SourceRawBankTransactionId,
                        principalTable: "RawBankTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TransactionRelationships_RawBankTransactions_TargetRawBankT~",
                        column: x => x.TargetRawBankTransactionId,
                        principalTable: "RawBankTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TransactionRelationships_Transactions_SourceTransactionId",
                        column: x => x.SourceTransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TransactionRelationships_Transactions_TargetTransactionId",
                        column: x => x.TargetTransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TransactionRelationships_RelationshipKey",
                table: "TransactionRelationships",
                column: "RelationshipKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransactionRelationships_RelationshipStatus",
                table: "TransactionRelationships",
                column: "RelationshipStatus");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionRelationships_RelationshipType",
                table: "TransactionRelationships",
                column: "RelationshipType");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionRelationships_SourceFinancialAccountId",
                table: "TransactionRelationships",
                column: "SourceFinancialAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionRelationships_SourceRawBankTransactionId",
                table: "TransactionRelationships",
                column: "SourceRawBankTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionRelationships_SourceTransactionId",
                table: "TransactionRelationships",
                column: "SourceTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionRelationships_TargetFinancialAccountId",
                table: "TransactionRelationships",
                column: "TargetFinancialAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionRelationships_TargetRawBankTransactionId",
                table: "TransactionRelationships",
                column: "TargetRawBankTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionRelationships_TargetTransactionId",
                table: "TransactionRelationships",
                column: "TargetTransactionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TransactionRelationships");
        }
    }
}
