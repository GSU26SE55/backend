using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotificationService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "batch_id",
                table: "notifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "notification_batches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: true),
                    entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    channels = table.Column<int[]>(type: "integer[]", nullable: false),
                    source = table.Column<int>(type: "integer", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    recipient_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    notification_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_batches", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notification_batch_targets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_kind = table.Column<int>(type: "integer", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_batch_targets", x => x.id);
                    table.CheckConstraint("ck_notification_batch_targets_shape", "(target_kind = 1 AND group_id IS NOT NULL AND user_id IS NULL) OR (target_kind = 2 AND user_id IS NOT NULL AND group_id IS NULL)");
                    table.ForeignKey(
                        name: "FK_notification_batch_targets_notification_batches_batch_id",
                        column: x => x.batch_id,
                        principalTable: "notification_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_notification_batch_targets_notification_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "notification_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_batch",
                table: "notifications",
                column: "batch_id",
                filter: "batch_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_notifications_batch_user_channel",
                table: "notifications",
                columns: new[] { "batch_id", "user_id", "channel" },
                unique: true,
                filter: "batch_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_notification_batch_targets_batch",
                table: "notification_batch_targets",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_notification_batch_targets_group",
                table: "notification_batch_targets",
                column: "group_id",
                filter: "group_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_notification_batches_created_at",
                table: "notification_batches",
                column: "created_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_notification_batches_entity",
                table: "notification_batches",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_notifications_notification_batches_batch_id",
                table: "notifications",
                column: "batch_id",
                principalTable: "notification_batches",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_notifications_notification_batches_batch_id",
                table: "notifications");

            migrationBuilder.DropTable(
                name: "notification_batch_targets");

            migrationBuilder.DropTable(
                name: "notification_batches");

            migrationBuilder.DropIndex(
                name: "ix_notifications_batch",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "ux_notifications_batch_user_channel",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "batch_id",
                table: "notifications");
        }
    }
}
