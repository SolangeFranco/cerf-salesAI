using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vanta.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCommerceEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "commerce_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    CartId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Metadata = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_commerce_events", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_commerce_events_TenantId_CartId",
                table: "commerce_events",
                columns: new[] { "TenantId", "CartId" });

            migrationBuilder.CreateIndex(
                name: "IX_commerce_events_TenantId_ConversationId_CreatedAt",
                table: "commerce_events",
                columns: new[] { "TenantId", "ConversationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_commerce_events_TenantId_OrderId",
                table: "commerce_events",
                columns: new[] { "TenantId", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_commerce_events_TenantId_PaymentId",
                table: "commerce_events",
                columns: new[] { "TenantId", "PaymentId" });

            migrationBuilder.CreateIndex(
                name: "IX_commerce_events_TenantId_Type_CreatedAt",
                table: "commerce_events",
                columns: new[] { "TenantId", "Type", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "commerce_events");
        }
    }
}
