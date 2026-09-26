using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymAppApi.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvitationConcurrencyTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "PendingPackageAssignmentInvitations",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "PendingAssignmentInvitations",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "xmin",
                table: "PendingPackageAssignmentInvitations");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "PendingAssignmentInvitations");
        }
    }
}
