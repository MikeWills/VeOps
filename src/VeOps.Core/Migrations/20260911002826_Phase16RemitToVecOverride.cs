using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeOps.Core.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Renames the session fee override to say what it now means: the amount owed to the VEC, not
    /// the amount the team keeps (#544).
    ///
    /// <para>⚠️ <b>The value is carried across unchanged, and that is a decision, not an oversight.</b>
    /// The number's meaning is reversed by this change, so in general a stored figure would need
    /// converting. It is not converted here because exactly one session in production had the
    /// override set, and the figure its operator typed was the amount owed to the VEC all along --
    /// they were reaching for the new meaning and the old field could not express it. Converting
    /// would have corrupted the one row it touched.</para>
    ///
    /// <para>Anyone applying this to a deployment with more than a handful of overridden sessions
    /// should check those rows by hand first. There is no safe automatic conversion: retained and
    /// remit are only interchangeable when what was collected is known and correct, which is
    /// precisely the condition that fails on the sessions people override.</para>
    /// </summary>
    public partial class Phase16RemitToVecOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sessions_AspNetUsers_RetainedAmountOverrideByUserId",
                table: "Sessions");

            migrationBuilder.RenameColumn(
                name: "RetainedAmountOverrideUtc",
                table: "Sessions",
                newName: "RemitToVecOverrideUtc");

            migrationBuilder.RenameColumn(
                name: "RetainedAmountOverrideByUserId",
                table: "Sessions",
                newName: "RemitToVecOverrideByUserId");

            migrationBuilder.RenameColumn(
                name: "RetainedAmountOverride",
                table: "Sessions",
                newName: "RemitToVecOverride");

            migrationBuilder.RenameIndex(
                name: "IX_Sessions_RetainedAmountOverrideByUserId",
                table: "Sessions",
                newName: "IX_Sessions_RemitToVecOverrideByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Sessions_AspNetUsers_RemitToVecOverrideByUserId",
                table: "Sessions",
                column: "RemitToVecOverrideByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sessions_AspNetUsers_RemitToVecOverrideByUserId",
                table: "Sessions");

            migrationBuilder.RenameColumn(
                name: "RemitToVecOverrideUtc",
                table: "Sessions",
                newName: "RetainedAmountOverrideUtc");

            migrationBuilder.RenameColumn(
                name: "RemitToVecOverrideByUserId",
                table: "Sessions",
                newName: "RetainedAmountOverrideByUserId");

            migrationBuilder.RenameColumn(
                name: "RemitToVecOverride",
                table: "Sessions",
                newName: "RetainedAmountOverride");

            migrationBuilder.RenameIndex(
                name: "IX_Sessions_RemitToVecOverrideByUserId",
                table: "Sessions",
                newName: "IX_Sessions_RetainedAmountOverrideByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Sessions_AspNetUsers_RetainedAmountOverrideByUserId",
                table: "Sessions",
                column: "RetainedAmountOverrideByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");
        }
    }
}
