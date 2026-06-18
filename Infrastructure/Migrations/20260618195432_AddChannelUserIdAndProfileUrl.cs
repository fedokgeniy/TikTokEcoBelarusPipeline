using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TikTokEcoBelarusPipeline.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelUserIdAndProfileUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UserId",
                table: "TrackedChannels",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfileUrl",
                table: "TrackedChannels",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UserId",
                table: "TrackedChannels");

            migrationBuilder.DropColumn(
                name: "ProfileUrl",
                table: "TrackedChannels");
        }
    }
}
