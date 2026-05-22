using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
        [HttpPost("generate")]
        public async Task<IActionResult> GenerateTripPlan([FromBody] TripPlanRequest request, CancellationToken ct)
        {
            var result = await _tripPlanService.GenerateTripPlanAsync(request, ct);
            return ToActionResult(result);
        }

        [Authorize]
        [HttpPost("save-created")]
        public async Task<IActionResult> SaveCreatedTripPlan([FromBody] SaveTripPlanRequest request, CancellationToken ct)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return ToActionResult(Result.Failure(Error.Forbidden("SaveTripPlan.Unauthorized", "Unauthorized Request")));

            var result = await _tripPlanService.SaveCreatedTripPlanAsync(request, userId, ct);
            return ToCreatedResult(result);
        }

        [Authorize]
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateTripPlan(Guid id, [FromBody] SaveTripPlanRequest request, CancellationToken ct)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return ToActionResult(Result.Failure(Error.Forbidden("UpdateTripPlan.Unauthorized", "Unauthorized Request")));

            var result = await _tripPlanService.UpdateTripPlanAsync(id, request, userId, ct);
            return ToActionResult(result);
        }

        [Authorize]
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteTripPlan(Guid id, CancellationToken ct)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return ToActionResult(Result.Failure(Error.Forbidden("DeleteTripPlan.Unauthorized", "Unauthorized Request")));

            var result = await _tripPlanService.DeleteTripPlanAsync(id, userId, ct);
            return ToActionResult(result);
        }

        [Authorize]
        [HttpGet("{id}")]
        public async Task<IActionResult> GetTripPlan(Guid id)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return ToActionResult(Result.Failure(Error.Forbidden("GetTripPlan.Unauthorized", "Unauthorized Request")));

            var result = await _tripPlanService.GetTripPlanAsync(id, userId);
            return ToActionResult(result);
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> GetTripPlans()
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return ToActionResult(Result.Failure(Error.Forbidden("GetTripPlans.Unauthorized", "Unauthorized Request")));

            var result = await _tripPlanService.GetTripPlansAsync(userId);
            return ToActionResult(result);
        }

        [Authorize]
        [HttpPost("refine/{id}")]
        public async Task<IActionResult> RefinePlan(Guid id, [FromBody]RefinePlanRequest request, CancellationToken ct)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return ToActionResult(Result.Failure(Error.Forbidden("RefinePlan.Unauthorized", "Unauthorized Request")));

            var result = await _tripPlanService.RefinePlanAsync(request, id, userId, ct);
            return ToActionResult(result);
        }

        [Authorize]
        [HttpPost("save-refined/{id}")]
        public async Task<IActionResult> SaveRefinedTripPlan(Guid id, [FromBody] SaveTripPlanRequest request, CancellationToken ct)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return ToActionResult(Result.Failure(Error.Forbidden("SaveRefinePlan.Unauthorized", "Unauthorized Request")));

            var result = await _tripPlanService.SaveRefinedTripPlanAsync(id, request, userId, ct);
            return ToCreatedResult(result);
        }
    }
}
