using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BatteryService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSiteAndBatteryGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "battery_group_id",
                table: "battery_assets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "site_id",
                table: "battery_assets",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "sites",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    capacity_kw = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    install_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    contact_person_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    contact_person_phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sites", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "battery_groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    battery_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    battery_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_battery_groups", x => x.id);
                    table.ForeignKey(
                        name: "FK_battery_groups_battery_types_battery_type_id",
                        column: x => x.battery_type_id,
                        principalTable: "battery_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_battery_groups_sites_site_id",
                        column: x => x.site_id,
                        principalTable: "sites",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_battery_assets_battery_group_id",
                table: "battery_assets",
                column: "battery_group_id");

            migrationBuilder.CreateIndex(
                name: "IX_battery_assets_battery_group_id_is_deleted_status",
                table: "battery_assets",
                columns: new[] { "battery_group_id", "is_deleted", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_battery_assets_site_id",
                table: "battery_assets",
                column: "site_id");

            migrationBuilder.CreateIndex(
                name: "IX_battery_assets_site_id_is_deleted_status",
                table: "battery_assets",
                columns: new[] { "site_id", "is_deleted", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_battery_groups_battery_type_id",
                table: "battery_groups",
                column: "battery_type_id");

            migrationBuilder.CreateIndex(
                name: "IX_battery_groups_site_id",
                table: "battery_groups",
                column: "site_id");

            migrationBuilder.CreateIndex(
                name: "IX_battery_groups_site_id_name",
                table: "battery_groups",
                columns: new[] { "site_id", "name" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "IX_sites_customer_id",
                table: "sites",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "IX_sites_customer_id_is_deleted_status",
                table: "sites",
                columns: new[] { "customer_id", "is_deleted", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_sites_customer_id_name",
                table: "sites",
                columns: new[] { "customer_id", "name" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "IX_sites_status",
                table: "sites",
                column: "status");

            migrationBuilder.AddForeignKey(
                name: "FK_battery_assets_battery_groups_battery_group_id",
                table: "battery_assets",
                column: "battery_group_id",
                principalTable: "battery_groups",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_battery_assets_sites_site_id",
                table: "battery_assets",
                column: "site_id",
                principalTable: "sites",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_battery_assets_battery_groups_battery_group_id",
                table: "battery_assets");

            migrationBuilder.DropForeignKey(
                name: "FK_battery_assets_sites_site_id",
                table: "battery_assets");

            migrationBuilder.DropTable(
                name: "battery_groups");

            migrationBuilder.DropTable(
                name: "sites");

            migrationBuilder.DropIndex(
                name: "IX_battery_assets_battery_group_id",
                table: "battery_assets");

            migrationBuilder.DropIndex(
                name: "IX_battery_assets_battery_group_id_is_deleted_status",
                table: "battery_assets");

            migrationBuilder.DropIndex(
                name: "IX_battery_assets_site_id",
                table: "battery_assets");

            migrationBuilder.DropIndex(
                name: "IX_battery_assets_site_id_is_deleted_status",
                table: "battery_assets");

            migrationBuilder.DropColumn(
                name: "battery_group_id",
                table: "battery_assets");

            migrationBuilder.DropColumn(
                name: "site_id",
                table: "battery_assets");
        }
    }
}
