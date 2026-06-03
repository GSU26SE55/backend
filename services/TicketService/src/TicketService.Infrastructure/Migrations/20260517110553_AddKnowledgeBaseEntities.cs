using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddKnowledgeBaseEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "knowledge_base_articles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    category = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    symptoms = table.Column<string>(type: "text", nullable: false),
                    diagnosis_steps = table.Column<string>(type: "text", nullable: false),
                    solution_steps = table.Column<string>(type: "text", nullable: false),
                    recommended_parts = table.Column<string>(type: "jsonb", nullable: true),
                    tags = table.Column<List<string>>(type: "jsonb", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    view_count = table.Column<int>(type: "integer", nullable: false),
                    helpful_count = table.Column<int>(type: "integer", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_base_articles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ticket_kb_references",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kb_article_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kb_article_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    referenced_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference_type = table.Column<int>(type: "integer", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_kb_references", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_base_articles_category",
                table: "knowledge_base_articles",
                column: "category");

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_base_articles_code",
                table: "knowledge_base_articles",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_base_articles_status",
                table: "knowledge_base_articles",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_kb_references_ticket_id_kb_article_id",
                table: "ticket_kb_references",
                columns: new[] { "ticket_id", "kb_article_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "knowledge_base_articles");

            migrationBuilder.DropTable(
                name: "ticket_kb_references");
        }
    }
}
