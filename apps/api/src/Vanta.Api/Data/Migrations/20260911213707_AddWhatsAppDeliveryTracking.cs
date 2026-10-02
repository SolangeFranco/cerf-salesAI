using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vanta.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppDeliveryTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DeliveryAttempts",
                table: "conversation_messages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryError",
                table: "conversation_messages",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryStatus",
                table: "conversation_messages",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextDeliveryAttemptAt",
                table: "conversation_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_conversation_messages_DeliveryStatus_NextDeliveryAttemptAt",
                table: "conversation_messages",
                columns: new[] { "DeliveryStatus", "NextDeliveryAttemptAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_conversation_messages_DeliveryStatus_NextDeliveryAttemptAt",
                table: "conversation_messages");

            migrationBuilder.DropColumn(
                name: "DeliveryAttempts",
                table: "conversation_messages");

            migrationBuilder.DropColumn(
                name: "DeliveryError",
                table: "conversation_messages");

            migrationBuilder.DropColumn(
                name: "DeliveryStatus",
                table: "conversation_messages");

            migrationBuilder.DropColumn(
                name: "NextDeliveryAttemptAt",
                table: "conversation_messages");
        }
    }
}
