using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TikTokEcoBelarus.Migrations;

/// <summary>
/// Adds Score, Category, ShouldReply columns to VideoComments.
/// Run: dotnet ef migrations add AddClassifierScoreFields
/// (or apply manually via ALTER TABLE)
/// </summary>
public partial class AddClassifierScoreFields : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "Score",
            table: "VideoComments",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Category",
            table: "VideoComments",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "ShouldReply",
            table: "VideoComments",
            type: "boolean",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "Score",       table: "VideoComments");
        migrationBuilder.DropColumn(name: "Category",    table: "VideoComments");
        migrationBuilder.DropColumn(name: "ShouldReply", table: "VideoComments");
    }
}
