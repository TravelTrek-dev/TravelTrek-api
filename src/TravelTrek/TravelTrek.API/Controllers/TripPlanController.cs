using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TravelTrek.Application.DTOs.TripPlanner;
using TravelTrek.Application.Interfaces;
using TravelTrek.Domain.Common;

namespace TravelTrek.API.Controllers
{
    [Route("api/trip-plan")]
    public class TripPlanController : ApiBaseController
    {
        private readonly ITripPlanService _tripPlanService;

        public TripPlanController(ITripPlanService tripPlanService)
        {
            _tripPlanService = tripPlanService;
        }
        
        
        [Authorize]
        [EnableRateLimiting("trip-generate")]
        [HttpPost("generate")]
        public async Task<IActionResult> GenerateTripPlan([FromBody] TripPlanRequest request, CancellationToken ct)
        {
            var result = await _tripPlanService.GenerateTripPlanAsync(request, ct);
            return ToActionResult(result);
        }

        [Authorize]
        [EnableRateLimiting("trip-db")]
        [HttpPost("save-created")]
        public async Task<IActionResult> SaveCreatedTripPlan([FromBody] SaveTripPlanRequest request, CancellationToken ct)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return ToActionResult(Result.Failure(Error.Forbidden("SaveTripPlan.Unauthorized", "Unauthorized Request")));

            var result = await _tripPlanService.SaveCreatedTripPlanAsync(request, userId, ct);
            return ToCreatedResult(result);
        }

        [Authorize]
        [EnableRateLimiting("trip-db")]
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateTripPlan(Guid id, [FromBody] SaveTripPlanRequest request, CancellationToken ct)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return ToActionResult(Result.Failure(Error.Forbidden("UpdateTripPlan.Unauthorized", "Unauthorized Request")));

            var result = await _tripPlanService.UpdateTripPlanAsync(id, request, userId, ct);
            return ToActionResult(result);
        }

        [Authorize]
        [EnableRateLimiting("trip-db")]
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteTripPlan(Guid id, CancellationToken ct)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return ToActionResult(Result.Failure(Error.Forbidden("DeleteTripPlan.Unauthorized", "Unauthorized Request")));

            var result = await _tripPlanService.DeleteTripPlanAsync(id, userId, ct);
            return ToActionResult(result);
        }

        [Authorize]
        [EnableRateLimiting("trip-db")]
        [HttpGet("{id}")]
        public async Task<IActionResult> GetTripPlan(Guid id)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return ToActionResult(Result.Failure(Error.Forbidden("GetTripPlan.Unauthorized", "Unauthorized Request")));

            var result = await _tripPlanService.GetTripPlanAsync(id, userId);
            return ToActionResult(result);
        }

        [Authorize]
        [EnableRateLimiting("trip-db")]
        [HttpGet]
        public async Task<IActionResult> GetTripPlans()
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return ToActionResult(Result.Failure(Error.Forbidden("GetTripPlans.Unauthorized", "Unauthorized Request")));

            var result = await _tripPlanService.GetTripPlansAsync(userId);
            return ToActionResult(result);
        }

        [Authorize]
        [EnableRateLimiting("trip-generate")]
        [HttpPost("refine/{id}")]
        public async Task<IActionResult> RefinePlan(Guid id, [FromBody]RefinePlanRequest request, CancellationToken ct)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return ToActionResult(Result.Failure(Error.Forbidden("RefinePlan.Unauthorized", "Unauthorized Request")));

            var result = await _tripPlanService.RefinePlanAsync(request, id, userId, ct);
            return ToActionResult(result);
        }

        [Authorize]
        [EnableRateLimiting("trip-db")]
        [HttpPost("save-refined/{id}")]
        public async Task<IActionResult> SaveRefinedTripPlan(Guid id, [FromBody] SaveTripPlanRequest request, CancellationToken ct)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return ToActionResult(Result.Failure(Error.Forbidden("SaveRefinePlan.Unauthorized", "Unauthorized Request")));

            var result = await _tripPlanService.SaveRefinedTripPlanAsync(id, request, userId, ct);
            return ToCreatedResult(result);
        }

        [Authorize]
        [EnableRateLimiting("trip-db")]
        [HttpPost("{id}/share")]
        public async Task<IActionResult> ShareTripPlan(Guid id, CancellationToken ct)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return ToActionResult(Result.Failure(Error.Forbidden("ShareTripPlan.Unauthorized", "Unauthorized Request")));

            var result = await _tripPlanService.ShareTripPlanAsync(id, userId, ct);
            return ToActionResult(result);
        }

        [EnableRateLimiting("trip-db")]
        [HttpGet("shared/{token}")]
        public async Task<IActionResult> GetSharedTripPlan(string token, CancellationToken ct)
        {
            var result = await _tripPlanService.GetSharedTripPlanAsync(token, ct);
            return ToActionResult(result);
        }

        [Authorize]
        [EnableRateLimiting("trip-db")]
        [HttpPost("clone/{token}")]
        public async Task<IActionResult> CloneTripPlan(string token, CancellationToken ct)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return ToActionResult(Result.Failure(Error.Forbidden("CloneTrip.Unauthorized", "Unauthorized Request")));

            var result = await _tripPlanService.CloneTripPlanAsync(token, userId, ct);
            return ToCreatedResult(result);
        }

        [Authorize]
        [HttpPost("expense/{tripId}")]
        public async Task<IActionResult> AddTripExpense([FromBody] CreateExpenseDto request, Guid tripId, CancellationToken ct)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return ToActionResult(Result.Failure(Error.Forbidden("AddTripExpense.Unauthorized", "Unauthorized Request")));

            var result = await _tripPlanService.AddTripExpenseAsync(request, tripId, userId, ct);
            return ToCreatedResult(result);
        }

        [Authorize]
        [HttpGet("expense/{tripId}")]
        public async Task<IActionResult> GetTripExpenses(Guid tripId, CancellationToken ct)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return ToActionResult(Result.Failure(Error.Forbidden("GetTripExpenses.Unauthorized", "Unauthorized Request")));

            var result = await _tripPlanService.GetTripExpensesAsync(tripId, userId, ct);
            return ToActionResult(result);
        }

        [Authorize]
        [HttpPut("expense/{id}")]
        public async Task<IActionResult> UpdateTripExpense([FromBody] EditExpenseDto request, Guid id, CancellationToken ct)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return ToActionResult(Result.Failure(Error.Forbidden("UpdateTripExpense.Unauthorized", "Unauthorized Request")));

            var result = await _tripPlanService.EditTripExpenseAsync(request, id, userId, ct);
            return ToActionResult(result);
        }
        
        [Authorize]
        [HttpDelete("expense/{id}")]
        public async Task<IActionResult> DeleteTripExpense(Guid id, CancellationToken ct)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return ToActionResult(Result.Failure(Error.Forbidden("UpdateTripExpense.Unauthorized", "Unauthorized Request")));

            var result = await _tripPlanService.DeleteTripExpenseAsync(id, userId, ct);
            return ToActionResult(result);
        }
    }
}
