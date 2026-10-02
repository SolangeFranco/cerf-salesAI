using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vanta.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderConversationLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ConversationId",
                table: "orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_orders_ConversationId",
                table: "orders",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_orders_TenantId_ConversationId",
                table: "orders",
                columns: new[] { "TenantId", "ConversationId" });

            migrationBuilder.AddForeignKey(
                name: "FK_orders_conversations_ConversationId",
                table: "orders",
                column: "ConversationId",
                principalTable: "conversations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_orders_conversations_ConversationId",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "IX_orders_ConversationId",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "IX_orders_TenantId_ConversationId",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "ConversationId",
                table: "orders");
        }
    }
}
