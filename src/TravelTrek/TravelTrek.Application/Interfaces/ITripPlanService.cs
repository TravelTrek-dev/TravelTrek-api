using TravelTrek.Application.DTOs.TripPlanner;
using TravelTrek.Domain.Common;

namespace TravelTrek.Application.Interfaces;

public interface ITripPlanService
{
    Task<Result<TripPlanResponse>> GenerateTripPlanAsync(TripPlanRequest request, CancellationToken ct = default);
    Task<Result<Guid>> SaveTripPlanAsync(TripPlanResponse planDto, Guid userId, CancellationToken ct = default);
    Task<Result> UpdateTripPlanAsync(Guid tripId, TripPlanResponse updatedPlanDto, Guid userId, CancellationToken ct = default);
    Task<Result> DeleteTripPlanAsync(Guid tripId, Guid userId, CancellationToken ct = default);
    Task<Result<TripPlanResponse>> GetTripPlanAsync(Guid id, Guid userId);
    Task<Result<IEnumerable<TripPlanResponse>>> GetTripPlansAsync(Guid userId); 
}
