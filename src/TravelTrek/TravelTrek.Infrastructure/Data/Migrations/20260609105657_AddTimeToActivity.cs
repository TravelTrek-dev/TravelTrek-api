using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelTrek.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTimeToActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Time",
                table: "Activities",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Time",
                table: "Activities");
        }
    }
}
