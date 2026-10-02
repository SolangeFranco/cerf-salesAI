using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vanta.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderReservationExpiration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReservationExpiresAt",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE orders
                SET "ReservationExpiresAt" = "CreatedAt" + INTERVAL '30 minutes'
                WHERE "Status" = 'AwaitingPayment' AND "ReservationExpiresAt" IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_orders_Status_ReservationExpiresAt",
                table: "orders",
                columns: new[] { "Status", "ReservationExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_orders_Status_ReservationExpiresAt",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "ReservationExpiresAt",
                table: "orders");
        }
    }
}
