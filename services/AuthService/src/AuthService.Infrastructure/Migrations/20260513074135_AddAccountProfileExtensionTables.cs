using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountProfileExtensionTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "account_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    avatar_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    external_avatar_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    avatar_source = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    birth_date = table.Column<DateTime>(type: "date", nullable: true),
                    time_zone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_profiles", x => x.id);
                    table.ForeignKey(
                        name: "FK_account_profiles_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "staff_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    department = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    max_concurrent_tickets = table.Column<int>(type: "integer", nullable: false, defaultValue: 3),
                    is_available = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_staff_profiles", x => x.id);
                    table.UniqueConstraint("AK_staff_profiles_account_id", x => x.account_id);
                    table.ForeignKey(
                        name: "FK_staff_profiles_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "staff_skills",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    staff_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    skill_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    skill_level = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    certified_until = table.Column<DateTime>(type: "date", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_staff_skills", x => x.id);
                    table.ForeignKey(
                        name: "FK_staff_skills_staff_profiles_staff_account_id",
                        column: x => x.staff_account_id,
                        principalTable: "staff_profiles",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_account_profiles_account_id",
                table: "account_profiles",
                column: "account_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_account_profiles_avatar_file_id",
                table: "account_profiles",
                column: "avatar_file_id",
                filter: "\"avatar_file_id\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_account_profiles_is_deleted",
                table: "account_profiles",
                column: "is_deleted");

            migrationBuilder.CreateIndex(
                name: "IX_staff_profiles_account_id",
                table: "staff_profiles",
                column: "account_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_staff_profiles_employee_code",
                table: "staff_profiles",
                column: "employee_code",
                unique: true,
                filter: "\"employee_code\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_staff_profiles_is_available",
                table: "staff_profiles",
                column: "is_available");

            migrationBuilder.CreateIndex(
                name: "IX_staff_profiles_is_deleted",
                table: "staff_profiles",
                column: "is_deleted");

            migrationBuilder.CreateIndex(
                name: "IX_staff_skills_is_deleted",
                table: "staff_skills",
                column: "is_deleted");

            migrationBuilder.CreateIndex(
                name: "IX_staff_skills_skill_code",
                table: "staff_skills",
                column: "skill_code");

            migrationBuilder.CreateIndex(
                name: "IX_staff_skills_staff_account_id_skill_code",
                table: "staff_skills",
                columns: new[] { "staff_account_id", "skill_code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_profiles");

            migrationBuilder.DropTable(
                name: "staff_skills");

            migrationBuilder.DropTable(
                name: "staff_profiles");
        }
    }
}
