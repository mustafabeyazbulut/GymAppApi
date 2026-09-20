using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymAppApi.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProgressNoteMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MediaFileId",
                table: "ProgressNotes",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProgressNotes_MediaFileId",
                table: "ProgressNotes",
                column: "MediaFileId");

            migrationBuilder.AddForeignKey(
                name: "FK_ProgressNotes_MediaFiles_MediaFileId",
                table: "ProgressNotes",
                column: "MediaFileId",
                principalTable: "MediaFiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProgressNotes_MediaFiles_MediaFileId",
                table: "ProgressNotes");

            migrationBuilder.DropIndex(
                name: "IX_ProgressNotes_MediaFileId",
                table: "ProgressNotes");

            migrationBuilder.DropColumn(
                name: "MediaFileId",
                table: "ProgressNotes");
        }
    }
}
