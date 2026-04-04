using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSFinance.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeterministicClassificationEngineHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DeterministicClassificationCategoryId",
                table: "Transactions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeterministicClassificationEvaluatedUtc",
                table: "Transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeterministicClassificationRuleKey",
                table: "Transactions",
                type: "character varying(96)",
                maxLength: 96,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeterministicClassificationStatus",
                table: "Transactions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DeterministicClassificationSubcategoryId",
                table: "Transactions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DeterministicClassificationTerminal",
                table: "Transactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "DeterministicClassificationVersion",
                table: "Transactions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DeterministicDeferredRetryEligible",
                table: "Transactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeterministicLastRetryConsideredUtc",
                table: "Transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeterministicLinkedTransactionId",
                table: "Transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeterministicMatchScore",
                table: "Transactions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeterministicReasonCode",
                table: "Transactions",
                type: "character varying(96)",
                maxLength: 96,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeterministicReasonDetailJson",
                table: "Transactions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeterministicRelationshipGroupId",
                table: "Transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeterministicRelationshipType",
                table: "Transactions",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeterministicSourceSignature",
                table: "Transactions",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NeedsDeterministicReclassification",
                table: "Transactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "DeterministicRelationshipType",
                table: "TransactionRelationships",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PairedUtc",
                table: "TransactionRelationships",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PairingEvidenceJson",
                table: "TransactionRelationships",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PairingRuleKey",
                table: "TransactionRelationships",
                type: "character varying(96)",
                maxLength: 96,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PairingStatus",
                table: "TransactionRelationships",
                type: "character varying(48)",
                maxLength: 48,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RelationshipGroupId",
                table: "TransactionRelationships",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceConnectionId",
                table: "TransactionRelationships",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TargetConnectionId",
                table: "TransactionRelationships",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_DeterministicClassificationStatus",
                table: "Transactions",
                column: "DeterministicClassificationStatus");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_DeterministicClassificationTerminal",
                table: "Transactions",
                column: "DeterministicClassificationTerminal");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_DeterministicClassificationVersion",
                table: "Transactions",
                column: "DeterministicClassificationVersion");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_DeterministicClassificationVersion_Determinist~",
                table: "Transactions",
                columns: new[] { "DeterministicClassificationVersion", "DeterministicClassificationTerminal" });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_DeterministicDeferredRetryEligible",
                table: "Transactions",
                column: "DeterministicDeferredRetryEligible");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_DeterministicLinkedTransactionId",
                table: "Transactions",
                column: "DeterministicLinkedTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_DeterministicRelationshipGroupId",
                table: "Transactions",
                column: "DeterministicRelationshipGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_NeedsDeterministicReclassification",
                table: "Transactions",
                column: "NeedsDeterministicReclassification");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionRelationships_DeterministicRelationshipType",
                table: "TransactionRelationships",
                column: "DeterministicRelationshipType");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionRelationships_PairingStatus",
                table: "TransactionRelationships",
                column: "PairingStatus");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionRelationships_RelationshipGroupId",
                table: "TransactionRelationships",
                column: "RelationshipGroupId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_DeterministicClassificationStatus",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_DeterministicClassificationTerminal",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_DeterministicClassificationVersion",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_DeterministicClassificationVersion_Determinist~",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_DeterministicDeferredRetryEligible",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_DeterministicLinkedTransactionId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_DeterministicRelationshipGroupId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_NeedsDeterministicReclassification",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_TransactionRelationships_DeterministicRelationshipType",
                table: "TransactionRelationships");

            migrationBuilder.DropIndex(
                name: "IX_TransactionRelationships_PairingStatus",
                table: "TransactionRelationships");

            migrationBuilder.DropIndex(
                name: "IX_TransactionRelationships_RelationshipGroupId",
                table: "TransactionRelationships");

            migrationBuilder.DropColumn(
                name: "DeterministicClassificationCategoryId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "DeterministicClassificationEvaluatedUtc",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "DeterministicClassificationRuleKey",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "DeterministicClassificationStatus",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "DeterministicClassificationSubcategoryId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "DeterministicClassificationTerminal",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "DeterministicClassificationVersion",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "DeterministicDeferredRetryEligible",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "DeterministicLastRetryConsideredUtc",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "DeterministicLinkedTransactionId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "DeterministicMatchScore",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "DeterministicReasonCode",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "DeterministicReasonDetailJson",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "DeterministicRelationshipGroupId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "DeterministicRelationshipType",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "DeterministicSourceSignature",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "NeedsDeterministicReclassification",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "DeterministicRelationshipType",
                table: "TransactionRelationships");

            migrationBuilder.DropColumn(
                name: "PairedUtc",
                table: "TransactionRelationships");

            migrationBuilder.DropColumn(
                name: "PairingEvidenceJson",
                table: "TransactionRelationships");

            migrationBuilder.DropColumn(
                name: "PairingRuleKey",
                table: "TransactionRelationships");

            migrationBuilder.DropColumn(
                name: "PairingStatus",
                table: "TransactionRelationships");

            migrationBuilder.DropColumn(
                name: "RelationshipGroupId",
                table: "TransactionRelationships");

            migrationBuilder.DropColumn(
                name: "SourceConnectionId",
                table: "TransactionRelationships");

            migrationBuilder.DropColumn(
                name: "TargetConnectionId",
                table: "TransactionRelationships");
        }
    }
}
