using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoMapper;
using Microsoft.Extensions.Logging;
using TravelTrek.Application.DTOs.Ner;
using TravelTrek.Application.DTOs.Osm;
using TravelTrek.Application.DTOs.TripPlanner;
using TravelTrek.Application.DTOs.Weather;
using TravelTrek.Application.Interfaces;
using TravelTrek.Domain.Common;
using TravelTrek.Domain.Entities.Trip;
using TravelTrek.Domain.Interfaces;

namespace TravelTrek.Application.Services;

public class TripPlanService : ITripPlanService
{
    private readonly INerService _nerService;
    private readonly IOsmService _osmService;
    private readonly IOpenWeatherService _weatherService;
    private readonly ILLMService _illmService;
    private readonly ILogger<TripPlanService> _logger;
    private readonly ITripPlanRepository _tripPlanRepository;
    private readonly IExpenseRepository _expenseRepository;
    private readonly IGenericRepository<SharedTripToken> _sharedTokenRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    
    private const int MinPoisPerDay = 3;
    private const int DefaultPoiLimit = 12;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public TripPlanService(INerService nerService, IOsmService osmService, IOpenWeatherService weatherService, ILLMService illmService, ILogger<TripPlanService> logger, ITripPlanRepository tripPlanRepository, IExpenseRepository expenseRepository, IGenericRepository<SharedTripToken> sharedTokenRepository, IUnitOfWork unitOfWork, IMapper mapper)
    {
        _nerService = nerService;
        _osmService = osmService;
        _weatherService = weatherService;
        _illmService = illmService;
        _logger = logger;
        _tripPlanRepository = tripPlanRepository;
        _expenseRepository = expenseRepository;
        _sharedTokenRepository = sharedTokenRepository;
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<Result<TripPlanResponse>> GenerateTripPlanAsync(TripPlanRequest request, CancellationToken ct = default)
    {
        var nerRequest = new NerRequest { Inputs = request.Prompt };
        var rawEntitiesResult = await _nerService.ExtractEntitiesAsync(nerRequest, ct);

        if (rawEntitiesResult.IsFailure)
        {
            _logger.LogWarning("NER extraction failed: {Error}", rawEntitiesResult.Error);
            var promptOnlyContext = new TripContext { UserPrompt = request.Prompt, NerSucceeded = false};

            var promptOnlyLlmResult = await _illmService.GenerateAsync(BuildGeneratePlanLlmPrompt(promptOnlyContext), ct);
            if (promptOnlyLlmResult.IsFailure)
            {
                _logger.LogWarning("LLM generation failed: {Error}", promptOnlyLlmResult.Error);
                return Result.Failure<TripPlanResponse>(promptOnlyLlmResult.Error);
            }

            var promptOnlyPlan = TryParseLlmResponse(promptOnlyLlmResult.Value);
            if (promptOnlyPlan == null)
            {
                return Result.Failure<TripPlanResponse>(Error.External("TripPlan.ParseFailed", "Failed to parse the generated itinerary. Please try again."));
            }

            return Result.Success(promptOnlyPlan);   
        }

        var rawEntities = rawEntitiesResult.Value;
        var feasibilityResult = await _nerService.CheckFeasibilityAsync(rawEntities, ct);
        if (feasibilityResult.IsSuccess && !feasibilityResult.Value.IsFeasible)
        {
            _logger.LogWarning("Trip plan feasibility check failed: {Explanation}", feasibilityResult.Value.Explanation);
            return Result.Failure<TripPlanResponse>(Error.Validation("TripPlan.Infeasible", feasibilityResult.Value.Explanation));
        }

        var tripData = ParseTripData(rawEntities);
        if (tripData.Locations.Count == 0)
        {
            return Result.Failure<TripPlanResponse>(Error.Validation("TripPlan.NoCity", "Could not extract a destination city from your prompt. Please include a city name."));
        }

        _logger.LogInformation("NER extracted — City: {cities}, Duration: {Duration}, Budget: {Budget}, GroupSize: {GroupSize}",
            tripData.Locations, tripData.Durations.FirstOrDefault() ?? "N/A", tripData.Budgets.FirstOrDefault() ?? "N/A", tripData.GroupSizes.FirstOrDefault() ?? "N/A");
        
        
        var osmTask = tripData.Locations.Select(city => _osmService.GetTopAttractionsAsync(city, DefaultPoiLimit, ct));
        var weatherTask = tripData.Locations.Select(city => GetWeatherForCityAsync(city, ct));
        
        var attractionsResults = await Task.WhenAll(osmTask);
        var weatherResults = await Task.WhenAll(weatherTask);
        
        var attractions = attractionsResults.Where(result => result.IsSuccess).SelectMany(result => result.Value).ToList();
        var weather = weatherResults.Where(w => w != null).ToList();

        var context = new TripContext
        {
            UserPrompt = request.Prompt,
            Cities = tripData.Locations,
            TripData = tripData,
            Attractions = attractions.Count == 0 ? null : attractions,
            Weather = weather.Count == 0 ? null : weather,
            NerSucceeded = true
        };

        var llmResult = await _illmService.GenerateAsync(BuildGeneratePlanLlmPrompt(context), ct);
        if (llmResult.IsFailure)
        {
            _logger.LogWarning("LLM generation failed: {Error}", llmResult.Error);
            return Result.Failure<TripPlanResponse>(llmResult.Error);
        }

        var plan = TryParseLlmResponse(llmResult.Value);
        if (plan == null)
        {
            return Result.Failure<TripPlanResponse>(Error.External("TripPlan.ParseFailed", "Failed to parse the generated itinerary. Please try again."));
        }

        PatchMissingFields(plan, context);
        return Result.Success(plan);
    }
    
    public async Task<Result<TripPlanResponse>> RefinePlanAsync(RefinePlanRequest request, Guid planId, Guid userId, CancellationToken ct = default)
    {
        var existingTripPlan = await _tripPlanRepository.GetTripPlanWithDetailsAsync(planId);
        if (existingTripPlan == null)
        {
            return Result.Failure<TripPlanResponse>(Error.NotFound("RefinePlan.NotFound", "Trip Plan Not Found"));
        }
            
        if (existingTripPlan.UserId != userId)
        {
            return Result.Failure<TripPlanResponse>(Error.Forbidden("RefinePlan.Forbidden", "Forbidden Request"));
        }
        var tripPlanDto = _mapper.Map<TripPlanResponse>(existingTripPlan);
        
        var tripPlanJson = JsonSerializer.Serialize(tripPlanDto);

        var llmResult = await _illmService.GenerateAsync(BuildRefinePlanLlmPrompt(tripPlanJson, request.UserPrompt), ct);
        
        if (llmResult.IsFailure)
        {
            return Result.Failure<TripPlanResponse>(llmResult.Error);
        }

        var plan = TryParseLlmResponse(llmResult.Value);
        if (plan == null)
        {
            return Result.Failure<TripPlanResponse>(Error.External("RefinePlan.ParseFailed", "Failed to parse the generated itinerary. Please try again."));
        }
        return Result.Success(plan);
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

    public async Task<Result<Guid>> SaveCreatedTripPlanAsync(SaveTripPlanRequest planDto, Guid userId, CancellationToken ct = default)
    {
        var tripPlan = _mapper.Map<TripPlan>(planDto);
        tripPlan.UserId = userId;
        await _tripPlanRepository.AddAsync(tripPlan);
        await _unitOfWork.SaveChangesAsync();

        return Result.Success(tripPlan.Id);
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
            City = sourcePlan.City,
            Country = sourcePlan.Country,
            Budget = sourcePlan.Budget,
            GroupSize = sourcePlan.GroupSize,
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
                    Website = a.Website
                }).ToList()
            }).ToList()
        };

        await _tripPlanRepository.AddAsync(clonedPlan);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("TripPlan {SourceId} cloned to {ClonedId} by User {UserId} via token.", sourcePlan.Id, clonedPlan.Id, userId);

        return Result.Success(clonedPlan.Id);
    }

    public async Task<Result<Guid>> AddTripExpenseAsync(CreateExpenseDto request, Guid tripPlanId, Guid userId, CancellationToken ct = default)
    {
        var tripPlan = await _tripPlanRepository.GetByIdAsync(tripPlanId);
        if (tripPlan == null)
        {
            return Result.Failure<Guid>(Error.NotFound("AddTripExpense.NotFound", "Trip does not exist"));
        }

        if (tripPlan.UserId != userId)
        {
            return Result.Failure<Guid>(Error.Forbidden("AddTripExpense.Forbidden", "Forbidden request"));
        }
        
        var newExpense = new Expense()
        {
            TripPlanId = tripPlanId,
            UserId = userId
        };

        _mapper.Map(request, newExpense);

        await _expenseRepository.AddAsync(newExpense);
        await _unitOfWork.SaveChangesAsync();

        return Result.Success(newExpense.Id);
    }
    
    public async Task<Result> EditTripExpenseAsync(EditExpenseDto request, Guid id, Guid userId, CancellationToken ct = default)
    {
        var expense = await _expenseRepository.GetByIdAsync(id);
        if (expense == null)
        {
            return Result.Failure(Error.NotFound("EditTripExpense.NotFound", "Expense does not exist"));
        }
        if (expense.UserId != userId)
        {
            return Result.Failure(Error.Forbidden("EditTripExpense.Forbidden", "Forbidden request"));
        }

        _mapper.Map(request, expense);

        _expenseRepository.Update(expense);
        await _unitOfWork.SaveChangesAsync();

        return Result.Success();
    }

    public async Task<Result> DeleteTripExpenseAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        var expense = await _expenseRepository.GetByIdAsync(id);
        if (expense == null)
        {
            return Result.Failure(Error.NotFound("DeleteTripExpense.NotFound", "Expense does not exist"));
        }
        if (expense.UserId != userId)
        {
            return Result.Failure(Error.Forbidden("DeleteTripExpense.Forbidden", "Forbidden request"));
        }
        
        _expenseRepository.Delete(expense);
        await _unitOfWork.SaveChangesAsync();

        return Result.Success();
    }
    
    public async Task<Result<ExpensesDto>> GetTripExpensesAsync(Guid tripPlanId, Guid userId, CancellationToken ct = default)
    {
        var tripPlan = await _tripPlanRepository.GetByIdAsync(tripPlanId);
        if (tripPlan == null)
        {
            return Result.Failure<ExpensesDto>(Error.NotFound("GetTripExpenses.NotFound", "Trip does not exist"));
        }

        if (tripPlan.UserId != userId)
        {
            return Result.Failure<ExpensesDto>(Error.Forbidden("GetTripExpenses.Forbidden", "Forbidden request"));
        }
        var expenses = await _expenseRepository.GetForTrip(tripPlanId);

        var spent = expenses.Sum(e => e.Price);
        var remaining = tripPlan.Budget - spent;
        var remainingValue = remaining ?? 0m;
        var expensesDto = new ExpensesDto()
        {
            City = tripPlan.City,
            Expenses = _mapper.Map<List<ExpenseDto>>(expenses),
            Budget = tripPlan.Budget ?? 0m,
            Spent  = spent,
            Remaining = remainingValue > 0 ? remainingValue : 0m,
            Currency = tripPlan.Currency ?? ""
        };

        return Result.Success(expensesDto);
    }

    #region Helpers

    private async Task<WeatherSummaryDto?> GetWeatherForCityAsync(string city, CancellationToken ct)
    {
        try
        {
            var geocodeResult = await _weatherService.GeocodeByNameAsync(city, ct);
            if (geocodeResult.IsFailure)
            {
                _logger.LogWarning("Failed to geocode city '{City}' for weather: {Error}", city, geocodeResult.Error);
                return null;
            }

            var coords = new WeatherRequest(geocodeResult.Value.Lat, geocodeResult.Value.Lon);

            var forecastResult = await _weatherService.GetForecastAsync(coords, ct);
            var forecast = forecastResult.Value;
            if (forecastResult.IsSuccess && forecastResult.Value.Items.Count > 0)
            {
                return new WeatherSummaryDto
                {
                    AvgTempCelsius = Math.Round(forecast.Items.Average(i => i.Main.Temp), 1),
                    Condition = forecast.Items
                        .SelectMany(i => i.Weather)
                        .GroupBy(w => w.Main)
                        .OrderByDescending(g => g.Count())
                        .First().Key,
                    AvgHumidity = Math.Round(forecast.Items.Average(i => i.Main.Humidity), 1),
                    AvgWindSpeed = Math.Round(forecast.Items.Average(i => i.Wind.Speed), 1)
                };
            }

            _logger.LogWarning("Forecast unavailable for '{City}', falling back to current weather.", city);

            var currentResult = await _weatherService.GetCurrentWeatherAsync(coords, ct);
            if (currentResult.IsFailure)
            {
                return null;
            }

            var c = currentResult.Value;
            return new WeatherSummaryDto
            {
                AvgTempCelsius = c.Main.Temp,
                Condition = c.Weather.FirstOrDefault()?.Description ?? "Unknown",
                AvgHumidity = c.Main.Humidity,
                AvgWindSpeed = c.Wind.Speed
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error fetching weather for '{City}'.", city);
            return null;
        }
    }
    
    private static ExtractedTripData ParseTripData(List<NerEntity> entities)
    {
        var data = new ExtractedTripData();
        foreach (var entity in entities)
        {
            var word = entity.Word.Trim();
            if (string.IsNullOrWhiteSpace(word)) continue;

            switch (entity.EntityGroup.ToUpperInvariant())
            {
                case "LOCATION":
                    data.Locations.Add(word);
                    break;
                case "DATE":
                    data.Dates.Add(word);
                    break;
                case "DURATION":
                    data.Durations.Add(word);
                    break;
                case "BUDGET":
                    data.Budgets.Add(word);
                    break;
                case "GROUP_SIZE":
                    data.GroupSizes.Add(word);
                    break;
                case "TRAVEL_TYPE":
                    data.TravelTypes.Add(word);
                    break;
                case "ACTIVITY":
                    data.Activities.Add(word);
                    break;
            }
        }
        return data;
    }
    
    private static string BuildGeneratePlanLlmPrompt(TripContext context)
    {
        var sb = new StringBuilder();

        sb.AppendLine("You are an expert travel planner. Generate a detailed day-by-day trip itinerary in valid JSON format.");
        sb.AppendLine();

        sb.AppendLine("=== USER REQUEST ===");
        sb.AppendLine(context.UserPrompt);
        sb.AppendLine();

        if (!context.NerSucceeded)
        {
            sb.AppendLine("No pre-extracted points of interest are available. You MUST fetch, search, and recommend top points of interest and attractions using: https://www.lonelyplanet.com/");
            sb.AppendLine("No weather data or forcast are available You MUST fetch, search for the cities forcast and weether using : https://www.meteoblue.com/");
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("=== EXTRACTED TRIP DETAILS ===");

            if (context.Cities != null)
                sb.AppendLine($"- Destination: {string.Join(", ", context.Cities)}");
            if (context.TripData != null && context.TripData.Durations.Count != 0)
                sb.AppendLine($"- Duration: {string.Join(", ", context.TripData.Durations)}");
            if (context.TripData != null && !string.IsNullOrWhiteSpace(context.TripData.Budgets.FirstOrDefault()))
                sb.AppendLine($"- Budget: {context.TripData.Budgets[0]}");
            if (context.TripData != null && !string.IsNullOrWhiteSpace(context.TripData.GroupSizes.FirstOrDefault()))
                sb.AppendLine($"- Group size: {context.TripData.GroupSizes[0]}");
            if (context.TripData != null && context.TripData.TravelTypes.Count > 0)
                sb.AppendLine($"- Travel type: {string.Join(", ", context.TripData.TravelTypes)}");
            if (context.TripData != null && context.TripData.Activities.Count > 0)
                sb.AppendLine($"- Preferred activities: {string.Join(", ", context.TripData.Activities)}");
            if (context.TripData != null && context.TripData.Dates.Count > 0)
                sb.AppendLine($"- Travel dates: {string.Join(", ", context.TripData.Dates)}");
            sb.AppendLine();

            sb.AppendLine("=== WEATHER FORECAST ===");
            if (context.Weather != null)
            {
                if (context.Cities != null)
                {
                    for (int i = 0; i < Math.Min(context.Cities.Count, context.Weather.Count); i++)
                    {
                        var w = context.Weather[i];
                        if (w != null)
                            sb.AppendLine($"- {context.Cities[i]}: {w.AvgTempCelsius}°C, {w.Condition}, Humidity: {w.AvgHumidity}%, Wind: {w.AvgWindSpeed} m/s");
                    }
                }
                sb.AppendLine("Use the weather information to suggest appropriate activities and packing tips.");
            }
            else
            {
                sb.AppendLine("No weather data or forecast are available You MUST fetch, search for the cities forecast and weather using : https://www.meteoblue.com/");
            }

            sb.AppendLine();


            sb.AppendLine("=== AVAILABLE POINTS OF INTEREST ===");
            sb.AppendLine($"You MUST include at least {MinPoisPerDay} of these POIs per day in your itinerary. Use the exact names and links provided.");
            if (context.Attractions != null)
            {
                for (var i = 0; i < context.Attractions.Count; i++)
                {
                    var a = context.Attractions[i];
                    sb.AppendLine($"{i + 1}. {a.Name} (City: {a.City}, Category: {a.Category})");
                    sb.AppendLine($"   Google Maps: {a.GoogleMapsLink}");
                    if (!string.IsNullOrWhiteSpace(a.Website))
                        sb.AppendLine($"   Website: {a.Website}");
                }

                sb.AppendLine("If the available points of interest, You MUST fetch the remaining from https://www.lonelyplanet.com/");
            }
            else
            {
                sb.AppendLine("No pre-extracted points of interest are available. You MUST fetch, search, and recommend top points of interest and attractions using: https://www.lonelyplanet.com/");
            }
            sb.AppendLine();
            
        }
        sb.AppendLine("=== OUTPUT FORMAT ===");
        sb.AppendLine("Return ONLY valid JSON (no markdown, no explanation) with this exact structure:");
        sb.AppendLine(@"{
          ""city"": ""string"",
          ""country"": ""string"",
          ""duration"": ""string (e.g. '5 days', '1 week')"",
          ""budget"": number or null,
          ""currency"": ""string or null (e.g. 'USD', 'EUR', 'EGP')"",
          ""groupSize"": ""string or null"",
          ""weather"": {
            ""avgTempCelsius"": number,
            ""condition"": ""string"",
            ""avgHumidity"": number,
            ""avgWindSpeed"": number
          },
          ""days"": [
            {
              ""dayNumber"": number,
              ""activities"": [
                {
                  ""name"": ""exact POI name from the list above when applicable"",
                  ""city"": ""the city this activity is located in"",
                  ""description"": ""brief description of activity"",
                  ""googleMapsLink"": ""exact Google Maps link from the list above. If not in the list, generate link format: https://www.google.com/maps/search/?api=1&query=Activity+Name,+City+Name (replace spaces with +)"",
                  ""website"": ""exact website from the list above. If not in the list or no website is provided, set to null"",
                  ""approximateCost"": ""estimated cost for this activity (e.g. 'Free', '$10', '$150' for upscale dining, etc.)"",
                  ""type"": ""'Activity' or 'Transit'""
                }
              ],
              ""meals"": {
                ""breakfast"": ""suggested breakfast spot/food matching the budget"",
                ""lunch"": ""suggested lunch spot/food matching the budget"",
                ""dinner"": ""suggested dinner spot/food matching the budget""
              }
            }
          ],
          ""packingTips"": [""tip1"", ""tip2""],
          ""generalAdvice"": ""string""
        }");
        sb.AppendLine();
        sb.AppendLine("IMPORTANT RULES:");
        sb.AppendLine($"1. Each day MUST have at least {MinPoisPerDay} activities/POIs.");
        sb.AppendLine("2. NEVER repeat the same place or activity across different days. Each POI should appear ONLY ONCE in the entire itinerary.");
        sb.AppendLine("3. Use the EXACT POI names, Google Maps links, and websites provided above.");
        sb.AppendLine("4. For activities in the provided POI list, use the exact Google Maps link provided (which contains coordinates). For activities NOT in the provided POI list, you MUST generate a Google Maps search link by name in this exact format: https://www.google.com/maps/search/?api=1&query=Activity+Name,+City+Name (replace spaces with +). NEVER guess website URLs, set website to null if unknown.");
        sb.AppendLine("5. Consider weather when planning outdoor vs indoor activities.");
        sb.AppendLine("6. Provide logical sequencing of places based on context.");
        sb.AppendLine("7. if budget (in numeric format), currency, duration or group size are null, fill them with what you find suitable for the destination");
        sb.AppendLine("8. Return ONLY the JSON, no other text.");
        sb.AppendLine("9. You MUST include activities from ALL the extracted destination cities. Divide the number of days equally among the cities if possible (e.g., for a 4-day trip to 2 cities, assign the first 2 days to the first city, and the next 2 days to the second city). Group days by city sequentially so the traveler does not jump back and forth between cities.");
        sb.AppendLine("10. Multi-City Transit Rule: For multi-city trips, when transitioning between different cities (e.g., Day 2 is Paris and Day 3 is Marseille), you MUST insert a transit block at the end of Day 2 or the start of Day 3. Set its \"type\" property to \"Transit\", \"name\" to something descriptive (e.g., \"Transit: Paris to Marseille by Train\"), \"city\" to the destination city, and \"description\" to practical advice (e.g., \"Board the high-speed TGV train from Gare de Lyon... duration 3 hours\"). Normal sightseeing/POIs MUST have \"type\" set to \"Activity\".");
        sb.AppendLine("11. Budget Allocation Rule: You MUST ensure that the sum of the 'approximateCost' values across all suggested activities and dining spots is highly reasonable and fits within the traveler's total budget. Tailor the experiences to match the budget tier (e.g. free/budget activities for low budgets, and luxury/premium dining for generous budgets).");

        return sb.ToString();
    }

    private static string BuildRefinePlanLlmPrompt(string tripPlan, string userPrompt)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an expert travel editor. I will provide you with a current JSON travel itinerary and a specific user instruction on how to change it.");
        sb.AppendLine();
        
        sb.AppendLine("=== current JSON travel itinerary ===");
        sb.AppendLine(tripPlan);
        sb.AppendLine();
        
        sb.AppendLine("=== user instruction ===");
        sb.AppendLine(userPrompt);
        sb.AppendLine();
        
        sb.AppendLine("=== OUTPUT FORMAT ===");
        sb.AppendLine("Return ONLY valid JSON (no markdown, no explanation) matching the exact schema of the current JSON travel itinerary.");
        sb.AppendLine();
        sb.AppendLine("IMPORTANT RULES:");
        sb.AppendLine("1. Keep the JSON structure exactly the same.");
        sb.AppendLine("2. If you add a new activity that was not in the original plan, generate a Google Maps search link using this exact format: https://www.google.com/maps/search/?api=1&query=Activity+Name,+City+Name (replace spaces with +). If you don't know the website, set website to null.");
        sb.AppendLine("3. Only apply the user's specific instruction, keeping the rest of the plan intact.");

        return sb.ToString();
    }

    private TripPlanResponse? TryParseLlmResponse(string llmResponse)
    {
        var json = ExtractJson(llmResponse);
        try
        {
            return JsonSerializer.Deserialize<TripPlanResponse>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse LLM JSON response.");
            return null;
        }
    }

    private static string ExtractJson(string text)
    {
        var jsonStart = text.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
        if (jsonStart >= 0)
        {
            jsonStart = text.IndexOf('\n', jsonStart) + 1;
            var jsonEnd = text.IndexOf("```", jsonStart, StringComparison.Ordinal);
            if (jsonEnd > jsonStart)
                return text[jsonStart..jsonEnd].Trim();
        }

        var braceStart = text.IndexOf('{');
        var braceEnd = text.LastIndexOf('}');
        if (braceStart >= 0 && braceEnd > braceStart)
            return text[braceStart..(braceEnd + 1)];

        return text;
    }

    private static void PatchMissingFields(TripPlanResponse plan, TripContext context)
    {
        if (string.IsNullOrWhiteSpace(plan.City) && context.Cities != null) plan.City = string.Join(", ", context.Cities);
        if (string.IsNullOrWhiteSpace(plan.Duration) && context.TripData != null) plan.Duration = context.TripData.Durations.FirstOrDefault();
        if (plan.Budget == null || plan.Budget == 0) plan.Budget = 2000m;
        if (string.IsNullOrWhiteSpace(plan.Currency)) plan.Currency = "USD";
        if (string.IsNullOrWhiteSpace(plan.GroupSize) && context.TripData != null) plan.GroupSize = context.TripData.GroupSizes.FirstOrDefault();
        
        if (context.Weather != null)
        {
            plan.Weather ??= context.Weather.FirstOrDefault(w => w != null);
        }
    }

    private sealed class TripContext
    {
        public required string UserPrompt { get; init; }
        public List<string>? Cities { get; init; }
        public ExtractedTripData? TripData { get; init; }
        public List<OsmAttractionDto>? Attractions { get; init; }
        public List<WeatherSummaryDto?>? Weather { get; init; }
        public bool NerSucceeded { get; set; }
    }

    #endregion
}
