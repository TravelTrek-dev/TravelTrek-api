using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelTrek.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDurationToTripPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Duration",
                table: "TripPlans",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Duration",
                table: "TripPlans");
        }
    }
}
