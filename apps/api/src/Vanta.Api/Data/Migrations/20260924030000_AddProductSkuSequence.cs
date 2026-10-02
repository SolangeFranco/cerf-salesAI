using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Vanta.Api.Data;

namespace Vanta.Api.Data.Migrations;

[DbContext(typeof(VantaDbContext))]
[Migration("20260924030000_AddProductSkuSequence")]
public partial class AddProductSkuSequence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("CREATE SEQUENCE IF NOT EXISTS product_sku_sequence START WITH 1 INCREMENT BY 1;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP SEQUENCE IF EXISTS product_sku_sequence;");
    }
}