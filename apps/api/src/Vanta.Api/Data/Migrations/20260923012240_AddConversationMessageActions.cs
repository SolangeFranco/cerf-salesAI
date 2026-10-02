using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vanta.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationMessageActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "conversation_messages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "QuotedExternalMessageId",
                table: "conversation_messages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuotedText",
                table: "conversation_messages",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "conversation_messages");

            migrationBuilder.DropColumn(
                name: "QuotedExternalMessageId",
                table: "conversation_messages");

            migrationBuilder.DropColumn(
                name: "QuotedText",
                table: "conversation_messages");
        }
    }
}
