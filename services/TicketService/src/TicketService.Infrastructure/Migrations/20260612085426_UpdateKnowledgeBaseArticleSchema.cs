using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateKnowledgeBaseArticleSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "manager_reject_reason",
                table: "knowledge_base_articles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "pending_review_by",
                table: "knowledge_base_articles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "review_required",
                table: "knowledge_base_articles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "kb_article_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    article_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    symptoms = table.Column<string>(type: "text", nullable: false),
                    diagnosis_steps = table.Column<string>(type: "text", nullable: false),
                    solution_steps = table.Column<string>(type: "text", nullable: false),
                    recommended_parts = table.Column<string>(type: "jsonb", nullable: true),
                    tags = table.Column<string>(type: "jsonb", nullable: false),
                    change_description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kb_article_versions", x => x.id);
                    table.ForeignKey(
                        name: "FK_kb_article_versions_knowledge_base_articles_article_id",
                        column: x => x.article_id,
                        principalTable: "knowledge_base_articles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_kb_article_versions_article_id",
                table: "kb_article_versions",
                column: "article_id");

            migrationBuilder.CreateIndex(
                name: "IX_kb_article_versions_article_id_version_number",
                table: "kb_article_versions",
                columns: new[] { "article_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_kb_article_versions_version_number",
                table: "kb_article_versions",
                column: "version_number");

            // Manual SQL for enum migration (2->3, 3->4)
            migrationBuilder.Sql("UPDATE knowledge_base_articles SET status = 4 WHERE status = 3;");
            migrationBuilder.Sql("UPDATE knowledge_base_articles SET status = 3 WHERE status = 2;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Manual SQL for enum rollback (4->3, 3->2)
            migrationBuilder.Sql("UPDATE knowledge_base_articles SET status = 2 WHERE status = 3;");
            migrationBuilder.Sql("UPDATE knowledge_base_articles SET status = 3 WHERE status = 4;");

            migrationBuilder.DropTable(
                name: "kb_article_versions");

            migrationBuilder.DropColumn(
                name: "manager_reject_reason",
                table: "knowledge_base_articles");

            migrationBuilder.DropColumn(
                name: "pending_review_by",
                table: "knowledge_base_articles");

            migrationBuilder.DropColumn(
                name: "review_required",
                table: "knowledge_base_articles");
        }
    }
}
