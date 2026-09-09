using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TarotDestiny.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PromptCopyUnlocks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "prompt_readings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Owner = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProtectedSnapshot = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Completed = table.Column<bool>(type: "boolean", nullable: false),
                    RequiredAds = table.Column<int>(type: "integer", nullable: true),
                    CompletedAds = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prompt_readings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "prompt_ad_attempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReadingId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProviderEventId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Closed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prompt_ad_attempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_prompt_ad_attempts_prompt_readings_ReadingId",
                        column: x => x.ReadingId,
                        principalTable: "prompt_readings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_prompt_ad_attempts_ProviderEventId",
                table: "prompt_ad_attempts",
                column: "ProviderEventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_prompt_ad_attempts_ReadingId",
                table: "prompt_ad_attempts",
                column: "ReadingId");

            migrationBuilder.CreateIndex(
                name: "IX_prompt_readings_Owner",
                table: "prompt_readings",
                column: "Owner");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "prompt_ad_attempts");

            migrationBuilder.DropTable(
                name: "prompt_readings");
        }
    }
}
