using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vanta.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryDistanceSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_tenants_delivery_costs_non_negative",
                table: "tenants");

            migrationBuilder.AddColumn<decimal>(
                name: "DeliveryBaseCostGs",
                table: "tenants",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 10000m);

            migrationBuilder.AddColumn<decimal>(
                name: "DeliveryExtraCostPerKmGs",
                table: "tenants",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DeliveryIncludedDistanceKm",
                table: "tenants",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 10m);

            migrationBuilder.AddCheckConstraint(
                name: "CK_tenants_delivery_costs_non_negative",
                table: "tenants",
                sql: "\"MetropolitanDeliveryCostGs\" >= 0 AND \"InteriorDeliveryCostGs\" >= 0 AND \"DeliveryBaseCostGs\" >= 0 AND \"DeliveryIncludedDistanceKm\" > 0 AND \"DeliveryExtraCostPerKmGs\" >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_tenants_delivery_costs_non_negative",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "DeliveryBaseCostGs",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "DeliveryExtraCostPerKmGs",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "DeliveryIncludedDistanceKm",
                table: "tenants");

            migrationBuilder.AddCheckConstraint(
                name: "CK_tenants_delivery_costs_non_negative",
                table: "tenants",
                sql: "\"MetropolitanDeliveryCostGs\" >= 0 AND \"InteriorDeliveryCostGs\" >= 0");
        }
    }
}
