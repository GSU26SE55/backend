using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketService.Infrastructure.Migrations;

/// <summary>
/// Removes chat templates from the EF model without altering existing historical data.
/// </summary>
public partial class RemoveChatTemplateModel : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
