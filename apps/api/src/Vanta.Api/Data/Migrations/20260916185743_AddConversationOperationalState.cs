using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vanta.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationOperationalState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AssignedAt",
                table: "conversations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssignedUserId",
                table: "conversations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrentIntent",
                table: "conversations",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastActivityAt",
                table: "conversations",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            // Backfill existing rows: last activity defaults to the last known message time, and
            // the legacy "Open" status maps to the new AI_ACTIVE state.
            migrationBuilder.Sql("UPDATE conversations SET \"LastActivityAt\" = \"LastMessageAt\";");
            migrationBuilder.Sql("UPDATE conversations SET \"Status\" = 'AI_ACTIVE' WHERE \"Status\" = 'Open';");

            migrationBuilder.AddColumn<Guid>(
                name: "LastRespondedToMessageId",
                table: "conversations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "conversation_activities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    FromStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ToStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignedUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversation_activities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_conversation_activities_conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_conversations_AssignedUserId",
                table: "conversations",
                column: "AssignedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_conversations_TenantId_AssignedUserId",
                table: "conversations",
                columns: new[] { "TenantId", "AssignedUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_conversations_TenantId_Status_LastActivityAt",
                table: "conversations",
                columns: new[] { "TenantId", "Status", "LastActivityAt" });

            migrationBuilder.CreateIndex(
                name: "IX_conversation_activities_ConversationId",
                table: "conversation_activities",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_conversation_activities_TenantId_ConversationId_CreatedAt",
                table: "conversation_activities",
                columns: new[] { "TenantId", "ConversationId", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_conversations_users_AssignedUserId",
                table: "conversations",
                column: "AssignedUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_conversations_users_AssignedUserId",
                table: "conversations");

            migrationBuilder.DropTable(
                name: "conversation_activities");

            migrationBuilder.DropIndex(
                name: "IX_conversations_AssignedUserId",
                table: "conversations");

            migrationBuilder.DropIndex(
                name: "IX_conversations_TenantId_AssignedUserId",
                table: "conversations");

            migrationBuilder.DropIndex(
                name: "IX_conversations_TenantId_Status_LastActivityAt",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "AssignedAt",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "AssignedUserId",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "CurrentIntent",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "LastActivityAt",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "LastRespondedToMessageId",
                table: "conversations");
        }
    }
}
