using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateKbArticleVersionSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_kb_article_versions_article_id_version_number",
                table: "kb_article_versions");

            migrationBuilder.DropIndex(
                name: "IX_kb_article_versions_version_number",
                table: "kb_article_versions");

            migrationBuilder.RenameColumn(
                name: "version_number",
                table: "kb_article_versions",
                newName: "status");

            migrationBuilder.AddColumn<int>(
                name: "major_version",
                table: "kb_article_versions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "manager_reject_reason",
                table: "kb_article_versions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "minor_version",
                table: "kb_article_versions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_kb_article_versions_article_id_major_version_minor_version",
                table: "kb_article_versions",
                columns: new[] { "article_id", "major_version", "minor_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_kb_article_versions_major_version",
                table: "kb_article_versions",
                column: "major_version");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_kb_article_versions_article_id_major_version_minor_version",
                table: "kb_article_versions");

            migrationBuilder.DropIndex(
                name: "IX_kb_article_versions_major_version",
                table: "kb_article_versions");

            migrationBuilder.DropColumn(
                name: "major_version",
                table: "kb_article_versions");

            migrationBuilder.DropColumn(
                name: "manager_reject_reason",
                table: "kb_article_versions");

            migrationBuilder.DropColumn(
                name: "minor_version",
                table: "kb_article_versions");

            migrationBuilder.RenameColumn(
                name: "status",
                table: "kb_article_versions",
                newName: "version_number");

            migrationBuilder.CreateIndex(
                name: "IX_kb_article_versions_article_id_version_number",
                table: "kb_article_versions",
                columns: new[] { "article_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_kb_article_versions_version_number",
                table: "kb_article_versions",
                column: "version_number");
        }
    }
}
