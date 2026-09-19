using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymAppApi.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPackageFreezeLimit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "PreferredLanguage",
                table: "Users",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "en",
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10,
                oldDefaultValue: "tr");

            migrationBuilder.AddColumn<int>(
                name: "MaxFreezeDays",
                table: "Packages",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TotalFrozenDays",
                table: "PackageAssignments",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxFreezeDays",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "TotalFrozenDays",
                table: "PackageAssignments");

            migrationBuilder.AlterColumn<string>(
                name: "PreferredLanguage",
                table: "Users",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "tr",
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10,
                oldDefaultValue: "en");
        }
    }
}
