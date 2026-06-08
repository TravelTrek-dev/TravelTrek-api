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
        private readonly ITripGenerationService _generationService;
        private readonly ITripPlanCrudService _crudService;
        private readonly ITripSharingService _sharingService;
        private readonly ITripExpenseService _expenseService;

        public TripPlanController(ITripGenerationService generationService, ITripPlanCrudService crudService, ITripSharingService sharingService, ITripExpenseService expenseService)
        {
            _generationService = generationService;
            _crudService = crudService;
            _sharingService = sharingService;
            _expenseService = expenseService;
        }
        
        
        /// <summary>
        /// Generates a new AI travel plan based on user preferences.
        /// </summary>
        [Authorize]
        [EnableRateLimiting("trip-generate")]
        [HttpPost("generate", Name = "GenerateTripPlan")]
        public async Task<IActionResult> GenerateTripPlan([FromBody] TripPlanRequest request, CancellationToken ct)
        {
            var result = await _generationService.GenerateTripPlanAsync(request, ct);
            return ToActionResult(result);
        }

        /// <summary>
        /// Saves a newly generated trip plan to the user's account.
        /// </summary>
        [Authorize]
        [EnableRateLimiting("trip-db")]
        [HttpPost("save-created", Name = "SaveCreatedTripPlan")]
        public async Task<IActionResult> SaveCreatedTripPlan([FromBody] SaveTripPlanRequest request, CancellationToken ct)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return ToActionResult(Result.Failure(Error.Forbidden("SaveTripPlan.Unauthorized", "Unauthorized Request")));

            var result = await _crudService.SaveCreatedTripPlanAsync(request, userId, ct);
            return ToCreatedResult(result);
        }

        /// <summary>
        /// Updates an existing trip plan's details.
        /// </summary>
        [Authorize]
        [EnableRateLimiting("trip-db")]
        [HttpPut("{id}", Name = "UpdateTripPlan")]
        public async Task<IActionResult> UpdateTripPlan(Guid id, [FromBody] SaveTripPlanRequest request, CancellationToken ct)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return ToActionResult(Result.Failure(Error.Forbidden("UpdateTripPlan.Unauthorized", "Unauthorized Request")));

            var result = await _crudService.UpdateTripPlanAsync(id, request, userId, ct);
            return ToActionResult(result);
        }

        /// <summary>
        /// Deletes a specific trip plan by ID.
        /// </summary>
        [Authorize]
        [EnableRateLimiting("trip-db")]
        [HttpDelete("{id}", Name = "DeleteTripPlan")]
        public async Task<IActionResult> DeleteTripPlan(Guid id, CancellationToken ct)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return ToActionResult(Result.Failure(Error.Forbidden("DeleteTripPlan.Unauthorized", "Unauthorized Request")));

            var result = await _crudService.DeleteTripPlanAsync(id, userId, ct);
            return ToActionResult(result);
        }

        /// <summary>
        /// Retrieves a specific trip plan by ID.
        /// </summary>
        [Authorize]
        [EnableRateLimiting("trip-db")]
        [HttpGet("{id}", Name = "GetTripPlan")]
        public async Task<IActionResult> GetTripPlan(Guid id)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return ToActionResult(Result.Failure(Error.Forbidden("GetTripPlan.Unauthorized", "Unauthorized Request")));

            var result = await _crudService.GetTripPlanAsync(id, userId);
            return ToActionResult(result);
        }

        /// <summary>
        /// Retrieves all trip plans belonging to the authenticated user.
        /// </summary>
        [Authorize]
        [EnableRateLimiting("trip-db")]
        [HttpGet(Name = "GetTripPlans")]
        public async Task<IActionResult> GetTripPlans()
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return ToActionResult(Result.Failure(Error.Forbidden("GetTripPlans.Unauthorized", "Unauthorized Request")));

            var result = await _crudService.GetTripPlansAsync(userId);
            return ToActionResult(result);
        }

        /// <summary>
        /// Refines an existing trip plan using AI based on a new prompt or instruction.
        /// </summary>
        [Authorize]
        [EnableRateLimiting("trip-generate")]
        [HttpPost("refine/{id}", Name = "RefinePlan")]
        public async Task<IActionResult> RefinePlan(Guid id, [FromBody]RefinePlanRequest request, CancellationToken ct)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return ToActionResult(Result.Failure(Error.Forbidden("RefinePlan.Unauthorized", "Unauthorized Request")));

            var result = await _generationService.RefinePlanAsync(request, id, userId, ct);
            return ToActionResult(result);
        }

        /// <summary>
        /// Retrieves all prompts made by the authenticated user.
        /// </summary>
        [Authorize]
        [EnableRateLimiting("trip-db")]
        [HttpGet("prompts", Name = "GetUserPrompts")]
        public async Task<IActionResult> GetUserPrompts()
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return ToActionResult(Result.Failure(Error.Forbidden("GetUserPrompts.Unauthorized", "Unauthorized Request")));

            var result = await _crudService.GetUserPromptsAsync(userId);
            return ToActionResult(result);
        }

        /// <summary>
        /// Saves a refined version of an existing trip plan.
        /// </summary>
        [Authorize]
        [EnableRateLimiting("trip-db")]
        [HttpPost("save-refined/{id}", Name = "SaveRefinedTripPlan")]
        public async Task<IActionResult> SaveRefinedTripPlan(Guid id, [FromBody] SaveTripPlanRequest request, CancellationToken ct)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return ToActionResult(Result.Failure(Error.Forbidden("SaveRefinePlan.Unauthorized", "Unauthorized Request")));

            var result = await _crudService.SaveRefinedTripPlanAsync(id, request, userId, ct);
            return ToCreatedResult(result);
        }

        /// <summary>
        /// Generates a public shareable token for a trip plan.
        /// </summary>
        [Authorize]
        [EnableRateLimiting("trip-db")]
        [HttpPost("{id}/share", Name = "ShareTripPlan")]
        public async Task<IActionResult> ShareTripPlan(Guid id, CancellationToken ct)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return ToActionResult(Result.Failure(Error.Forbidden("ShareTripPlan.Unauthorized", "Unauthorized Request")));

            var result = await _sharingService.ShareTripPlanAsync(id, userId, ct);
            return ToActionResult(result);
        }

        /// <summary>
        /// Retrieves a public/shared trip plan using a shareable token.
        /// </summary>
        [EnableRateLimiting("trip-db")]
        [HttpGet("shared/{token}", Name = "GetSharedTripPlan")]
        public async Task<IActionResult> GetSharedTripPlan(string token, CancellationToken ct)
        {
            var result = await _sharingService.GetSharedTripPlanAsync(token, ct);
            return ToActionResult(result);
        }

        /// <summary>
        /// Clones a shared trip plan into the authenticated user's account.
        /// </summary>
        [Authorize]
        [EnableRateLimiting("trip-db")]
        [HttpPost("clone/{token}", Name = "CloneTripPlan")]
        public async Task<IActionResult> CloneTripPlan(string token, CancellationToken ct)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return ToActionResult(Result.Failure(Error.Forbidden("CloneTrip.Unauthorized", "Unauthorized Request")));

            var result = await _sharingService.CloneTripPlanAsync(token, userId, ct);
            return ToCreatedResult(result);
        }

        /// <summary>
        /// Adds a new expense entry to a specific trip plan.
        /// </summary>
        [Authorize]
        [HttpPost("expense/{tripId}", Name = "AddTripExpense")]
        public async Task<IActionResult> AddTripExpense([FromBody] CreateExpenseDto request, Guid tripId, CancellationToken ct)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return ToActionResult(Result.Failure(Error.Forbidden("AddTripExpense.Unauthorized", "Unauthorized Request")));

            var result = await _expenseService.AddTripExpenseAsync(request, tripId, userId, ct);
            return ToCreatedResult(result);
        }

        /// <summary>
        /// Retrieves all expenses associated with a specific trip plan.
        /// </summary>
        [Authorize]
        [HttpGet("expense/{tripId}", Name = "GetTripExpenses")]
        public async Task<IActionResult> GetTripExpenses(Guid tripId, CancellationToken ct)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return ToActionResult(Result.Failure(Error.Forbidden("GetTripExpenses.Unauthorized", "Unauthorized Request")));

            var result = await _expenseService.GetTripExpensesAsync(tripId, userId, ct);
            return ToActionResult(result);
        }

        /// <summary>
        /// Updates details of an existing trip expense.
        /// </summary>
        [Authorize]
        [HttpPut("expense/{id}", Name = "UpdateTripExpense")]
        public async Task<IActionResult> UpdateTripExpense([FromBody] EditExpenseDto request, Guid id, CancellationToken ct)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return ToActionResult(Result.Failure(Error.Forbidden("UpdateTripExpense.Unauthorized", "Unauthorized Request")));

            var result = await _expenseService.EditTripExpenseAsync(request, id, userId, ct);
            return ToActionResult(result);
        }
        
        /// <summary>
        /// Deletes a specific trip expense by ID.
        /// </summary>
        [Authorize]
        [HttpDelete("expense/{id}", Name = "DeleteTripExpense")]
        public async Task<IActionResult> DeleteTripExpense(Guid id, CancellationToken ct)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return ToActionResult(Result.Failure(Error.Forbidden("UpdateTripExpense.Unauthorized", "Unauthorized Request")));

            var result = await _expenseService.DeleteTripExpenseAsync(id, userId, ct);
            return ToActionResult(result);
        }
    }
}
