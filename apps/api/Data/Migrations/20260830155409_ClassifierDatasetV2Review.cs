using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TarotDestiny.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ClassifierDatasetV2Review : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "paraphrase_group",
                table: "tarot_question_classification_training",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reviewed_personalization",
                table: "tarot_question_classification_training",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "reviewer_time_seconds",
                table: "tarot_question_classification_training",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_tarot_training_reviewed_personalization",
                table: "tarot_question_classification_training",
                sql: "reviewed_personalization IS NULL OR reviewed_personalization IN ('LOW', 'MEDIUM', 'HIGH')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tarot_training_reviewer_time",
                table: "tarot_question_classification_training",
                sql: "reviewer_time_seconds IS NULL OR reviewer_time_seconds >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_tarot_training_reviewed_personalization",
                table: "tarot_question_classification_training");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tarot_training_reviewer_time",
                table: "tarot_question_classification_training");

            migrationBuilder.DropColumn(
                name: "paraphrase_group",
                table: "tarot_question_classification_training");

            migrationBuilder.DropColumn(
                name: "reviewed_personalization",
                table: "tarot_question_classification_training");

            migrationBuilder.DropColumn(
                name: "reviewer_time_seconds",
                table: "tarot_question_classification_training");
        }
    }
}
