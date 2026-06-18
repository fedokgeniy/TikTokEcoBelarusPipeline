using System;
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
                name: "AppSettings",
                table: "AppSettings",
                type: "character varying(100)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "TrackedChannels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: true),
                    UniqueId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AvatarUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ProfileUrl = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    LastVideoCount = table.Column<int>(type: "integer", nullable: true),
                    LastCommentCount = table.Column<int>(type: "integer", nullable: true),
                    LastCheckedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackedChannels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrackedChannelVideos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TrackedChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    VideoId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CommentCount = table.Column<long>(type: "bigint", nullable: false),
                    LikeCount = table.Column<long>(type: "bigint", nullable: false),
                    PlayCount = table.Column<long>(type: "bigint", nullable: false),
                    ShareCount = table.Column<long>(type: "bigint", nullable: false),
                    VideoCreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FetchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackedChannelVideos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrackedChannelVideos_TrackedChannels_TrackedChannelId",
                        column: x => x.TrackedChannelId,
                        principalTable: "TrackedChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrackedChannels_UniqueId",
                table: "TrackedChannels",
                column: "UniqueId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrackedChannelVideos_TrackedChannelId_VideoId",
                table: "TrackedChannelVideos",
                columns: new[] { "TrackedChannelId", "VideoId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AppSettings");
            migrationBuilder.DropTable(name: "TrackedChannelVideos");
            migrationBuilder.DropTable(name: "TrackedChannels");
        }
    }
}
