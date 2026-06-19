using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountMergeLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "merged_at",
                table: "accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "merged_into_id",
                table: "accounts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "account_merge_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    primary_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    secondary_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    performed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    secondary_account_snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                    conflict_resolution_json = table.Column<string>(type: "jsonb", nullable: false),
                    sessions_revoked = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    audit_logs_linked = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_merge_logs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_account_merge_logs_primary",
                table: "account_merge_logs",
                column: "primary_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_account_merge_logs_secondary",
                table: "account_merge_logs",
                column: "secondary_account_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_merge_logs");

            migrationBuilder.DropColumn(
                name: "merged_at",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "merged_into_id",
                table: "accounts");
        }
    }
}
