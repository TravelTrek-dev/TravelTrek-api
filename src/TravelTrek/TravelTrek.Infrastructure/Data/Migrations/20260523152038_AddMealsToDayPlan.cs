using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelTrek.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMealsToDayPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Meals_Breakfast",
                table: "DayPlans",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Meals_Dinner",
                table: "DayPlans",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Meals_Lunch",
                table: "DayPlans",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Meals_Breakfast",
                table: "DayPlans");

            migrationBuilder.DropColumn(
                name: "Meals_Dinner",
                table: "DayPlans");

            migrationBuilder.DropColumn(
                name: "Meals_Lunch",
                table: "DayPlans");
        }
    }
}
