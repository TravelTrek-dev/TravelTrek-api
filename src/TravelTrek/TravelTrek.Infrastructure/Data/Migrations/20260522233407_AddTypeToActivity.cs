using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelTrek.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTypeToActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "Activities",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Type",
                table: "Activities");
        }
    }
}
