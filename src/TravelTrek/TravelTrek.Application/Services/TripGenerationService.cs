using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TravelTrek.Application.DTOs.Ner;
using TravelTrek.Application.DTOs.Osm;
using TravelTrek.Application.DTOs.TripPlanner;
using TravelTrek.Application.DTOs.Weather;
using TravelTrek.Application.Interfaces;
using TravelTrek.Domain.Common;
using TravelTrek.Domain.Interfaces;

namespace TravelTrek.Application.Services;

public class TripGenerationService : ITripGenerationService
{
    private readonly INerService _nerService;
    private readonly IOsmService _osmService;
    private readonly IOpenWeatherService _weatherService;
    private readonly ILLMService _illmService;
    private readonly ILogger<TripGenerationService> _logger;
    private readonly ITripPlanRepository _tripPlanRepository;
    private readonly AutoMapper.IMapper _mapper;
    
    private const int MinPoisPerDay = 3;
    private const int DefaultPoiLimit = 12;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public TripGenerationService(INerService nerService, IOsmService osmService, IOpenWeatherService weatherService, ILLMService illmService, ILogger<TripGenerationService> logger, ITripPlanRepository tripPlanRepository, AutoMapper.IMapper mapper)
    {
        _nerService = nerService;
        _osmService = osmService;
        _weatherService = weatherService;
        _illmService = illmService;
        _logger = logger;
        _tripPlanRepository = tripPlanRepository;
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
                _logger.LogWarning("First LLM parse failed (NER-fallback), retrying...");
                var retryResult = await _illmService.GenerateAsync(BuildGeneratePlanLlmPrompt(promptOnlyContext), ct);
                if (retryResult.IsSuccess)
                    promptOnlyPlan = TryParseLlmResponse(retryResult.Value);

                if (promptOnlyPlan == null)
                    return Result.Failure<TripPlanResponse>(Error.External("TripPlan.ParseFailed", "Failed to parse the generated itinerary after retry. Please try again."));
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
        
        
        var osmTasks = tripData.Locations.Select(city => _osmService.GetTopAttractionsAsync(city, DefaultPoiLimit, ct)).ToList();
        var weatherTasks = tripData.Locations.Select(city => GetWeatherForCityAsync(city, ct)).ToList();
        
        await Task.WhenAll(osmTasks.Cast<Task>().Concat(weatherTasks));
        
        var attractionsResults = osmTasks.Select(t => t.Result).ToArray();
        var weatherResults = weatherTasks.Select(t => t.Result).ToArray();
        
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
            _logger.LogWarning("First LLM parse failed, retrying...");
            var retryResult = await _illmService.GenerateAsync(BuildGeneratePlanLlmPrompt(context), ct);
            if (retryResult.IsSuccess)
                plan = TryParseLlmResponse(retryResult.Value);

            if (plan == null)
                return Result.Failure<TripPlanResponse>(Error.External("TripPlan.ParseFailed", "Failed to parse the generated itinerary after retry. Please try again."));
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
            _logger.LogWarning("First LLM parse failed (refine), retrying...");
            var retryResult = await _illmService.GenerateAsync(BuildRefinePlanLlmPrompt(tripPlanJson, request.UserPrompt), ct);
            if (retryResult.IsSuccess)
                plan = TryParseLlmResponse(retryResult.Value);

            if (plan == null)
                return Result.Failure<TripPlanResponse>(Error.External("RefinePlan.ParseFailed", "Failed to parse the refined itinerary after retry. Please try again."));
        }
        return Result.Success(plan);
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
            if (forecastResult.IsFailure)
            {
                return null;
            }
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
        sb.AppendLine("You are an expert travel editor. Your ONLY job is to refine an existing JSON travel itinerary based on specific user instructions.");
        sb.AppendLine();
        
        sb.AppendLine("=== CRITICAL VALIDATION ===");
        sb.AppendLine("BEFORE proceeding, validate that the user instruction is a REFINEMENT request, NOT a request for a NEW trip plan.");
        sb.AppendLine("REFINEMENT means: modifying, adjusting, replacing, or removing existing activities/dates/details from this specific trip.");
        sb.AppendLine("NEW TRIP means: creating an entirely different trip, planning a new destination, or starting from scratch.");
        sb.AppendLine();
        sb.AppendLine("If the user is asking for a NEW trip plan instead of refining THIS trip, REFUSE and respond ONLY with:");
        sb.AppendLine("{\"error\": \"I can only refine the existing trip. To create a new trip plan, please start a new planning session.\"}");
        sb.AppendLine();
        
        sb.AppendLine("=== CURRENT JSON TRAVEL ITINERARY ===");
        sb.AppendLine(tripPlan);
        sb.AppendLine();
        
        sb.AppendLine("=== USER INSTRUCTION ===");
        sb.AppendLine(userPrompt);
        sb.AppendLine();
        
        sb.AppendLine("=== ALLOWED MODIFICATIONS ===");
        sb.AppendLine("You may ONLY:");
        sb.AppendLine("• Modify dates/times of existing activities");
        sb.AppendLine("• Replace an existing activity with a different one at the same location");
        sb.AppendLine("• Remove activities from the itinerary");
        sb.AppendLine("• Adjust activity details (description, duration) while keeping the activity");
        sb.AppendLine("• Reorder existing activities");
        sb.AppendLine();
        sb.AppendLine("You may NOT:");
        sb.AppendLine("• Add completely new destinations not in the original plan");
        sb.AppendLine("• Expand the trip scope beyond the original structure");
        sb.AppendLine("• Suggest an entirely different itinerary");
        sb.AppendLine();
        
        sb.AppendLine("=== OUTPUT FORMAT ===");
        sb.AppendLine("Return ONLY valid JSON (no markdown, no explanation, no error messages) matching the exact schema of the current JSON travel itinerary.");
        sb.AppendLine();
        sb.AppendLine("IMPORTANT RULES:");
        sb.AppendLine("1. Keep the JSON structure exactly the same—do not add or remove top-level keys or array lengths (unless explicitly removing activities as per user request).");
        sb.AppendLine("2. If you replace an activity with a new one, generate a Google Maps search link using this exact format: https://www.google.com/maps/search/?api=1&query=Activity+Name,+City+Name (replace spaces with +). If you don't know the website, set website to null.");
        sb.AppendLine("3. Apply ONLY the user's specific instruction. Do NOT make any additional changes.");
        sb.AppendLine("4. Preserve all unchanged activities, dates, and trip details exactly as they are.");
        sb.AppendLine("5. Return ONLY the refined JSON. If the refinement is impossible or the request is out of scope, return: {\"error\": \"Refinement not possible: [reason]\"}");

        return sb.ToString();
    }
    private TripPlanResponse? TryParseLlmResponse(string llmResponse)
    {
        var json = ExtractJson(llmResponse);
        try
        {
            return JsonSerializer.Deserialize<TripPlanResponse>(json, JsonOptions);
        }
        catch (JsonException)
        {
            var sanitized = SanitizeJson(json);
            try
            {
                return JsonSerializer.Deserialize<TripPlanResponse>(sanitized, JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse LLM JSON response even after sanitization.");
                return null;
            }
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

    private static string SanitizeJson(string json)
    {
        var sanitized = Regex.Replace(json, @",\s*([}\]])", "$1");
        sanitized = Regex.Replace(sanitized, @"//.*?$", "", RegexOptions.Multiline);
        return sanitized.Trim();
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
