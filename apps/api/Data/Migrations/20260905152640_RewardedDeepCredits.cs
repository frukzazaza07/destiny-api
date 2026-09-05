using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TarotDestiny.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class RewardedDeepCredits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rewarded_deep_session",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    anonymous_token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    required_ad_completions = table.Column<int>(type: "integer", nullable: false),
                    deep_credits_per_completed_bundle = table.Column<int>(type: "integer", nullable: false),
                    valid_ad_completions = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revision = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rewarded_deep_session", x => x.id);
                    table.CheckConstraint("ck_rewarded_deep_session_credits", "deep_credits_per_completed_bundle BETWEEN 1 AND 5");
                    table.CheckConstraint("ck_rewarded_deep_session_expiry", "expires_at > created_at");
                    table.CheckConstraint("ck_rewarded_deep_session_identity", "user_id IS NOT NULL OR anonymous_token_hash IS NOT NULL");
                    table.CheckConstraint("ck_rewarded_deep_session_progress", "valid_ad_completions >= 0 AND valid_ad_completions <= required_ad_completions");
                    table.CheckConstraint("ck_rewarded_deep_session_required", "required_ad_completions BETWEEN 1 AND 10");
                    table.ForeignKey(
                        name: "FK_rewarded_deep_session_account_user_user_id",
                        column: x => x.user_id,
                        principalTable: "account_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rewarded_deep_settings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    required_ad_completions = table.Column<int>(type: "integer", nullable: false),
                    deep_credits_per_completed_bundle = table.Column<int>(type: "integer", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rewarded_deep_settings", x => x.id);
                    table.CheckConstraint("ck_rewarded_deep_settings_credits", "deep_credits_per_completed_bundle BETWEEN 1 AND 5");
                    table.CheckConstraint("ck_rewarded_deep_settings_required", "required_ad_completions BETWEEN 1 AND 10");
                    table.CheckConstraint("ck_rewarded_deep_settings_revision", "revision >= 0");
                    table.CheckConstraint("ck_rewarded_deep_settings_singleton", "id = 1");
                });

            migrationBuilder.CreateTable(
                name: "rewarded_ad_attempt",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nonce_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rewarded_ad_attempt", x => x.id);
                    table.CheckConstraint("ck_rewarded_ad_attempt_expiry", "expires_at > created_at");
                    table.ForeignKey(
                        name: "FK_rewarded_ad_attempt_rewarded_deep_session_session_id",
                        column: x => x.session_id,
                        principalTable: "rewarded_deep_session",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rewarded_deep_credit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reservation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reserved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reservation_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revision = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rewarded_deep_credit", x => x.id);
                    table.CheckConstraint("ck_rewarded_deep_credit_expiry", "expires_at > issued_at");
                    table.ForeignKey(
                        name: "FK_rewarded_deep_credit_account_user_user_id",
                        column: x => x.user_id,
                        principalTable: "account_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_rewarded_deep_credit_rewarded_deep_session_session_id",
                        column: x => x.session_id,
                        principalTable: "rewarded_deep_session",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "rewarded_deep_settings",
                columns: new[] { "id", "deep_credits_per_completed_bundle", "required_ad_completions", "updated_at", "updated_by_user_id" },
                values: new object[] { 1, 1, 3, new DateTimeOffset(new DateTime(2026, 9, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null });

            migrationBuilder.CreateIndex(
                name: "ix_rewarded_ad_attempt_session_created",
                table: "rewarded_ad_attempt",
                columns: new[] { "session_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_rewarded_ad_attempt_nonce",
                table: "rewarded_ad_attempt",
                column: "nonce_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rewarded_deep_credit_session_expiry",
                table: "rewarded_deep_credit",
                columns: new[] { "session_id", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_rewarded_deep_credit_user_expiry",
                table: "rewarded_deep_credit",
                columns: new[] { "user_id", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ux_rewarded_deep_credit_reservation",
                table: "rewarded_deep_credit",
                column: "reservation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rewarded_deep_session_user_created",
                table: "rewarded_deep_session",
                columns: new[] { "user_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_rewarded_deep_session_anonymous_token",
                table: "rewarded_deep_session",
                column: "anonymous_token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rewarded_ad_attempt");

            migrationBuilder.DropTable(
                name: "rewarded_deep_credit");

            migrationBuilder.DropTable(
                name: "rewarded_deep_settings");

            migrationBuilder.DropTable(
                name: "rewarded_deep_session");
        }
    }
}
