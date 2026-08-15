using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParcelNumberGenerator.Notifications.Migrations.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ParcelNumber = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    Body = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Severity = table.Column<int>(type: "integer", nullable: false),
                    RaisedBy = table.Column<int>(type: "integer", nullable: false),
                    AcknowledgementRequired = table.Column<bool>(type: "boolean", nullable: false),
                    AcknowledgedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AcknowledgedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Pinned = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notifications", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_created_at",
                table: "notifications",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_outstanding",
                table: "notifications",
                columns: new[] { "AcknowledgementRequired", "AcknowledgedAt" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_parcel_number",
                table: "notifications",
                column: "ParcelNumber");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notifications");
        }
    }
}
