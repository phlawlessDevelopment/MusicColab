using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusicColab.Api.Migrations
{
    /// <inheritdoc />
    public partial class SyncPreferenceSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserArtistPreferences_Artists_ArtistId",
                table: "UserArtistPreferences");

            migrationBuilder.DropTable(
                name: "Artists");

            migrationBuilder.DropIndex(
                name: "IX_UserArtistPreferences_ArtistId",
                table: "UserArtistPreferences");

            migrationBuilder.AlterColumn<string>(
                name: "ArtistId",
                table: "UserArtistPreferences",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AddColumn<string>(
                name: "ArtistName",
                table: "UserArtistPreferences",
                type: "TEXT",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "UserArtistPreferences",
                type: "TEXT",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviewUrl",
                table: "UserArtistPreferences",
                type: "TEXT",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TagsJson",
                table: "UserArtistPreferences",
                type: "TEXT",
                maxLength: 4000,
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArtistName",
                table: "UserArtistPreferences");

            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "UserArtistPreferences");

            migrationBuilder.DropColumn(
                name: "PreviewUrl",
                table: "UserArtistPreferences");

            migrationBuilder.DropColumn(
                name: "TagsJson",
                table: "UserArtistPreferences");

            migrationBuilder.AlterColumn<string>(
                name: "ArtistId",
                table: "UserArtistPreferences",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 64);

            migrationBuilder.CreateTable(
                name: "Artists",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Artists", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserArtistPreferences_ArtistId",
                table: "UserArtistPreferences",
                column: "ArtistId");

            migrationBuilder.AddForeignKey(
                name: "FK_UserArtistPreferences_Artists_ArtistId",
                table: "UserArtistPreferences",
                column: "ArtistId",
                principalTable: "Artists",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
