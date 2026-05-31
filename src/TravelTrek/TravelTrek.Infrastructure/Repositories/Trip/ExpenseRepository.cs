using Microsoft.EntityFrameworkCore;
using TravelTrek.Domain.Entities.Trip;
using TravelTrek.Domain.Interfaces;
using TravelTrek.Infrastructure.Data;

namespace TravelTrek.Infrastructure.Repositories.Trip;

public class ExpenseRepository : GenericRepository<Expense>, IExpenseRepository
{
    public ExpenseRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<List<Expense>> GetForTrip(Guid tripPlanId)
    {
        var expenses = await _dbSet.Where(e => e.TripPlanId == tripPlanId).ToListAsync();
        return expenses;
    }

}