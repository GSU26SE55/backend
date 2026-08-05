using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditAggregatorService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditReplayJob : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_replay_job",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    service_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    from_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    to_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    expected_responders = table.Column<int>(type: "integer", nullable: false),
                    responded_count = table.Column<int>(type: "integer", nullable: false),
                    republished_count = table.Column<int>(type: "integer", nullable: false),
                    truncated = table.Column<bool>(type: "boolean", nullable: false),
                    responded_services = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    error = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    requested_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requested_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_replay_job", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_replay_job_requested_at",
                table: "audit_replay_job",
                column: "requested_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_audit_replay_job_status",
                table: "audit_replay_job",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_replay_job");
        }
    }
}
