using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BatteryService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIotDeviceMqttPasswordPlaintextAndPollingInterval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "mqtt_password_plaintext",
                table: "iot_devices",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "polling_interval_seconds",
                table: "iot_devices",
                type: "integer",
                nullable: false,
                defaultValue: 10);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "mqtt_password_plaintext",
                table: "iot_devices");

            migrationBuilder.DropColumn(
                name: "polling_interval_seconds",
                table: "iot_devices");
        }
    }
}
