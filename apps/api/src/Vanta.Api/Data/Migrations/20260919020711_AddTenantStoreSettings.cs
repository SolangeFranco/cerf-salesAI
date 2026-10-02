using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vanta.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantStoreSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "InteriorDeliveryCostGs",
                table: "tenants",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 35000m);

            migrationBuilder.AddColumn<string>(
                name: "LogoUrl",
                table: "tenants",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MetropolitanDeliveryCostGs",
                table: "tenants",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 15000m);

            migrationBuilder.AddCheckConstraint(
                name: "CK_tenants_delivery_costs_non_negative",
                table: "tenants",
                sql: "\"MetropolitanDeliveryCostGs\" >= 0 AND \"InteriorDeliveryCostGs\" >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_tenants_delivery_costs_non_negative",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "InteriorDeliveryCostGs",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "LogoUrl",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "MetropolitanDeliveryCostGs",
                table: "tenants");
        }
    }
}
