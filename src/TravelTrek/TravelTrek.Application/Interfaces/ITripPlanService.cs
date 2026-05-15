using TravelTrek.Application.DTOs.TripPlanner;
using TravelTrek.Domain.Common;

namespace TravelTrek.Application.Interfaces;

public interface ITripPlanService
{
    /// <summary>
    /// Orchestrates the full trip planning pipeline:
    /// 1. NER extraction from the user prompt
    /// 2. OSM POI fetching for the destination city
    /// 3. Weather forecast retrieval
    /// 4. LLM-based itinerary generation via Ollama
    /// </summary>
    Task<Result<TripPlanResponse>> GenerateTripPlanAsync(TripPlanRequest request, CancellationToken ct = default);
    Task<Result<Guid>> SaveTripPlanAsync(TripPlanResponse planDto, Guid userId, CancellationToken ct = default);
}
