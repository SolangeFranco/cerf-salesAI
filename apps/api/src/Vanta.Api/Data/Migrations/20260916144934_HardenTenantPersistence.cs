using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vanta.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class HardenTenantPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_product_variants_products_ProductId",
                table: "product_variants");

            migrationBuilder.DropIndex(
                name: "IX_product_variants_ProductId_IsActive",
                table: "product_variants");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_products_TenantId_Id",
                table: "products",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_product_variants_TenantId_ProductId_IsActive",
                table: "product_variants",
                columns: new[] { "TenantId", "ProductId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_payments_TenantId_Provider_ProviderReference",
                table: "payments",
                columns: new[] { "TenantId", "Provider", "ProviderReference" });

            migrationBuilder.CreateIndex(
                name: "IX_orders_TenantId_Status_CreatedAt",
                table: "orders",
                columns: new[] { "TenantId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_conversation_messages_TenantId_ConversationId_CreatedAt",
                table: "conversation_messages",
                columns: new[] { "TenantId", "ConversationId", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_product_variants_products_TenantId_ProductId",
                table: "product_variants",
                columns: new[] { "TenantId", "ProductId" },
                principalTable: "products",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_product_variants_products_TenantId_ProductId",
                table: "product_variants");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_products_TenantId_Id",
                table: "products");

            migrationBuilder.DropIndex(
                name: "IX_product_variants_TenantId_ProductId_IsActive",
                table: "product_variants");

            migrationBuilder.DropIndex(
                name: "IX_payments_TenantId_Provider_ProviderReference",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "IX_orders_TenantId_Status_CreatedAt",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "IX_conversation_messages_TenantId_ConversationId_CreatedAt",
                table: "conversation_messages");

            migrationBuilder.CreateIndex(
                name: "IX_product_variants_ProductId_IsActive",
                table: "product_variants",
                columns: new[] { "ProductId", "IsActive" });

            migrationBuilder.AddForeignKey(
                name: "FK_product_variants_products_ProductId",
                table: "product_variants",
                column: "ProductId",
                principalTable: "products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
