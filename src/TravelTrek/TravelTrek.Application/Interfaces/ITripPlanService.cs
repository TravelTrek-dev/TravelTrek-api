using TravelTrek.Application.DTOs.TripPlanner;
using TravelTrek.Domain.Common;

namespace TravelTrek.Application.Interfaces;

public interface ITripPlanService
{
    Task<Result<TripPlanResponse>> GenerateTripPlanAsync(TripPlanRequest request, CancellationToken ct = default);
    Task<Result<TripPlanResponse>> RefinePlanAsync(RefinePlanRequest request, Guid planId, Guid userId, CancellationToken ct = default);
    Task<Result<Guid>> SaveCreatedTripPlanAsync(SaveTripPlanRequest planDto, Guid userId, CancellationToken ct = default);
    Task<Result<Guid>> SaveRefinedTripPlanAsync(Guid tripId, SaveTripPlanRequest updatedPlanDto, Guid userId, CancellationToken ct = default);
    Task<Result> UpdateTripPlanAsync(Guid tripId, SaveTripPlanRequest updatedPlanDto, Guid userId, CancellationToken ct = default);
    Task<Result> DeleteTripPlanAsync(Guid tripId, Guid userId, CancellationToken ct = default);
    Task<Result<TripPlanResponse>> GetTripPlanAsync(Guid id, Guid userId);
    Task<Result<IEnumerable<TripPlanResponse>>> GetTripPlansAsync(Guid userId);
    Task<Result<ShareTripResponse>> ShareTripPlanAsync(Guid tripId, Guid userId, CancellationToken ct = default);
    Task<Result<TripPlanResponse>> GetSharedTripPlanAsync(string token, CancellationToken ct = default);
    Task<Result<Guid>> CloneTripPlanAsync(string token, Guid userId, CancellationToken ct = default);
}
