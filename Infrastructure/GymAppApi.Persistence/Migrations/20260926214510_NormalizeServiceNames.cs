using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymAppApi.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeServiceNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Services_BranchId_Name",
                table: "Services");

            migrationBuilder.AddColumn<string>(
                name: "NameNormalized",
                table: "Services",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "");

            // Mevcut satırlar: normalize edilmiş adı doldur (tekil indeks öncesi).
            migrationBuilder.Sql("UPDATE \"Services\" SET \"NameNormalized\" = lower(btrim(\"Name\"));");

            migrationBuilder.CreateIndex(
                name: "IX_Services_BranchId_NameNormalized",
                table: "Services",
                columns: new[] { "BranchId", "NameNormalized" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Services_BranchId_NameNormalized",
                table: "Services");

            migrationBuilder.DropColumn(
                name: "NameNormalized",
                table: "Services");

            migrationBuilder.CreateIndex(
                name: "IX_Services_BranchId_Name",
                table: "Services",
                columns: new[] { "BranchId", "Name" },
                unique: true);
        }
    }
}
