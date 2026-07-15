using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TikTokEcoBelarusPipeline.Migrations
{
    /// <inheritdoc />
    public partial class AddClassifierScoreFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "VideoComments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Score",
                table: "VideoComments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShouldReply",
                table: "VideoComments",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Category",
                table: "VideoComments");

            migrationBuilder.DropColumn(
                name: "Score",
                table: "VideoComments");

            migrationBuilder.DropColumn(
                name: "ShouldReply",
                table: "VideoComments");
        }
    }
}
