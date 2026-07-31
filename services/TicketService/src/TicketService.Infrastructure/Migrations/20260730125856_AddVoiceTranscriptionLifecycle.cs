using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVoiceTranscriptionLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "transcribed_at",
                table: "ticket_chats",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "transcription_started_at",
                table: "ticket_chats",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "voice_transcription_error",
                table: "ticket_chats",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "voice_transcription_status",
                table: "ticket_chats",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "transcribed_at",
                table: "ticket_chats");

            migrationBuilder.DropColumn(
                name: "transcription_started_at",
                table: "ticket_chats");

            migrationBuilder.DropColumn(
                name: "voice_transcription_error",
                table: "ticket_chats");

            migrationBuilder.DropColumn(
                name: "voice_transcription_status",
                table: "ticket_chats");
        }
    }
}
