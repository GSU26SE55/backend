using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotificationService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationDispatchRetryColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Compatibility path for databases that were briefly migrated by the old
            // AddNotificationRetryFields migration. That migration used attempt_count and
            // already created next_attempt_at, while this branch uses dispatch_attempt_count.
            // Preserve retry data instead of requiring a database reset.
            migrationBuilder.Sql(
                """
                DO $migration$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'notifications'
                          AND column_name = 'attempt_count'
                    ) AND NOT EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'notifications'
                          AND column_name = 'dispatch_attempt_count'
                    ) THEN
                        ALTER TABLE notifications
                            RENAME COLUMN attempt_count TO dispatch_attempt_count;
                    ELSIF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'notifications'
                          AND column_name = 'dispatch_attempt_count'
                    ) THEN
                        ALTER TABLE notifications
                            ADD COLUMN dispatch_attempt_count integer NOT NULL DEFAULT 0;
                    END IF;

                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'notifications'
                          AND column_name = 'attempt_count'
                    ) THEN
                        UPDATE notifications
                        SET dispatch_attempt_count = GREATEST(dispatch_attempt_count, attempt_count);

                        ALTER TABLE notifications DROP COLUMN attempt_count;
                    END IF;

                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'notifications'
                          AND column_name = 'next_attempt_at'
                    ) THEN
                        ALTER TABLE notifications
                            ADD COLUMN next_attempt_at timestamp with time zone;
                    END IF;
                END
                $migration$;

                DROP INDEX IF EXISTS "IX_notifications_status_next_attempt_at";
                CREATE INDEX IF NOT EXISTS ix_notifications_dispatch_queue
                    ON notifications (status, next_attempt_at, created_at);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS ix_notifications_dispatch_queue;
                ALTER TABLE notifications DROP COLUMN IF EXISTS dispatch_attempt_count;
                ALTER TABLE notifications DROP COLUMN IF EXISTS next_attempt_at;
                """);
        }
    }
}
