using System.Security.Cryptography;
using AutoMapper;
using Microsoft.Extensions.Logging;
using TravelTrek.Application.DTOs.TripPlanner;
using TravelTrek.Application.Interfaces;
using TravelTrek.Domain.Common;
using TravelTrek.Domain.Entities.Trip;
using TravelTrek.Domain.Interfaces;

namespace TravelTrek.Application.Services;

public class TripSharingService : ITripSharingService
{
    private readonly ITripPlanRepository _tripPlanRepository;
    private readonly IGenericRepository<SharedTripToken> _sharedTokenRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly ILogger<TripSharingService> _logger;

    public TripSharingService(ITripPlanRepository tripPlanRepository, IGenericRepository<SharedTripToken> sharedTokenRepository, IUnitOfWork unitOfWork, IMapper mapper, ILogger<TripSharingService> logger)
    {
        _tripPlanRepository = tripPlanRepository;
        _sharedTokenRepository = sharedTokenRepository;
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<Result<ShareTripResponse>> ShareTripPlanAsync(Guid tripId, Guid userId, CancellationToken ct = default)
    {
        var trip = await _tripPlanRepository.GetByIdAsync(tripId);
        if (trip == null)
            return Result.Failure<ShareTripResponse>(Error.NotFound("TripPlan.NotFound", "The trip could not be found."));

        if (trip.UserId != userId)
            return Result.Failure<ShareTripResponse>(Error.Forbidden("TripPlan.Forbidden", "You do not have permission to share this trip."));

        // Check if an active share token already exists
        var existingToken = await _sharedTokenRepository.FindFirstOrDefaultAsync(
            t => t.TripPlanId == tripId && !t.IsRevoked && (t.ExpiresAt == null || t.ExpiresAt > DateTime.UtcNow));

        if (existingToken != null)
        {
            return Result.Success(new ShareTripResponse(existingToken.Token, existingToken.ExpiresAt));
        }

        // Generate a new cryptographic URL-safe token
        var tokenBytes = RandomNumberGenerator.GetBytes(16);
        var token = Convert.ToBase64String(tokenBytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');

        var sharedToken = new SharedTripToken
        {
            Id = Guid.NewGuid(),
            TripPlanId = tripId,
            Token = token,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = null // Never expires by default
        };

        await _sharedTokenRepository.AddAsync(sharedToken);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("Share token generated for TripPlan {TripPlanId} by User {UserId}.", tripId, userId);

        return Result.Success(new ShareTripResponse(sharedToken.Token, sharedToken.ExpiresAt));
    }

    public async Task<Result<TripPlanResponse>> GetSharedTripPlanAsync(string token, CancellationToken ct = default)
    {
        var sharedToken = await _sharedTokenRepository.FindFirstOrDefaultAsync(
            t => t.Token == token);

        if (sharedToken == null)
            return Result.Failure<TripPlanResponse>(Error.NotFound("SharedTrip.NotFound", "Shared trip not found."));

        if (!sharedToken.IsActive)
            return Result.Failure<TripPlanResponse>(Error.Forbidden("SharedTrip.Inactive", "This share link is no longer active."));

        var tripPlan = await _tripPlanRepository.GetTripPlanWithDetailsAsync(sharedToken.TripPlanId);
        if (tripPlan == null)
            return Result.Failure<TripPlanResponse>(Error.NotFound("SharedTrip.TripNotFound", "The shared trip no longer exists."));

        var tripPlanDto = _mapper.Map<TripPlanResponse>(tripPlan);
        return Result.Success(tripPlanDto);
    }

    public async Task<Result<Guid>> CloneTripPlanAsync(string token, Guid userId, CancellationToken ct = default)
    {
        var sharedToken = await _sharedTokenRepository.FindFirstOrDefaultAsync(
            t => t.Token == token);

        if (sharedToken == null)
            return Result.Failure<Guid>(Error.NotFound("CloneTrip.NotFound", "Shared trip not found."));

        if (!sharedToken.IsActive)
            return Result.Failure<Guid>(Error.Forbidden("CloneTrip.Inactive", "This share link is no longer active."));

        var sourcePlan = await _tripPlanRepository.GetTripPlanWithDetailsAsync(sharedToken.TripPlanId);
        if (sourcePlan == null)
            return Result.Failure<Guid>(Error.NotFound("CloneTrip.TripNotFound", "The shared trip no longer exists."));

        // Deep copy — create a completely independent TripPlan for the new user
        var clonedPlan = new TripPlan
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Prompt = sourcePlan.Prompt,
            City = sourcePlan.City,
            Country = sourcePlan.Country,
            Duration = sourcePlan.Duration,
            Budget = sourcePlan.Budget,
            Currency = sourcePlan.Currency,
            GroupSize = sourcePlan.GroupSize,
            ImageUrl = sourcePlan.ImageUrl,
            GeneralAdvice = sourcePlan.GeneralAdvice,
            PackingTips = sourcePlan.PackingTips != null ? new List<string>(sourcePlan.PackingTips) : null,
            Weather = sourcePlan.Weather != null
                ? new WeatherSummary
                {
                    AvgTempCelsius = sourcePlan.Weather.AvgTempCelsius,
                    Condition = sourcePlan.Weather.Condition,
                    AvgHumidity = sourcePlan.Weather.AvgHumidity,
                    AvgWindSpeed = sourcePlan.Weather.AvgWindSpeed
                }
                : null,
            Days = sourcePlan.Days.Select(day => new DayPlan
            {
                Id = Guid.NewGuid(),
                DayNumber = day.DayNumber,
                Activities = day.Activities.Select(a => new Activity
                {
                    Id = Guid.NewGuid(),
                    Name = a.Name,
                    City = a.City,
                    Description = a.Description,
                    GoogleMapsLink = a.GoogleMapsLink,
                    Website = a.Website,
                    ImageUrl = a.ImageUrl
                }).ToList()
            }).ToList()
        };

        await _tripPlanRepository.AddAsync(clonedPlan);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("TripPlan {SourceId} cloned to {ClonedId} by User {UserId} via token.", sourcePlan.Id, clonedPlan.Id, userId);

        return Result.Success(clonedPlan.Id);
    }
}
