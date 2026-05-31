using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelTrek.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class UpdateBudgetToNumericAndCurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Safely convert existing string data in Budget to decimal, setting invalid text to NULL
            migrationBuilder.Sql("UPDATE [TripPlans] SET [Budget] = TRY_CAST([Budget] AS decimal(18,2));");

            migrationBuilder.AlterColumn<decimal>(
                name: "Budget",
                table: "TripPlans",
                type: "decimal(18,2)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "TripPlans",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Currency",
                table: "TripPlans");

            migrationBuilder.AlterColumn<string>(
                name: "Budget",
                table: "TripPlans",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldNullable: true);
        }
    }
}
