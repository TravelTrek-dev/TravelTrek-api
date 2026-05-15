using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TravelTrek.Application.DTOs.TripPlanner;
using TravelTrek.Application.Interfaces;
using TravelTrek.Domain.Common;
using TravelTrek.Domain.Entities.Trip;
using TravelTrek.Domain.Interfaces;

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
        [HttpPost("save")]
        public async Task<IActionResult> SaveTripPlan([FromBody] TripPlanResponse planDto, CancellationToken ct)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return ToActionResult(Result.Failure(Error.Forbidden("SaveTripPlan.Unauthorized", "Unauthorized Request")));

            var result = await _tripPlanService.SaveTripPlanAsync(planDto, userId, ct);
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
    }
}
