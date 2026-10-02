using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vanta.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationGroupFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsGroup",
                table: "conversations",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsGroup",
                table: "conversations");
        }
    }
}
