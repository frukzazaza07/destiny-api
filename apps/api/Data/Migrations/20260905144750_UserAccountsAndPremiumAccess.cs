using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TarotDestiny.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class UserAccountsAndPremiumAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "account_role",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_role", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "account_user",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    email_verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    disabled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    security_stamp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revision = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_user", x => x.id);
                    table.CheckConstraint("ck_account_user_failed_count", "access_failed_count >= 0");
                    table.CheckConstraint("ck_account_user_revision", "revision >= 0");
                });

            migrationBuilder.CreateTable(
                name: "user_entitlement_audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entitlement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_entitlement_audit", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "account_session",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    security_stamp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_session", x => x.id);
                    table.CheckConstraint("ck_account_session_expiry", "expires_at > created_at");
                    table.ForeignKey(
                        name: "FK_account_session_account_user_user_id",
                        column: x => x.user_id,
                        principalTable: "account_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "account_token",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_token", x => x.id);
                    table.CheckConstraint("ck_account_token_expiry", "expires_at > created_at");
                    table.ForeignKey(
                        name: "FK_account_token_account_user_user_id",
                        column: x => x.user_id,
                        principalTable: "account_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "account_user_role",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_user_role", x => new { x.user_id, x.role_id });
                    table.ForeignKey(
                        name: "FK_account_user_role_account_role_role_id",
                        column: x => x.role_id,
                        principalTable: "account_role",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_account_user_role_account_user_user_id",
                        column: x => x.user_id,
                        principalTable: "account_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_entitlement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    granted_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_entitlement", x => x.id);
                    table.CheckConstraint("ck_user_entitlement_expiry", "expires_at > starts_at");
                    table.CheckConstraint("ck_user_entitlement_revision", "revision >= 0");
                    table.ForeignKey(
                        name: "FK_user_entitlement_account_user_granted_by_user_id",
                        column: x => x.granted_by_user_id,
                        principalTable: "account_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_entitlement_account_user_user_id",
                        column: x => x.user_id,
                        principalTable: "account_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "account_role",
                columns: new[] { "id", "name" },
                values: new object[,]
                {
                    { new Guid("0d80c432-36ca-4b4c-90a5-3a824e5c73c1"), "USER" },
                    { new Guid("854ced1e-2943-42fc-9818-01a47952c723"), "ADMIN" }
                });

            migrationBuilder.CreateIndex(
                name: "ux_account_role_name",
                table: "account_role",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_account_session_user_expiry",
                table: "account_session",
                columns: new[] { "user_id", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_account_token_user_purpose",
                table: "account_token",
                columns: new[] { "user_id", "purpose" });

            migrationBuilder.CreateIndex(
                name: "ux_account_token_hash",
                table: "account_token",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_account_user_normalized_email",
                table: "account_user",
                column: "normalized_email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_account_user_role_role_id",
                table: "account_user_role",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_entitlement_granted_by_user_id",
                table: "user_entitlement",
                column: "granted_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ux_user_entitlement_user_type",
                table: "user_entitlement",
                columns: new[] { "user_id", "type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_entitlement_audit_user_created",
                table: "user_entitlement_audit",
                columns: new[] { "user_id", "created_at" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_session");

            migrationBuilder.DropTable(
                name: "account_token");

            migrationBuilder.DropTable(
                name: "account_user_role");

            migrationBuilder.DropTable(
                name: "user_entitlement");

            migrationBuilder.DropTable(
                name: "user_entitlement_audit");

            migrationBuilder.DropTable(
                name: "account_role");

            migrationBuilder.DropTable(
                name: "account_user");
        }
    }
}
