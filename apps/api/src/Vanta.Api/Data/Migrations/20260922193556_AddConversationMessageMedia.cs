using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vanta.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationMessageMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "MediaData",
                table: "conversation_messages",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MediaFileName",
                table: "conversation_messages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MediaKind",
                table: "conversation_messages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MediaMimeType",
                table: "conversation_messages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MediaUnavailable",
                table: "conversation_messages",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MediaData",
                table: "conversation_messages");

            migrationBuilder.DropColumn(
                name: "MediaFileName",
                table: "conversation_messages");

            migrationBuilder.DropColumn(
                name: "MediaKind",
                table: "conversation_messages");

            migrationBuilder.DropColumn(
                name: "MediaMimeType",
                table: "conversation_messages");

            migrationBuilder.DropColumn(
                name: "MediaUnavailable",
                table: "conversation_messages");
        }
    }
}
