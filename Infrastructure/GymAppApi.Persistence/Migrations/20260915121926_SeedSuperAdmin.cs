using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymAppApi.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedSuperAdmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Users",
                columns: new[] { "Id", "CreatedAt", "Email", "FullName", "Gender", "PasswordHash", "Phone", "PhoneVerified", "PreferredLanguage", "UpdatedAt" },
                values: new object[] { -1, new DateTime(2026, 9, 15, 0, 0, 0, 0, DateTimeKind.Utc), "admin@gymapp.local", "GymApp SuperAdmin", 0, "AQAAAAIAAYagAAAAEH3IHnPC7S7v0YMoHMqzARS4fXkwWC4uv1EQA2nXq9kmTl09mYL41BXzA2EU6OuOZg==", "+900000000000", true, "tr", null });

            migrationBuilder.InsertData(
                table: "Assignments",
                columns: new[] { "Id", "BranchId", "CompanyId", "CreatedAt", "IsActive", "Role", "UpdatedAt", "UserId" },
                values: new object[] { -1, null, null, new DateTime(2026, 9, 15, 0, 0, 0, 0, DateTimeKind.Utc), true, "SuperAdmin", null, -1 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Assignments",
                keyColumn: "Id",
                keyValue: -1);

            migrationBuilder.DeleteData(
                table: "Users",
                keyColumn: "Id",
                keyValue: -1);
        }
    }
}
