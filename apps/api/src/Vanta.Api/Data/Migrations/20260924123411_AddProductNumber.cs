using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vanta.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProductNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProductNumber",
                table: "products",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                WITH numbered AS (
                    SELECT "Id", ROW_NUMBER() OVER (PARTITION BY "TenantId" ORDER BY "CreatedAt", "Id") AS number
                    FROM products
                )
                UPDATE products AS product
                SET "ProductNumber" = 'PRD-' || LPAD(numbered.number::text, 6, '0')
                FROM numbered
                WHERE product."Id" = numbered."Id";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_products_TenantId_ProductNumber",
                table: "products",
                columns: new[] { "TenantId", "ProductNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_products_TenantId_ProductNumber",
                table: "products");

            migrationBuilder.DropColumn(
                name: "ProductNumber",
                table: "products");
        }
    }
}
