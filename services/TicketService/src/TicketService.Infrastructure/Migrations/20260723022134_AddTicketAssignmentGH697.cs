using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketAssignmentGH697 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Create ticket_assignments table first
            migrationBuilder.CreateTable(
                name: "ticket_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    staff_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_assignments", x => x.id);
                    table.ForeignKey(
                        name: "FK_ticket_assignments_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalTable: "tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ticket_assignments_ticket_role",
                table: "ticket_assignments",
                columns: new[] { "ticket_id", "role" });

            migrationBuilder.CreateIndex(
                name: "ix_ticket_assignments_ticket_staff",
                table: "ticket_assignments",
                columns: new[] { "ticket_id", "staff_id" },
                unique: true);

            // 2. Seed: migrate existing assigned_staff_id → ticket_assignments as PrimaryHandler (role=1)
            migrationBuilder.Sql(@"
                INSERT INTO ticket_assignments (id, ticket_id, staff_id, role, created_at, is_deleted)
                SELECT gen_random_uuid(), id, assigned_staff_id, 1, NOW() AT TIME ZONE 'UTC', false
                FROM tickets
                WHERE assigned_staff_id IS NOT NULL
                  AND is_deleted = false;
            ");

            // 3. Drop index and column after data is migrated
            migrationBuilder.DropIndex(
                name: "IX_tickets_assigned_staff_id",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "assigned_staff_id",
                table: "tickets");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "assigned_staff_id",
                table: "tickets",
                type: "uuid",
                nullable: true);

            // Restore assigned_staff_id from PrimaryHandler (role=1) in ticket_assignments
            migrationBuilder.Sql(@"
                UPDATE tickets t
                SET assigned_staff_id = ta.staff_id
                FROM ticket_assignments ta
                WHERE ta.ticket_id = t.id
                  AND ta.role = 1
                  AND ta.is_deleted = false;
            ");

            migrationBuilder.CreateIndex(
                name: "IX_tickets_assigned_staff_id",
                table: "tickets",
                column: "assigned_staff_id");

            migrationBuilder.DropTable(
                name: "ticket_assignments");
        }
    }
}
