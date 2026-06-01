using TravelTrek.Application.DTOs.TripPlanner;
using TravelTrek.Domain.Common;

namespace TravelTrek.Application.Interfaces;

public interface ITripExpenseService
{
    Task<Result<Guid>> AddTripExpenseAsync(CreateExpenseDto request, Guid tripPlanId, Guid userId, CancellationToken ct = default);
    Task<Result> EditTripExpenseAsync(EditExpenseDto request, Guid id, Guid userId, CancellationToken ct = default);
    Task<Result> DeleteTripExpenseAsync(Guid id, Guid userId, CancellationToken ct = default);
    Task<Result<ExpensesDto>> GetTripExpensesAsync(Guid tripPlanId, Guid userId, CancellationToken ct = default);
}
