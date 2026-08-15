using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParcelNumberGenerator.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "used_numbers",
                columns: table => new
                {
                    number = table.Column<int>(type: "int", nullable: false),
                    allocated_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_used_numbers", x => x.number);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "used_numbers");
        }
    }
}
