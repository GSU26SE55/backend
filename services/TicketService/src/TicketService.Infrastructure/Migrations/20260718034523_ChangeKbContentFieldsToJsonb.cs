using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ChangeKbContentFieldsToJsonb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Reset existing text data to valid JSON before converting column type
            migrationBuilder.Sql("UPDATE knowledge_base_articles SET symptoms = '{}', solution_steps = '{}', diagnosis_steps = '{}';");
            migrationBuilder.Sql("UPDATE kb_article_versions SET symptoms = '{}', solution_steps = '{}', diagnosis_steps = '{}';");
            migrationBuilder.Sql("UPDATE blog_templates SET content_html = '{}';");
            migrationBuilder.Sql("UPDATE blog_posts SET content_html = '{}';");
            migrationBuilder.Sql("UPDATE blog_post_versions SET content_html = '{}';");

            migrationBuilder.Sql("ALTER TABLE knowledge_base_articles ALTER COLUMN symptoms TYPE jsonb USING symptoms::jsonb;");
            migrationBuilder.Sql("ALTER TABLE knowledge_base_articles ALTER COLUMN solution_steps TYPE jsonb USING solution_steps::jsonb;");
            migrationBuilder.Sql("ALTER TABLE knowledge_base_articles ALTER COLUMN diagnosis_steps TYPE jsonb USING diagnosis_steps::jsonb;");

            migrationBuilder.Sql("ALTER TABLE kb_article_versions ALTER COLUMN symptoms TYPE jsonb USING symptoms::jsonb;");
            migrationBuilder.Sql("ALTER TABLE kb_article_versions ALTER COLUMN solution_steps TYPE jsonb USING solution_steps::jsonb;");
            migrationBuilder.Sql("ALTER TABLE kb_article_versions ALTER COLUMN diagnosis_steps TYPE jsonb USING diagnosis_steps::jsonb;");

            migrationBuilder.Sql("ALTER TABLE blog_templates ALTER COLUMN content_html TYPE jsonb USING content_html::jsonb;");
            migrationBuilder.Sql("ALTER TABLE blog_posts ALTER COLUMN content_html TYPE jsonb USING content_html::jsonb;");
            migrationBuilder.Sql("ALTER TABLE blog_post_versions ALTER COLUMN content_html TYPE jsonb USING content_html::jsonb;");

            migrationBuilder.CreateTable(
                name: "ticket_battery_assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    battery_asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_battery_assets", x => x.id);
                    table.ForeignKey(
                        name: "FK_ticket_battery_assets_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalTable: "tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ticket_battery_assets_battery_asset_id",
                table: "ticket_battery_assets",
                column: "battery_asset_id");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_battery_assets_ticket_id",
                table: "ticket_battery_assets",
                column: "ticket_id");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_battery_assets_ticket_id_battery_asset_id",
                table: "ticket_battery_assets",
                columns: new[] { "ticket_id", "battery_asset_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ticket_battery_assets");

            migrationBuilder.Sql("ALTER TABLE knowledge_base_articles ALTER COLUMN symptoms TYPE text USING symptoms::text;");
            migrationBuilder.Sql("ALTER TABLE knowledge_base_articles ALTER COLUMN solution_steps TYPE text USING solution_steps::text;");
            migrationBuilder.Sql("ALTER TABLE knowledge_base_articles ALTER COLUMN diagnosis_steps TYPE text USING diagnosis_steps::text;");

            migrationBuilder.Sql("ALTER TABLE kb_article_versions ALTER COLUMN symptoms TYPE text USING symptoms::text;");
            migrationBuilder.Sql("ALTER TABLE kb_article_versions ALTER COLUMN solution_steps TYPE text USING solution_steps::text;");
            migrationBuilder.Sql("ALTER TABLE kb_article_versions ALTER COLUMN diagnosis_steps TYPE text USING diagnosis_steps::text;");

            migrationBuilder.Sql("ALTER TABLE blog_templates ALTER COLUMN content_html TYPE text USING content_html::text;");
            migrationBuilder.Sql("ALTER TABLE blog_posts ALTER COLUMN content_html TYPE text USING content_html::text;");
            migrationBuilder.Sql("ALTER TABLE blog_post_versions ALTER COLUMN content_html TYPE text USING content_html::text;");
        }
    }
}
