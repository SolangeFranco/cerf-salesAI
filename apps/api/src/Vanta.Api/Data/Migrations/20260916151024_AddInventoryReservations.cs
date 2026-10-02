using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vanta.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReservedStock",
                table: "product_variants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "inventory_movements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductVariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    StockReservationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_movements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "stock_reservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductVariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CommittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_reservations", x => x.Id);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_product_variants_stock_non_negative",
                table: "product_variants",
                sql: "\"StockQuantity\" >= 0 AND \"ReservedStock\" >= 0 AND \"ReservedStock\" <= \"StockQuantity\"");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_movements_TenantId_OrderId_Type",
                table: "inventory_movements",
                columns: new[] { "TenantId", "OrderId", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_movements_TenantId_ProductVariantId_CreatedAt",
                table: "inventory_movements",
                columns: new[] { "TenantId", "ProductVariantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_reservations_OrderItemId",
                table: "stock_reservations",
                column: "OrderItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_reservations_TenantId_OrderId_Status",
                table: "stock_reservations",
                columns: new[] { "TenantId", "OrderId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inventory_movements");

            migrationBuilder.DropTable(
                name: "stock_reservations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_product_variants_stock_non_negative",
                table: "product_variants");

            migrationBuilder.DropColumn(
                name: "ReservedStock",
                table: "product_variants");
        }
    }
}
