using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChatFullTextSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE ticket_chats ADD COLUMN body_tsv tsvector;");

            migrationBuilder.Sql(
                "UPDATE ticket_chats SET body_tsv = to_tsvector('simple', body);");

            migrationBuilder.Sql(
                "CREATE INDEX ix_ticket_chats_body_tsv ON ticket_chats USING GIN (body_tsv);");

            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION update_ticket_chat_tsv() RETURNS trigger AS $$
                BEGIN
                    NEW.body_tsv := to_tsvector('simple', coalesce(NEW.body, ''));
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
            ");

            migrationBuilder.Sql(@"
                CREATE TRIGGER trg_ticket_chats_body_tsv
                BEFORE INSERT OR UPDATE OF body ON ticket_chats
                FOR EACH ROW EXECUTE FUNCTION update_ticket_chat_tsv();
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_ticket_chats_body_tsv ON ticket_chats;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS update_ticket_chat_tsv();");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_ticket_chats_body_tsv;");
            migrationBuilder.Sql("ALTER TABLE ticket_chats DROP COLUMN IF EXISTS body_tsv;");
        }
    }
}
