using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateOutboxSchemaGH88 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_outbox_messages_occurred_at",
                table: "outbox_messages");

            migrationBuilder.DropIndex(
                name: "IX_outbox_messages_processed_at",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "correlation_id",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "created_at",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "created_by",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "event_type",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "is_deleted",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "processed_at",
                table: "outbox_messages");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "outbox_messages",
                newName: "processed_at_utc");

            migrationBuilder.RenameColumn(
                name: "occurred_at",
                table: "outbox_messages",
                newName: "occurred_at_utc");

            migrationBuilder.AlterColumn<int>(
                name: "retry_count",
                table: "outbox_messages",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "last_error",
                table: "outbox_messages",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "aggregate_id",
                table: "outbox_messages",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "type",
                table: "outbox_messages",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "idx_outbox_pending",
                table: "outbox_messages",
                column: "occurred_at_utc",
                filter: "processed_at_utc IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_outbox_pending",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "aggregate_id",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "type",
                table: "outbox_messages");

            migrationBuilder.RenameColumn(
                name: "processed_at_utc",
                table: "outbox_messages",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "occurred_at_utc",
                table: "outbox_messages",
                newName: "occurred_at");

            migrationBuilder.AlterColumn<int>(
                name: "retry_count",
                table: "outbox_messages",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 0);

            migrationBuilder.AlterColumn<string>(
                name: "last_error",
                table: "outbox_messages",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "correlation_id",
                table: "outbox_messages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "created_at",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "created_by",
                table: "outbox_messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "event_type",
                table: "outbox_messages",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "is_deleted",
                table: "outbox_messages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "processed_at",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_occurred_at",
                table: "outbox_messages",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_processed_at",
                table: "outbox_messages",
                column: "processed_at");
        }
    }
}
