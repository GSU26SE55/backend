using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketAiSuggestionAndStaffRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "role",
                table: "staff_accounts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Staff");

            migrationBuilder.AddColumn<string>(
                name: "ai_suggestion_json",
                table: "alert_ticket_saga_states",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ticket_ai_suggestions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prescription = table.Column<string>(type: "text", nullable: false),
                    action_steps = table.Column<string>(type: "jsonb", nullable: false),
                    ppe_required = table.Column<string>(type: "jsonb", nullable: false),
                    sop_references = table.Column<string>(type: "jsonb", nullable: false),
                    escalation_conditions = table.Column<string>(type: "jsonb", nullable: false),
                    safety_warnings = table.Column<string>(type: "jsonb", nullable: false),
                    kb_doc_refs = table.Column<string>(type: "jsonb", nullable: false),
                    human_verification_required = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    blocked = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    enriched = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    llm_provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    prescription_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_ai_suggestions", x => x.id);
                    table.ForeignKey(
                        name: "FK_ticket_ai_suggestions_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalTable: "tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ticket_ai_suggestions_ticket_id",
                table: "ticket_ai_suggestions",
                column: "ticket_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ticket_ai_suggestions");

            migrationBuilder.DropColumn(
                name: "role",
                table: "staff_accounts");

            migrationBuilder.DropColumn(
                name: "ai_suggestion_json",
                table: "alert_ticket_saga_states");
        }
    }
}
