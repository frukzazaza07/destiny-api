using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TarotDestiny.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ThaiAstrologyJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PromptJson",
                table: "reading_jobs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReadingType",
                table: "reading_jobs",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "TAROT");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PromptJson",
                table: "reading_jobs");

            migrationBuilder.DropColumn(
                name: "ReadingType",
                table: "reading_jobs");
        }
    }
}
