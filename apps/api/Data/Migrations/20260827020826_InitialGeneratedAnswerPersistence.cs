using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TarotDestiny.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialGeneratedAnswerPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tarot_generated_answer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cache_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    cache_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    domain = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    intent = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    reading_mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    spread_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    locale = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    cards = table.Column<string>(type: "jsonb", nullable: false),
                    prompt_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    interpretation_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    model_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    response = table.Column<string>(type: "jsonb", nullable: false),
                    hit_count = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tarot_generated_answer", x => x.id);
                    table.CheckConstraint("ck_tarot_generated_answer_cache_hash", "length(cache_hash) = 64");
                    table.CheckConstraint("ck_tarot_generated_answer_hit_count", "hit_count >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "ux_tarot_generated_answer_cache_hash",
                table: "tarot_generated_answer",
                column: "cache_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tarot_generated_answer");
        }
    }
}
