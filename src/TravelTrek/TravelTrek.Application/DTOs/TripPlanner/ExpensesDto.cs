namespace TravelTrek.Application.DTOs.TripPlanner;

public class ExpensesDto
{
    public string City { get; set; }
    public List<ExpenseDto> Expenses { get; set; }
    public decimal Budget { get; set; }
    public decimal Spent { get; set; }
    public decimal Remaining { get; set; }
    public string Currency { get; set; }
}