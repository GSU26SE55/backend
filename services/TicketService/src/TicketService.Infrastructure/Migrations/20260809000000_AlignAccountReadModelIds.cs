using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketService.Infrastructure.Migrations;

/// <summary>
/// Aligns TicketService account-mirror primary keys with AuthService account IDs.
/// Ticket records already reference AuthService IDs, so no ticket relationship is changed.
/// </summary>
public partial class AlignAccountReadModelIds : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM customer_accounts GROUP BY account_id HAVING COUNT(*) > 1)
                   OR EXISTS (SELECT 1 FROM staff_accounts GROUP BY account_id HAVING COUNT(*) > 1) THEN
                    RAISE EXCEPTION 'Cannot align account read-model IDs: duplicate account_id rows exist.';
                END IF;
            END $$;
            """);

        migrationBuilder.Sql("UPDATE customer_accounts SET id = account_id WHERE id <> account_id;");
        migrationBuilder.Sql("UPDATE staff_accounts SET id = account_id WHERE id <> account_id;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The old randomly generated local keys cannot be reconstructed safely.
    }
}
