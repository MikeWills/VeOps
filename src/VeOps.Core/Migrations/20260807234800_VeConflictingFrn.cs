using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeOps.Core.Migrations
{
    /// <inheritdoc />
    public partial class VeConflictingFrn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConflictingFrn",
                table: "VolunteerExaminers",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConflictingFrn",
                table: "VolunteerExaminers");
        }
    }
}
