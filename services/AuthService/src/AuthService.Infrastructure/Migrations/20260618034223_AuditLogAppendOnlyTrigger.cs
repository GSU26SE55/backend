using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthService.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// #AUTH-29: enforce append-only ở DB layer cho audit_logs. Trước migration này chỉ rely vào
    /// application code (handler không bao giờ gọi UpdateAsync/DeleteAsync trên AuditLog). DBA hoặc
    /// rogue SQL có thể vẫn UPDATE/DELETE → mất tính toàn vẹn audit.
    ///
    /// Trigger BEFORE UPDATE OR DELETE raise EXCEPTION → block ở storage layer cho mọi connection.
    /// </summary>
    public partial class AuditLogAppendOnlyTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // CREATE OR REPLACE function — idempotent nếu re-run.
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION enforce_audit_logs_append_only()
                RETURNS TRIGGER AS $$
                BEGIN
                    RAISE EXCEPTION 'audit_logs is append-only; % is not allowed', TG_OP
                        USING ERRCODE = 'P0001';
                END;
                $$ LANGUAGE plpgsql;
            ");

            // DROP IF EXISTS để có thể re-run safely; sau đó CREATE.
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS audit_logs_no_update_delete ON audit_logs;");
            migrationBuilder.Sql(@"
                CREATE TRIGGER audit_logs_no_update_delete
                BEFORE UPDATE OR DELETE ON audit_logs
                FOR EACH ROW
                EXECUTE FUNCTION enforce_audit_logs_append_only();
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS audit_logs_no_update_delete ON audit_logs;");
            migrationBuilder.Sql(@"DROP FUNCTION IF EXISTS enforce_audit_logs_append_only();");
        }
    }
}
