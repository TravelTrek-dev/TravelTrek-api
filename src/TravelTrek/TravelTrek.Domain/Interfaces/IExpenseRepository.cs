using TravelTrek.Domain.Entities.Trip;

namespace TravelTrek.Domain.Interfaces;

public interface IExpenseRepository : IGenericRepository<Expense>
{
    Task<List<Expense>> GetForTrip(Guid tripPlanId);
}