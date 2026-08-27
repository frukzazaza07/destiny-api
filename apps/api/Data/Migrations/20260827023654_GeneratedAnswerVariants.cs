using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TarotDestiny.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class GeneratedAnswerVariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tarot_generated_answer_variant",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    generated_answer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_number = table.Column<int>(type: "integer", nullable: false),
                    response = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tarot_generated_answer_variant", x => x.id);
                    table.CheckConstraint("ck_tarot_generated_answer_variant_number", "variant_number > 0");
                    table.ForeignKey(
                        name: "fk_tarot_generated_answer_variant_answer",
                        column: x => x.generated_answer_id,
                        principalTable: "tarot_generated_answer",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO tarot_generated_answer_variant (
                    id,
                    generated_answer_id,
                    variant_number,
                    response,
                    created_at)
                SELECT
                    id,
                    id,
                    1,
                    response,
                    created_at
                FROM tarot_generated_answer;
                """);

            migrationBuilder.DropColumn(
                name: "response",
                table: "tarot_generated_answer");

            migrationBuilder.CreateIndex(
                name: "ix_tarot_generated_answer_domain_intent_reading_mode",
                table: "tarot_generated_answer",
                columns: new[] { "domain", "intent", "reading_mode" });

            migrationBuilder.CreateIndex(
                name: "ix_tarot_generated_answer_hit_count",
                table: "tarot_generated_answer",
                column: "hit_count",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ux_tarot_generated_answer_variant_answer_number",
                table: "tarot_generated_answer_variant",
                columns: new[] { "generated_answer_id", "variant_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_tarot_generated_answer_domain_intent_reading_mode",
                table: "tarot_generated_answer");

            migrationBuilder.DropIndex(
                name: "ix_tarot_generated_answer_hit_count",
                table: "tarot_generated_answer");

            migrationBuilder.AddColumn<string>(
                name: "response",
                table: "tarot_generated_answer",
                type: "jsonb",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE tarot_generated_answer AS answer
                SET response = variant.response
                FROM tarot_generated_answer_variant AS variant
                WHERE variant.generated_answer_id = answer.id
                  AND variant.variant_number = 1;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "response",
                table: "tarot_generated_answer",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb",
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true);

            migrationBuilder.DropTable(
                name: "tarot_generated_answer_variant");
        }
    }
}
