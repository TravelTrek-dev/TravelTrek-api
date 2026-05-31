using Microsoft.EntityFrameworkCore;
using TravelTrek.Domain.Entities.Trip;
using TravelTrek.Domain.Interfaces;
using TravelTrek.Infrastructure.Data;

namespace TravelTrek.Infrastructure.Repositories.Trip
{
    public class TripPlanRepository : GenericRepository<TripPlan>, ITripPlanRepository
    {
        public TripPlanRepository(AppDbContext context) : base(context)
        {
        }

        public async Task<TripPlan?> GetTripPlanWithDetailsAsync(Guid tripPlanId)
        {
            return await _dbSet.Include(t => t.Days).ThenInclude(d => d.Activities).FirstOrDefaultAsync(t => t.Id == tripPlanId);
        }

        public async Task<IEnumerable<TripPlan>> GetUserTripPlansAsync(Guid userId)
        {
            return await _dbSet.Where(t => t.UserId == userId).Include(t => t.Days).ThenInclude(d => d.Activities).ToListAsync();
        }
        
        
        
        public async Task<bool> Exists(Guid id)
        {
            return await _dbSet.AnyAsync(e => e.Id == id);
        }
    }
}
