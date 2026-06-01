using TravelTrek.Application.DTOs.TripPlanner;
using TravelTrek.Domain.Common;

namespace TravelTrek.Application.Interfaces;

public interface ITripSharingService
{
    Task<Result<ShareTripResponse>> ShareTripPlanAsync(Guid tripId, Guid userId, CancellationToken ct = default);
    Task<Result<TripPlanResponse>> GetSharedTripPlanAsync(string token, CancellationToken ct = default);
    Task<Result<Guid>> CloneTripPlanAsync(string token, Guid userId, CancellationToken ct = default);
}
