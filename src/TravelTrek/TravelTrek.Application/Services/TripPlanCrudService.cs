using AutoMapper;
using TravelTrek.Application.DTOs.TripPlanner;
using TravelTrek.Application.Interfaces;
using TravelTrek.Domain.Common;
using TravelTrek.Domain.Entities.Trip;
using TravelTrek.Domain.Interfaces;

namespace TravelTrek.Application.Services;

public class TripPlanCrudService : ITripPlanCrudService
{
    private readonly ITripPlanRepository _tripPlanRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public TripPlanCrudService(ITripPlanRepository tripPlanRepository, IUnitOfWork unitOfWork, IMapper mapper)
    {
        _tripPlanRepository = tripPlanRepository;
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<Result<Guid>> SaveCreatedTripPlanAsync(SaveTripPlanRequest planDto, Guid userId, CancellationToken ct = default)
    {
        var tripPlan = _mapper.Map<TripPlan>(planDto);
        tripPlan.UserId = userId;
        await _tripPlanRepository.AddAsync(tripPlan);
        await _unitOfWork.SaveChangesAsync();

        return Result.Success(tripPlan.Id);
    }

    public async Task<Result<Guid>> SaveRefinedTripPlanAsync(Guid tripId, SaveTripPlanRequest updatedPlanDto, Guid userId, CancellationToken ct = default)
    {
        var existingTrip = await _tripPlanRepository.GetTripPlanWithDetailsAsync(tripId);
        if (existingTrip == null)
        {
            return Result.Failure<Guid>(Error.NotFound("SaveRefinedPlan.NotFound", "The trip could not be found."));
        }

        if (existingTrip.UserId != userId)
        {
            return Result.Failure<Guid>(Error.Forbidden("SaveRefinedPlan.Forbidden", "You do not have permission to edit this trip."));
        }

        _mapper.Map(updatedPlanDto, existingTrip);
        
        _tripPlanRepository.Update(existingTrip);
        await _unitOfWork.SaveChangesAsync();

        return Result.Success(existingTrip.Id);
    }

    public async Task<Result> UpdateTripPlanAsync(Guid tripId, SaveTripPlanRequest updatedPlanDto, Guid userId, CancellationToken ct = default)
    {
        var existingTrip = await _tripPlanRepository.GetTripPlanWithDetailsAsync(tripId);
        if (existingTrip == null)
        {
            return Result.Failure(Error.NotFound("TripPlan.NotFound", "The trip could not be found."));
        }

        if (existingTrip.UserId != userId)
        {
            return Result.Failure(Error.Forbidden("TripPlan.Forbidden", "You do not have permission to edit this trip."));
        }

        _mapper.Map(updatedPlanDto, existingTrip);

        _tripPlanRepository.Update(existingTrip);
        await _unitOfWork.SaveChangesAsync();

        return Result.Success();
    }

    public async Task<Result> DeleteTripPlanAsync(Guid tripId, Guid userId, CancellationToken ct = default)
    {
        var trip = await _tripPlanRepository.GetByIdAsync(tripId);
        if (trip == null)
        {
            return Result.Failure(Error.NotFound("TripPlan.NotFound", "The trip could not be found."));
        }

        if (trip.UserId != userId)
        {
            return Result.Failure(Error.Forbidden("TripPlan.Forbidden", "You do not have permission to delete this trip."));
        }

        _tripPlanRepository.Delete(trip);
        await _unitOfWork.SaveChangesAsync();

        return Result.Success();
    }

    public async Task<Result<TripPlanResponse>> GetTripPlanAsync(Guid id, Guid userId)
    {
        var tripPlan = await _tripPlanRepository.GetTripPlanWithDetailsAsync(id);
        if (tripPlan == null)
        {
            return Result.Failure<TripPlanResponse>(Error.NotFound("GetTripPlan.NotFound", "Trip Plan Not Found"));
        }

        if (tripPlan.UserId != userId)
        {
            return Result.Failure<TripPlanResponse>(Error.Forbidden("GetTripPlan.Forbidden", "Forbidden Request"));
        }

        var tripPlanDto = _mapper.Map<TripPlanResponse>(tripPlan);
        return Result.Success(tripPlanDto);
    }

    public async Task<Result<IEnumerable<TripPlanResponse>>> GetTripPlansAsync(Guid userId)
    {
        var tripPlans = await _tripPlanRepository.GetUserTripPlansAsync(userId);

        var tripPlansDto = _mapper.Map<IEnumerable<TripPlanResponse>>(tripPlans);
        return Result.Success(tripPlansDto);
    }

    public async Task<Result<UserPromptsResponse>> GetUserPromptsAsync(Guid userId)
    {
        var prompts = await _tripPlanRepository.GetUserPromptsAsync(userId);

        var response = new UserPromptsResponse
        {
            UserId = userId,
            Prompts = prompts.Select(p => new PromptItem
            {
                TripId = p.TripId,
                Prompt = p.Prompt
            }).ToList()
        };

        return Result.Success(response);
    }
}
