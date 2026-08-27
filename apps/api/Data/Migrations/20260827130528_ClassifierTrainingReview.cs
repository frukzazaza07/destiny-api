using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TarotDestiny.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ClassifierTrainingReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tarot_question_classification_training",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    question = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    question_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    locale = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    predicted_domain = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    predicted_intent = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    predicted_confidence = table.Column<double>(type: "double precision", nullable: false),
                    predicted_personalization = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    classifier_source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    classifier_model_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    review_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reviewed_domain = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    reviewed_intent = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    consent_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tarot_question_classification_training", x => x.id);
                    table.CheckConstraint("ck_tarot_training_confidence", "predicted_confidence >= 0 AND predicted_confidence <= 1");
                    table.CheckConstraint("ck_tarot_training_question_hash", "length(question_hash) = 64");
                    table.CheckConstraint("ck_tarot_training_review_status", "review_status IN ('PENDING', 'APPROVED', 'REJECTED')");
                    table.CheckConstraint("ck_tarot_training_revision", "revision >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_tarot_training_review_status_created_at",
                table: "tarot_question_classification_training",
                columns: new[] { "review_status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_tarot_training_question_hash",
                table: "tarot_question_classification_training",
                column: "question_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tarot_question_classification_training");
        }
    }
}
