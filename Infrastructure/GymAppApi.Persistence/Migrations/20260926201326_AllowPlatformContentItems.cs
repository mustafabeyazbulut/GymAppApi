using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymAppApi.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowPlatformContentItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "CompanyId",
                table: "ContentItems",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Genel (platform) içeriklerin firması yok; NOT NULL + FK'ya geri
            // dönülebilmesi için önce silinirler (medya kayıtları kalır).
            migrationBuilder.Sql("DELETE FROM \"ContentItems\" WHERE \"CompanyId\" IS NULL;");

            migrationBuilder.AlterColumn<int>(
                name: "CompanyId",
                table: "ContentItems",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
