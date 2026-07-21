using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MergeKbContentFieldsToSingleContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Step 1: Add new content column (nullable temporarily)
            migrationBuilder.Sql("ALTER TABLE knowledge_base_articles ADD COLUMN content jsonb;");
            migrationBuilder.Sql("ALTER TABLE kb_article_versions ADD COLUMN content jsonb;");

            // Step 2: Populate content by combining 4 old fields into HTML string stored as JSON string
            // to_json(html_string)::jsonb stores the HTML as a JSON string value ("\"<h2>...\"")
            // matching how KnowledgeBaseMapper.ToJsonDoc(htmlString) works
            foreach (var table in new[] { "knowledge_base_articles", "kb_article_versions" })
            {
                migrationBuilder.Sql($@"UPDATE {table} SET content = to_json(
    '<h2>Triệu chứng</h2>' ||
    COALESCE(CASE WHEN jsonb_typeof(symptoms) = 'string' THEN '<p>' || (symptoms#>>'{{}}') || '</p>'
                  ELSE '<p>' || symptoms::text || '</p>' END, '') ||
    '<h2>Các bước chẩn đoán</h2>' ||
    COALESCE(CASE WHEN jsonb_typeof(diagnosis_steps) = 'string' THEN '<p>' || (diagnosis_steps#>>'{{}}') || '</p>'
                  ELSE '<p>' || diagnosis_steps::text || '</p>' END, '') ||
    '<h2>Các bước giải quyết</h2>' ||
    COALESCE(CASE WHEN jsonb_typeof(solution_steps) = 'string' THEN '<p>' || (solution_steps#>>'{{}}') || '</p>'
                  ELSE '<p>' || solution_steps::text || '</p>' END, '')
)::jsonb;");
            }

            // Step 3: Set NOT NULL constraint now that data is populated
            migrationBuilder.Sql("ALTER TABLE knowledge_base_articles ALTER COLUMN content SET NOT NULL;");
            migrationBuilder.Sql("ALTER TABLE kb_article_versions ALTER COLUMN content SET NOT NULL;");

            // Step 4: Drop the 4 old columns
            migrationBuilder.DropColumn(name: "symptoms", table: "knowledge_base_articles");
            migrationBuilder.DropColumn(name: "diagnosis_steps", table: "knowledge_base_articles");
            migrationBuilder.DropColumn(name: "solution_steps", table: "knowledge_base_articles");
            migrationBuilder.DropColumn(name: "recommended_parts", table: "knowledge_base_articles");

            migrationBuilder.DropColumn(name: "symptoms", table: "kb_article_versions");
            migrationBuilder.DropColumn(name: "diagnosis_steps", table: "kb_article_versions");
            migrationBuilder.DropColumn(name: "solution_steps", table: "kb_article_versions");
            migrationBuilder.DropColumn(name: "recommended_parts", table: "kb_article_versions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restore old schema (data recovery: copy content back to symptoms; other fields will be empty)
            foreach (var table in new[] { "knowledge_base_articles", "kb_article_versions" })
            {
                migrationBuilder.Sql($"ALTER TABLE {table} ADD COLUMN symptoms jsonb NOT NULL DEFAULT '{{}}'::jsonb;");
                migrationBuilder.Sql($"ALTER TABLE {table} ADD COLUMN diagnosis_steps jsonb NOT NULL DEFAULT '{{}}'::jsonb;");
                migrationBuilder.Sql($"ALTER TABLE {table} ADD COLUMN solution_steps jsonb NOT NULL DEFAULT '{{}}'::jsonb;");
                migrationBuilder.Sql($"ALTER TABLE {table} ADD COLUMN recommended_parts jsonb;");
                migrationBuilder.Sql($"UPDATE {table} SET symptoms = content;");
                migrationBuilder.Sql($"ALTER TABLE {table} DROP COLUMN content;");
            }
        }
    }
}
