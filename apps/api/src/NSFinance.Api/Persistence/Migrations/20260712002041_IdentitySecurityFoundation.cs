using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSFinance.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IdentitySecurityFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailActionTokens");

            migrationBuilder.DropColumn(
                name: "BiometricUnlockEnabled",
                table: "Users");

            migrationBuilder.AddColumn<bool>(
                name: "PhoneRecoveryEnabled",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PendingPhoneNumber",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PendingPhoneRequestedUtc",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PhoneVerifiedUtc",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "IdentityChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Purpose = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Channel = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    DestinationHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SecretHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    GrantHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    FailedAttempts = table.Column<int>(type: "integer", nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VerifiedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    GrantExpiresUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConsumedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SupersededUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RequestedByIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MetadataJson = table.Column<string>(type: "jsonb", nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentityChallenges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IdentityChallenges_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TotpAuthenticators",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EncryptedSecret = table.Column<string>(type: "text", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EnrollmentExpiresUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VerifiedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DisabledUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastAcceptedTimeStep = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TotpAuthenticators", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TotpAuthenticators_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TransactionalMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IdentityChallengeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Channel = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    TemplateKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TemplateVersion = table.Column<int>(type: "integer", nullable: false),
                    Recipient = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    EncryptedPayload = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProviderMessageId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    NextAttemptUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LeaseExpiresUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LeaseId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ProviderAcceptedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeliveredUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastFailureCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionalMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransactionalMessages_IdentityChallenges_IdentityChallengeId",
                        column: x => x.IdentityChallengeId,
                        principalTable: "IdentityChallenges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TransactionalMessages_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "MfaRecoveryCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TotpAuthenticatorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfaRecoveryCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfaRecoveryCodes_TotpAuthenticators_TotpAuthenticatorId",
                        column: x => x.TotpAuthenticatorId,
                        principalTable: "TotpAuthenticators",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IdentityChallenges_DestinationHash_Purpose_CreatedUtc",
                table: "IdentityChallenges",
                columns: new[] { "DestinationHash", "Purpose", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_IdentityChallenges_GrantHash",
                table: "IdentityChallenges",
                column: "GrantHash");

            migrationBuilder.CreateIndex(
                name: "IX_IdentityChallenges_UserId_Purpose_CreatedUtc",
                table: "IdentityChallenges",
                columns: new[] { "UserId", "Purpose", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MfaRecoveryCodes_CodeHash",
                table: "MfaRecoveryCodes",
                column: "CodeHash");

            migrationBuilder.CreateIndex(
                name: "IX_MfaRecoveryCodes_TotpAuthenticatorId_UsedUtc",
                table: "MfaRecoveryCodes",
                columns: new[] { "TotpAuthenticatorId", "UsedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TotpAuthenticators_UserId_DisabledUtc",
                table: "TotpAuthenticators",
                columns: new[] { "UserId", "DisabledUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TransactionalMessages_IdentityChallengeId",
                table: "TransactionalMessages",
                column: "IdentityChallengeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransactionalMessages_ProviderMessageId",
                table: "TransactionalMessages",
                column: "ProviderMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionalMessages_Status_NextAttemptUtc",
                table: "TransactionalMessages",
                columns: new[] { "Status", "NextAttemptUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TransactionalMessages_UserId",
                table: "TransactionalMessages",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MfaRecoveryCodes");

            migrationBuilder.DropTable(
                name: "TransactionalMessages");

            migrationBuilder.DropTable(
                name: "TotpAuthenticators");

            migrationBuilder.DropTable(
                name: "IdentityChallenges");

            migrationBuilder.DropColumn(
                name: "PendingPhoneNumber",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PendingPhoneRequestedUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PhoneVerifiedUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PhoneRecoveryEnabled",
                table: "Users");

            migrationBuilder.AddColumn<bool>(
                name: "BiometricUnlockEnabled",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "EmailActionTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    ExpiresUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MetadataJson = table.Column<string>(type: "jsonb", nullable: true),
                    Purpose = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RequestedByIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    UsedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailActionTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailActionTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmailActionTokens_TokenHash",
                table: "EmailActionTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailActionTokens_UserId_Purpose",
                table: "EmailActionTokens",
                columns: new[] { "UserId", "Purpose" });
        }
    }
}
