using System.Text.Json;
using Microsoft.Extensions.Logging;
using TravelTrek.Application.DTOs.Ner;
using TravelTrek.Application.DTOs.TripPlanner;
using TravelTrek.Application.DTOs.Weather;
using TravelTrek.Application.Interfaces;
using TravelTrek.Domain.Common;
using TravelTrek.Domain.Interfaces;

namespace TravelTrek.Application.Services.TripGeneration;

public class TripGenerationService : ITripGenerationService
{
    private readonly INerService _nerService;
    private readonly IPoiService _poiService;
    private readonly IOpenWeatherService _weatherService;
    private readonly ILLMService _illmService;
    private readonly ILogger<TripGenerationService> _logger;
    private readonly ITripPlanRepository _tripPlanRepository;
    private readonly AutoMapper.IMapper _mapper;
    private readonly ICacheService _cache;
    private readonly IUserRepository _userRepository;
    private readonly ICurrencyService _currencyService;
    private const int PoisPerCity = 20;

    public TripGenerationService(
        INerService nerService, 
        IPoiService poiService, 
        IOpenWeatherService weatherService, 
        ILLMService illmService, 
        ILogger<TripGenerationService> logger, 
        ITripPlanRepository tripPlanRepository, 
        AutoMapper.IMapper mapper, 
        ICacheService cache,
        IUserRepository userRepository,
        ICurrencyService currencyService)
    {
        _nerService = nerService;
        _poiService = poiService;
        _weatherService = weatherService;
        _illmService = illmService;
        _logger = logger;
        _tripPlanRepository = tripPlanRepository;
        _mapper = mapper;
        _cache = cache;
        _userRepository = userRepository;
        _currencyService = currencyService;
    }
    
    public async Task<Result<TripPlanResponse>> GenerateTripPlanAsync(TripPlanRequest request, Guid userId, CancellationToken ct = default)
    {
        var rawExtractedEntitiesResult = await _nerService.ExtractEntitiesAsync(new NerRequest { Inputs = request.Prompt }, ct);

        if (rawExtractedEntitiesResult.IsFailure)
        {
            return Result.Failure<TripPlanResponse>(rawExtractedEntitiesResult.Error);
        }

        var feasibilityResult = await _nerService.CheckFeasibilityAsync(rawExtractedEntitiesResult.Value, ct);
        if (!feasibilityResult.IsSuccess)
        {
            return Result.Failure<TripPlanResponse>(feasibilityResult.Error);
        }

        var tripData = TripGenerationHelper.ParseTripData(rawExtractedEntitiesResult.Value);
        if (tripData.Locations.Count == 0)
        {
            return Result.Failure<TripPlanResponse>(Error.Validation("TripPlan.NoCity", "Could not extract a destination city from your prompt. Please include a city name."));
        }

        var cities = tripData.Locations;

        var poiTasks = cities.Select(city => _poiService.GetTopAttractionsAsync(city, PoisPerCity, ct)).ToList();
        var diningTasks = cities.Select(city => _poiService.GetTopDiningAsync(city, PoisPerCity, ct)).ToList();
        var weatherTasks = cities.Select(city => GetWeatherForCityAsync(city, ct)).ToList();

        await Task.WhenAll(poiTasks.Concat<Task>(diningTasks).Concat(weatherTasks));
        var attractions = poiTasks.Select(t => t.Result).Where(r => r.IsSuccess).SelectMany(r => r.Value).ToList();
        var dining = diningTasks.Select(t => t.Result).Where(r => r.IsSuccess).SelectMany(r => r.Value).ToList();
        var weather = weatherTasks.Select(t => t.Result).Where(w => w != null).ToList();

        var context = new TripContext
        {
            UserPrompt = request.Prompt,
            Cities = cities,
            TripData = tripData,
            Attractions = attractions.Count == 0 ? null : attractions,
            Dining = dining.Count == 0 ? null : dining,
            Weather = weather.Count == 0 ? null : weather,
        };
        
        var llmPrompt = TripGenerationHelper.BuildGeneratePlanLlmPrompt(context);
        var llmResult = await _illmService.GenerateAsync(llmPrompt, ct);
        if (llmResult.IsFailure)
        {
            return Result.Failure<TripPlanResponse>(llmResult.Error);
        }
        
        var plan = TripGenerationHelper.TryParseLlmResponse(llmResult.Value);
        if (plan == null)
        {
            var retryResult = await _illmService.GenerateAsync(llmPrompt, ct);
            if (retryResult.IsSuccess)
            {
                plan = TripGenerationHelper.TryParseLlmResponse(retryResult.Value);
            }

            if (plan == null)
            {
                return Result.Failure<TripPlanResponse>(Error.External("TripPlan.ParseFailed", "Failed to parse the generated itinerary after retry. Please try again."));
            }
        }
        
        TripGenerationHelper.PatchMissingFields(plan, context);
        TripGenerationHelper.PatchActivityDetails(plan, context.Attractions);
        TripGenerationHelper.PatchDestinationImage(plan, context.Attractions);
        
        plan.Prompt = request.Prompt;
        await PopulateCurrencyConversionAsync(plan, userId, ct);
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

        var refinePrompt = TripGenerationHelper.BuildRefinePlanLlmPrompt(tripPlanJson, request.UserPrompt);
        var llmResult = await _illmService.GenerateAsync(refinePrompt, ct);
        
        if (llmResult.IsFailure)
        {
            return Result.Failure<TripPlanResponse>(llmResult.Error);
        }

        var plan = TripGenerationHelper.TryParseLlmResponse(llmResult.Value);
        if (plan == null)
        {
            var retryResult = await _illmService.GenerateAsync(refinePrompt, ct);
            if (retryResult.IsSuccess)
                plan = TripGenerationHelper.TryParseLlmResponse(retryResult.Value);

            if (plan == null)
                return Result.Failure<TripPlanResponse>(Error.External("RefinePlan.ParseFailed", "Failed to parse the refined itinerary after retry. Please try again."));
        }

        plan.Prompt = request.UserPrompt;
        await PopulateCurrencyConversionAsync(plan, userId, ct);
        return Result.Success(plan);
    }

    #region Helpers

    private async Task<WeatherSummaryDto?> GetWeatherForCityAsync(string city, CancellationToken ct)
    {
        var cacheKey = $"weather:{city.ToLowerInvariant().Trim()}";

        var cached = await _cache.GetAsync<WeatherSummaryDto>(cacheKey, ct);
        if (cached != null)
        {
            _logger.LogInformation("Cache hit for weather '{City}'.", city);
            return cached;
        }

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
            if (forecastResult.IsFailure)
            {
                return null;
            }
            var forecast = forecastResult.Value;
            if (forecastResult.IsSuccess && forecastResult.Value.Items.Count > 0)
            {
                var summary = new WeatherSummaryDto
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
                await _cache.SetAsync(cacheKey, summary, TimeSpan.FromHours(2), ct);
                return summary;
            }

            _logger.LogWarning("Forecast unavailable for '{City}', falling back to current weather.", city);

            var currentResult = await _weatherService.GetCurrentWeatherAsync(coords, ct);
            if (currentResult.IsFailure)
            {
                return null;
            }

            var c = currentResult.Value;
            var currentSummary = new WeatherSummaryDto
            {
                AvgTempCelsius = c.Main.Temp,
                Condition = c.Weather.FirstOrDefault()?.Description ?? "Unknown",
                AvgHumidity = c.Main.Humidity,
                AvgWindSpeed = c.Wind.Speed
            };
            await _cache.SetAsync(cacheKey, currentSummary, TimeSpan.FromHours(2), ct);
            return currentSummary;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error fetching weather for '{City}'.", city);
            return null;
        }
    }
    private async Task PopulateCurrencyConversionAsync(TripPlanResponse plan, Guid userId, CancellationToken ct)
    {
        try
        {
            var user = await _userRepository.GetByIdAsync(userId);
            var userCountry = user?.Country;
            var userCurrency = TripGenerationHelper.GetCurrencyForCountry(userCountry);

            var destinationCurrency = plan.Currency;
            if (string.IsNullOrWhiteSpace(destinationCurrency))
            {
                string? destinationCountry = plan.Country;
                if (string.IsNullOrWhiteSpace(destinationCountry) && !string.IsNullOrWhiteSpace(plan.City))
                {
                    var firstCity = plan.City.Split(',').FirstOrDefault()?.Trim();
                    if (!string.IsNullOrWhiteSpace(firstCity))
                    {
                        destinationCountry = TripGenerationHelper.GetCountryForCity(firstCity);
                    }
                }

                destinationCurrency = !string.IsNullOrWhiteSpace(destinationCountry)
                    ? TripGenerationHelper.GetCurrencyForCountry(destinationCountry)
                    : "USD";

                plan.Currency = destinationCurrency;
            }

            if (plan.Budget == null || plan.Budget == 0)
            {
                if (string.Equals(destinationCurrency, "USD", StringComparison.OrdinalIgnoreCase))
                {
                    plan.Budget = 2000m;
                }
                else
                {
                    var usdToDestRate = await _currencyService.GetExchangeRateAsync("USD", destinationCurrency, ct);
                    if (usdToDestRate.HasValue)
                    {
                        plan.Budget = Math.Round(2000m * usdToDestRate.Value, 2);
                    }
                    else
                    {
                        plan.Budget = 2000m;
                    }
                }
            }

            plan.UserCurrency = userCurrency;

            var rate = await _currencyService.GetExchangeRateAsync(destinationCurrency, userCurrency, ct);
            plan.ConversionRate = rate;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to populate currency conversion for user {UserId}", userId);
        }
        finally
        {
            if (string.IsNullOrWhiteSpace(plan.Currency)) plan.Currency = "USD";
            if (plan.Budget == null || plan.Budget == 0) plan.Budget = 2000m;
        }
    }
    
    #endregion
}
