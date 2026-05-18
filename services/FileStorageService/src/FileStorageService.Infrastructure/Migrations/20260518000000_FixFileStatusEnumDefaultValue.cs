using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FileStorageService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixFileStatusEnumDefaultValue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Migrate existing data: old values (0–4) shift up by 1 to match new enum (1–5).
            // Order: descending to avoid value collision during update.
            migrationBuilder.Sql(
                "UPDATE uploaded_files SET status = status + 1 WHERE status BETWEEN 0 AND 4;");

            migrationBuilder.AlterColumn<int>(
                name: "status",
                table: "uploaded_files",
                type: "integer",
                nullable: false,
                defaultValue: 1,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "status",
                table: "uploaded_files",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 1);

            // Revert data migration.
            migrationBuilder.Sql(
                "UPDATE uploaded_files SET status = status - 1 WHERE status BETWEEN 1 AND 5;");
        }
    }
}
