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
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;


    private const int MinPoisPerDay = 3;
    private const int DefaultPoiLimit = 30;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public TripPlanService(INerService nerService, IOsmService osmService, IOpenWeatherService weatherService, ILLMService illmService, ILogger<TripPlanService> logger, ITripPlanRepository tripPlanRepository, IUnitOfWork unitOfWork, IMapper mapper)
    {
        _nerService = nerService;
        _osmService = osmService;
        _weatherService = weatherService;
        _illmService = illmService;
        _logger = logger;
        _tripPlanRepository = tripPlanRepository;
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<Result<TripPlanResponse>> GenerateTripPlanAsync(TripPlanRequest request, CancellationToken ct = default)
    {
        var nerResult = await _nerService.ExtractAndParseTripDataAsync(new NerRequest { Inputs = request.Prompt }, ct);
        if (nerResult.IsFailure)
        {
            _logger.LogWarning("NER extraction failed: {Error}", nerResult.Error);
            return Result.Failure<TripPlanResponse>(nerResult.Error);
        }

        var tripData = nerResult.Value;
        var city = tripData.Locations.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(city))
        {
            return Result.Failure<TripPlanResponse>(Error.Validation("TripPlan.NoCity", "Could not extract a destination city from your prompt. Please include a city name."));
        }

        _logger.LogInformation("NER extracted — City: {City}, Duration: {Duration}, Budget: {Budget}, GroupSize: {GroupSize}",
            city, tripData.Durations.FirstOrDefault() ?? "N/A", tripData.Budgets.FirstOrDefault() ?? "N/A", tripData.GroupSizes.FirstOrDefault() ?? "N/A");

        var osmTask = _osmService.GetTopAttractionsAsync(city, DefaultPoiLimit, ct);
        var weatherTask = GetWeatherForCityAsync(city, ct);
        await Task.WhenAll(osmTask, weatherTask);

        var osmResult = await osmTask;
        var attractions = osmResult.IsSuccess ? osmResult.Value : [];
        if (osmResult.IsFailure)
            _logger.LogWarning("OSM POI fetch failed: {Error}. Proceeding without POIs.", osmResult.Error);

        var context = new TripContext
        {
            UserPrompt = request.Prompt,
            City = city,
            TripData = tripData,
            Attractions = attractions,
            Weather = await weatherTask
        };

        var llmResult = await _illmService.GenerateAsync(BuildLlmPrompt(context), ct);
        if (llmResult.IsFailure)
        {
            _logger.LogWarning("LLM generation failed: {Error}", llmResult.Error);
            return Result.Failure<TripPlanResponse>(llmResult.Error);
        }

        var plan = TryParseLlmResponse(llmResult.Value);
        if (plan == null)
            return Result.Failure<TripPlanResponse>(Error.External("TripPlan.ParseFailed", "Failed to parse the generated itinerary. Please try again."));

        PatchMissingFields(plan, context);
        return Result.Success(plan);
    }

    public async Task<Result<Guid>> SaveTripPlanAsync(TripPlanResponse planDto, Guid userId, CancellationToken ct = default)
    {
        var tripPlan = _mapper.Map<TripPlan>(planDto);
        tripPlan.UserId = userId;
        await _tripPlanRepository.AddAsync(tripPlan);
        await _unitOfWork.SaveChangesAsync();

        return Result.Success(tripPlan.Id);
    }

    public async Task<Result> UpdateTripPlanAsync(Guid tripId, TripPlanResponse updatedPlanDto, Guid userId, CancellationToken ct = default)
    {
        var existingTrip = await _tripPlanRepository.GetTripPlanWithDetailsAsync(tripId);
        if (existingTrip == null)
            return Result.Failure(Error.NotFound("TripPlan.NotFound", "The trip could not be found."));

        if (existingTrip.UserId != userId)
            return Result.Failure(Error.Forbidden("TripPlan.Forbidden", "You do not have permission to edit this trip."));

        // 1. Update primitive properties
        existingTrip.City = updatedPlanDto.City;
        existingTrip.Country = updatedPlanDto.Country;
        existingTrip.Budget = updatedPlanDto.Budget;
        existingTrip.GroupSize = updatedPlanDto.GroupSize;
        existingTrip.GeneralAdvice = updatedPlanDto.GeneralAdvice;
        
        if (updatedPlanDto.Weather != null)
            existingTrip.Weather = _mapper.Map<WeatherSummary>(updatedPlanDto.Weather);

        if (updatedPlanDto.PackingTips != null)
            existingTrip.PackingTips = updatedPlanDto.PackingTips.ToList();

        // 2. Full Replacement of Days & Activities
        // Clear the old ones (EF Core will cascade delete the old records)
        existingTrip.Days.Clear();

        // Map the new ones (AutoMapper ignores IDs, so these will be inserted as new rows)
        var newDays = _mapper.Map<List<DayPlan>>(updatedPlanDto.Days);
        foreach (var day in newDays)
        {
            existingTrip.Days.Add(day);
        }

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
        if (tripPlan != null)
        {
            if (tripPlan.UserId != userId)
            {
                return Result.Failure<TripPlanResponse>(Error.Forbidden("GetTripPlan.Forbidden", "Forbidden Request"));
            }

            var tripPlanDto = _mapper.Map<TripPlanResponse>(tripPlan);
            return Result.Success(tripPlanDto);
        }

        return Result.Failure<TripPlanResponse>(Error.NotFound("GetTripPlan.NotFound", "Trip Plan Not Found"));
    }

    public async Task<Result<IEnumerable<TripPlanResponse>>> GetTripPlansAsync(Guid userId)
    {
        var tripPlans = await _tripPlanRepository.GetUserTripPlansAsync(userId);

        var tripPlansDto = _mapper.Map<IEnumerable<TripPlanResponse>>(tripPlans);
        return Result.Success(tripPlansDto);
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
            if (forecastResult.IsSuccess && forecastResult.Value.Items.Count > 0)
                return SummarizeForecast(forecastResult.Value);

            _logger.LogWarning("Forecast unavailable for '{City}', falling back to current weather.", city);

            var currentResult = await _weatherService.GetCurrentWeatherAsync(coords, ct);
            if (currentResult.IsFailure)
                return null;

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

    private static WeatherSummaryDto SummarizeForecast(ForecastResponse forecast)
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

    private static string BuildLlmPrompt(TripContext context)
    {
        var sb = new StringBuilder();

        sb.AppendLine("You are an expert travel planner. Generate a detailed day-by-day trip itinerary in valid JSON format.");
        sb.AppendLine();

        sb.AppendLine("=== USER REQUEST ===");
        sb.AppendLine(context.UserPrompt);
        sb.AppendLine();

        sb.AppendLine("=== EXTRACTED TRIP DETAILS ===");
        sb.AppendLine($"- Destination: {context.City}");
        if (!string.IsNullOrWhiteSpace(context.TripData.Durations.FirstOrDefault()))
            sb.AppendLine($"- Duration: {context.TripData.Durations[0]}");
        if (!string.IsNullOrWhiteSpace(context.TripData.Budgets.FirstOrDefault()))
            sb.AppendLine($"- Budget: {context.TripData.Budgets[0]}");
        if (!string.IsNullOrWhiteSpace(context.TripData.GroupSizes.FirstOrDefault()))
            sb.AppendLine($"- Group size: {context.TripData.GroupSizes[0]}");
        if (context.TripData.TravelTypes.Count > 0) sb.AppendLine($"- Travel type: {string.Join(", ", context.TripData.TravelTypes)}");
        if (context.TripData.Activities.Count > 0) sb.AppendLine($"- Preferred activities: {string.Join(", ", context.TripData.Activities)}");
        if (context.TripData.Dates.Count > 0) sb.AppendLine($"- Travel dates: {string.Join(", ", context.TripData.Dates)}");
        sb.AppendLine();

        if (context.Weather != null)
        {
            sb.AppendLine("=== WEATHER FORECAST ===");
            sb.AppendLine($"- Average temperature: {context.Weather.AvgTempCelsius}°C");
            sb.AppendLine($"- Condition: {context.Weather.Condition}");
            sb.AppendLine($"- Humidity: {context.Weather.AvgHumidity}%");
            sb.AppendLine($"- Wind speed: {context.Weather.AvgWindSpeed} m/s");
            sb.AppendLine("Use the weather information to suggest appropriate activities and packing tips.");
            sb.AppendLine();
        }

        if (context.Attractions.Count > 0)
        {
            sb.AppendLine("=== AVAILABLE POINTS OF INTEREST ===");
            sb.AppendLine($"You MUST include at least {MinPoisPerDay} of these POIs per day in your itinerary. Use the exact names and links provided.");
            sb.AppendLine();

            for (var i = 0; i < context.Attractions.Count; i++)
            {
                var a = context.Attractions[i];
                sb.AppendLine($"{i + 1}. {a.Name} (Category: {a.Category})");
                sb.AppendLine($"   Google Maps: {a.GoogleMapsLink}");
                if (!string.IsNullOrWhiteSpace(a.Website))
                    sb.AppendLine($"   Website: {a.Website}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("=== OUTPUT FORMAT ===");
        sb.AppendLine("Return ONLY valid JSON (no markdown, no explanation) with this exact structure:");
        sb.AppendLine(@"{
          ""city"": ""string"",
          ""country"": ""string"",
          ""duration"": ""string (e.g. '5 days', '1 week')"",
          ""budget"": ""string or null"",
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
                  ""description"": ""brief description of activity"",
                  ""googleMapsLink"": ""link from above or null"",
                  ""website"": ""website from above or null""
                }
              ]
            }
          ],
          ""packingTips"": [""tip1"", ""tip2""],
          ""generalAdvice"": ""string""
        }");
        sb.AppendLine();
        sb.AppendLine("IMPORTANT RULES:");
        sb.AppendLine($"1. Each day MUST have at least {MinPoisPerDay} activities/POIs.");
        sb.AppendLine("2. NEVER repeat the same place or activity across different days. Each POI should appear ONLY ONCE in the entire itinerary.");
        sb.AppendLine("3. Use the EXACT POI names and Google Maps links provided above.");
        sb.AppendLine("4. Consider weather when planning outdoor vs indoor activities.");
        sb.AppendLine("5. Provide logical sequencing of places based on context.");
        sb.AppendLine("6. Return ONLY the JSON, no other text.");

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
        if (string.IsNullOrWhiteSpace(plan.City)) plan.City = context.City;
        if (string.IsNullOrWhiteSpace(plan.Duration)) plan.Duration = context.TripData.Durations.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(plan.Budget)) plan.Budget = context.TripData.Budgets.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(plan.GroupSize)) plan.GroupSize = context.TripData.GroupSizes.FirstOrDefault();
        plan.Weather ??= context.Weather;
    }
    
    private sealed class TripContext
    {
        public required string UserPrompt { get; init; }
        public required string City { get; init; }
        public required ExtractedTripData TripData { get; init; }
        public required List<OsmAttractionDto> Attractions { get; init; }
        public WeatherSummaryDto? Weather { get; init; }
    }

    #endregion

}
