using TravelTrek.Application.DTOs.TripPlanner;
using TravelTrek.Domain.Common;

namespace TravelTrek.Application.Interfaces;

public interface ITripGenerationService
{
    Task<Result<TripPlanResponse>> GenerateTripPlanAsync(TripPlanRequest request, CancellationToken ct = default);
    Task<Result<TripPlanResponse>> RefinePlanAsync(RefinePlanRequest request, Guid planId, Guid userId, CancellationToken ct = default);
}
