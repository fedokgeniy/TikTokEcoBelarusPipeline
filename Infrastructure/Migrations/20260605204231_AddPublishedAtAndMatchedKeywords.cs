using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TikTokEcoBelarusPipeline.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPublishedAtAndMatchedKeywords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "MatchedKeywords",
                table: "Videos",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<DateTime>(
                name: "PublishedAt",
                table: "Videos",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MatchedKeywords",
                table: "Videos");

            migrationBuilder.DropColumn(
                name: "PublishedAt",
                table: "Videos");
        }
    }
}
