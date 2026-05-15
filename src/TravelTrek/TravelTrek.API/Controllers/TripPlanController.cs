using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TravelTrek.Application.DTOs.TripPlanner;
using TravelTrek.Application.Interfaces;
using TravelTrek.Domain.Entities.Trip;
using TravelTrek.Domain.Interfaces;

namespace TravelTrek.API.Controllers
{
    [Route("api/trip-plan")]
    public class TripPlanController : ApiBaseController
    {
        private readonly ITripPlanService _tripPlanService;
        private readonly ITripPlanRepository _tripPlanRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;

        public TripPlanController(
            ITripPlanService tripPlanService, 
            ITripPlanRepository tripPlanRepository, 
            IUnitOfWork unitOfWork,
            IMapper mapper)
        {
            _tripPlanService = tripPlanService;
            _tripPlanRepository = tripPlanRepository;
            _unitOfWork = unitOfWork;
            _mapper = mapper;
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
            if (userId == Guid.Empty) return Unauthorized();

            var result = await _tripPlanService.SaveTripPlanAsync(planDto, userId, ct);
            return ToActionResult(result);
        }
    }
}
