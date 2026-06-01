using TravelTrek.Application.DTOs.TripPlanner;
using TravelTrek.Domain.Common;

namespace TravelTrek.Application.Interfaces;

public interface ITripPlanCrudService
{
    Task<Result<Guid>> SaveCreatedTripPlanAsync(SaveTripPlanRequest planDto, Guid userId, CancellationToken ct = default);
    Task<Result<Guid>> SaveRefinedTripPlanAsync(Guid tripId, SaveTripPlanRequest updatedPlanDto, Guid userId, CancellationToken ct = default);
    Task<Result> UpdateTripPlanAsync(Guid tripId, SaveTripPlanRequest updatedPlanDto, Guid userId, CancellationToken ct = default);
    Task<Result> DeleteTripPlanAsync(Guid tripId, Guid userId, CancellationToken ct = default);
    Task<Result<TripPlanResponse>> GetTripPlanAsync(Guid id, Guid userId);
    Task<Result<IEnumerable<TripPlanResponse>>> GetTripPlansAsync(Guid userId);
}
