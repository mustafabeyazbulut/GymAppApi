using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GymAppApi.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProgressNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProgressNotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PackageAssignmentId = table.Column<int>(type: "integer", nullable: false),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    BranchId = table.Column<int>(type: "integer", nullable: true),
                    RecordedByUserId = table.Column<int>(type: "integer", nullable: false),
                    TechniqueScore = table.Column<int>(type: "integer", nullable: false),
                    ConditionScore = table.Column<int>(type: "integer", nullable: false),
                    NoteText = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProgressNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProgressNotes_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProgressNotes_PackageAssignments_PackageAssignmentId",
                        column: x => x.PackageAssignmentId,
                        principalTable: "PackageAssignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProgressNotes_CompanyId",
                table: "ProgressNotes",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_ProgressNotes_PackageAssignmentId",
                table: "ProgressNotes",
                column: "PackageAssignmentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProgressNotes");
        }
    }
}
