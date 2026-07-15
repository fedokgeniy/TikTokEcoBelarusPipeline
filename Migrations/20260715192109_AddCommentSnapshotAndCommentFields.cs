using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TikTokEcoBelarusPipeline.Migrations
{
    /// <inheritdoc />
    public partial class AddCommentSnapshotAndCommentFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsRelevant",
                table: "VideoComments",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ReplyCount",
                table: "VideoComments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "Tags",
                table: "VideoComments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "VideoCommentSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VideoId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SnapshotAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CommentCount = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoCommentSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VideoCommentSnapshots_TrackedChannelVideos_VideoId",
                        column: x => x.VideoId,
                        principalTable: "TrackedChannelVideos",
                        principalColumn: "VideoId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VideoCommentSnapshots_VideoId_SnapshotAt",
                table: "VideoCommentSnapshots",
                columns: new[] { "VideoId", "SnapshotAt" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VideoCommentSnapshots");

            migrationBuilder.DropColumn(
                name: "IsRelevant",
                table: "VideoComments");

            migrationBuilder.DropColumn(
                name: "ReplyCount",
                table: "VideoComments");

            migrationBuilder.DropColumn(
                name: "Tags",
                table: "VideoComments");
        }
    }
}
