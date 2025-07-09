using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NobetApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDutySchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DutySchedules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Department = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Primary = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Backup = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Kanban = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Monitoring = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DutySchedules", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DutySchedules");
        }
    }
}
