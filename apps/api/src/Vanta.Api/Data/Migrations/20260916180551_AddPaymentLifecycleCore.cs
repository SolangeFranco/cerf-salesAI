using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vanta.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentLifecycleCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PublicReference",
                table: "payments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            // Backfill existing rows with a unique value before the unique index below is created.
            migrationBuilder.Sql(
                "UPDATE payments SET \"PublicReference\" = md5(random()::text || clock_timestamp()::text || \"Id\"::text) WHERE \"PublicReference\" = '';");

            migrationBuilder.CreateTable(
                name: "payment_status_history",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ToStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_status_history", x => x.Id);
                    table.ForeignKey(
                        name: "FK_payment_status_history_payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_payments_PublicReference",
                table: "payments",
                column: "PublicReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_status_history_PaymentId",
                table: "payment_status_history",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_payment_status_history_TenantId_PaymentId_CreatedAt",
                table: "payment_status_history",
                columns: new[] { "TenantId", "PaymentId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_status_history");

            migrationBuilder.DropIndex(
                name: "IX_payments_PublicReference",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "PublicReference",
                table: "payments");
        }
    }
}
