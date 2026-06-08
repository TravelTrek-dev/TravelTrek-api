using TravelTrek.Domain.Entities.Trip;

namespace TravelTrek.Domain.Interfaces
{
    public interface ITripPlanRepository : IGenericRepository<TripPlan>
    {
        Task<TripPlan?> GetTripPlanWithDetailsAsync(Guid tripPlanId);
        Task<IEnumerable<TripPlan>> GetUserTripPlansAsync(Guid userId);
        Task<List<(Guid TripId, string Prompt)>> GetUserPromptsAsync(Guid userId);
        Task<bool> Exists(Guid id);
    }
}
