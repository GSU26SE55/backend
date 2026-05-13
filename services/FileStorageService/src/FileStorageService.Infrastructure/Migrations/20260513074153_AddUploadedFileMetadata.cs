using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FileStorageService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUploadedFileMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "uploaded_files",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    object_key = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    original_file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    folder_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    purpose = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    status = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    public_url = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_uploaded_files", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_uploaded_files_is_deleted",
                table: "uploaded_files",
                column: "is_deleted");

            migrationBuilder.CreateIndex(
                name: "IX_uploaded_files_object_key",
                table: "uploaded_files",
                column: "object_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_uploaded_files_purpose",
                table: "uploaded_files",
                column: "purpose");

            migrationBuilder.CreateIndex(
                name: "IX_uploaded_files_status",
                table: "uploaded_files",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "uploaded_files");
        }
    }
}
