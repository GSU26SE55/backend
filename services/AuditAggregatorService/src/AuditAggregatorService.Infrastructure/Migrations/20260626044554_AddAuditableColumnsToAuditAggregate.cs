using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditAggregatorService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditableColumnsToAuditAggregate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ingested_at",
                table: "audit_aggregate",
                newName: "created_at");

            migrationBuilder.AddColumn<Guid>(
                name: "created_by",
                table: "audit_aggregate",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at",
                table: "audit_aggregate",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_deleted",
                table: "audit_aggregate",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at",
                table: "audit_aggregate",
                type: "timestamptz",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "created_by",
                table: "audit_aggregate");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "audit_aggregate");

            migrationBuilder.DropColumn(
                name: "is_deleted",
                table: "audit_aggregate");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "audit_aggregate");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "audit_aggregate",
                newName: "ingested_at");
        }
    }
}
