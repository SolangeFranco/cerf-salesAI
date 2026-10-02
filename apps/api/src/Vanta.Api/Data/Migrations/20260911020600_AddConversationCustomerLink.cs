using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vanta.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationCustomerLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CustomerId",
                table: "conversations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_conversations_CustomerId",
                table: "conversations",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_conversations_TenantId_CustomerId",
                table: "conversations",
                columns: new[] { "TenantId", "CustomerId" });

            migrationBuilder.AddForeignKey(
                name: "FK_conversations_customers_CustomerId",
                table: "conversations",
                column: "CustomerId",
                principalTable: "customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_conversations_customers_CustomerId",
                table: "conversations");

            migrationBuilder.DropIndex(
                name: "IX_conversations_CustomerId",
                table: "conversations");

            migrationBuilder.DropIndex(
                name: "IX_conversations_TenantId_CustomerId",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                table: "conversations");
        }
    }
}
