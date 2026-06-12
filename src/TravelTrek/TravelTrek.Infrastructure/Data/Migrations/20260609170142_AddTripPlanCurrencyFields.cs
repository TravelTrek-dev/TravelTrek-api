using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelTrek.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTripPlanCurrencyFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ConversionRate",
                table: "TripPlans",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserCurrency",
                table: "TripPlans",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConversionRate",
                table: "TripPlans");

            migrationBuilder.DropColumn(
                name: "UserCurrency",
                table: "TripPlans");
        }
    }
}
