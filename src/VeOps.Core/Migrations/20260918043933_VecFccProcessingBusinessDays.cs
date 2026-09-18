using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeOps.Core.Migrations
{
    /// <inheritdoc />
    public partial class VecFccProcessingBusinessDays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FccProcessingBusinessDays",
                table: "Vecs",
                type: "INTEGER",
                nullable: false,
                // Existing VECs (ARRL on every deployment) get the same 3 the entity defaults to —
                // 0 would render {{FccNoticeExpectedBy}} as the session date itself.
                defaultValue: 3);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FccProcessingBusinessDays",
                table: "Vecs");
        }
    }
}
