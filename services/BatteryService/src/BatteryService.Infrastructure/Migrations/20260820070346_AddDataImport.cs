using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BatteryService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDataImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "import_batches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    file_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    is_dry_run = table.Column<bool>(type: "boolean", nullable: false),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: true),
                    entity_types_mask = table.Column<int>(type: "integer", nullable: false),
                    total_rows = table.Column<int>(type: "integer", nullable: false),
                    valid_rows = table.Column<int>(type: "integer", nullable: false),
                    invalid_rows = table.Column<int>(type: "integer", nullable: false),
                    created_rows = table.Column<int>(type: "integer", nullable: false),
                    updated_rows = table.Column<int>(type: "integer", nullable: false),
                    skipped_rows = table.Column<int>(type: "integer", nullable: false),
                    failed_rows = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    error_summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_batches", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "import_entity_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_type = table.Column<int>(type: "integer", nullable: false),
                    external_ref = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    external_ref_raw = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    internal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_entity_links", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "import_rows",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    import_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    row_number = table.Column<int>(type: "integer", nullable: false),
                    entity_type = table.Column<int>(type: "integer", nullable: false),
                    raw_json = table.Column<string>(type: "jsonb", nullable: false),
                    external_ref = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    errors_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    linked_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    processed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_rows", x => x.id);
                    table.ForeignKey(
                        name: "FK_import_rows_import_batches_import_batch_id",
                        column: x => x.import_batch_id,
                        principalTable: "import_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_import_batches_file_sha256",
                table: "import_batches",
                column: "file_sha256");

            migrationBuilder.CreateIndex(
                name: "idx_import_batches_status_created_at",
                table: "import_batches",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "idx_import_entity_links_batch",
                table: "import_entity_links",
                column: "created_by_batch_id");

            migrationBuilder.CreateIndex(
                name: "idx_import_entity_links_internal_id",
                table: "import_entity_links",
                column: "internal_id");

            migrationBuilder.CreateIndex(
                name: "ux_import_entity_links_entity_ref",
                table: "import_entity_links",
                columns: new[] { "entity_type", "external_ref" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "idx_import_rows_batch_entity_ref",
                table: "import_rows",
                columns: new[] { "import_batch_id", "entity_type", "external_ref" });

            migrationBuilder.CreateIndex(
                name: "idx_import_rows_batch_status",
                table: "import_rows",
                columns: new[] { "import_batch_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_import_rows_batch_entity_row",
                table: "import_rows",
                columns: new[] { "import_batch_id", "entity_type", "row_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "import_entity_links");

            migrationBuilder.DropTable(
                name: "import_rows");

            migrationBuilder.DropTable(
                name: "import_batches");
        }
    }
}
