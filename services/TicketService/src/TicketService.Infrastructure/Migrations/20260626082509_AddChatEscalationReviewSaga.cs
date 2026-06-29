using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChatEscalationReviewSaga : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chat_escalation_review_saga_states",
                columns: table => new
                {
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    current_state = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    manager_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    escalation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    pending_timeout_token_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_escalation_review_saga_states", x => x.correlation_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_chat_escalation_review_saga_states_current_state",
                table: "chat_escalation_review_saga_states",
                column: "current_state");

            migrationBuilder.CreateIndex(
                name: "ix_chat_escalation_review_saga_states_started_at",
                table: "chat_escalation_review_saga_states",
                column: "started_at");

            migrationBuilder.CreateIndex(
                name: "ix_chat_escalation_review_saga_states_ticket_id",
                table: "chat_escalation_review_saga_states",
                column: "ticket_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_escalation_review_saga_states");
        }
    }
}
