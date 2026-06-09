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
    private readonly IPoiService _poiService;
    private readonly IOpenWeatherService _weatherService;
    private readonly ILLMService _illmService;
    private readonly ILogger<TripGenerationService> _logger;
    private readonly ITripPlanRepository _tripPlanRepository;
    private readonly AutoMapper.IMapper _mapper;
    private readonly ICacheService _cache;
    
    private const int MinPoisPerDay = 3;
    private const int DefaultPoiLimit = 20;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly Dictionary<string, List<string>> CountryToCities = new(StringComparer.OrdinalIgnoreCase)
    {
        ["France"] = new() { "Paris", "Nice" },
        ["United Kingdom"] = new() { "London", "Edinburgh" },
        ["UK"] = new() { "London", "Edinburgh" },
        ["Great Britain"] = new() { "London", "Edinburgh" },
        ["England"] = new() { "London", "Manchester" },
        ["Scotland"] = new() { "Edinburgh", "Glasgow" },
        ["Ireland"] = new() { "Dublin", "Galway" },
        ["Germany"] = new() { "Berlin", "Munich" },
        ["Italy"] = new() { "Rome", "Florence" },
        ["Spain"] = new() { "Madrid", "Barcelona" },
        ["Japan"] = new() { "Tokyo", "Kyoto" },
        ["Egypt"] = new() { "Cairo", "Luxor" },
        ["United States"] = new() { "New York City", "Los Angeles" },
        ["USA"] = new() { "New York City", "Los Angeles" },
        ["US"] = new() { "New York City", "Los Angeles" },
        ["United States of America"] = new() { "New York City", "Los Angeles" },
        ["Canada"] = new() { "Toronto", "Vancouver" },
        ["Australia"] = new() { "Sydney", "Melbourne" },
        ["China"] = new() { "Beijing", "Shanghai" },
        ["Brazil"] = new() { "Rio de Janeiro", "Sao Paulo" },
        ["India"] = new() { "New Delhi", "Mumbai" },
        ["South Korea"] = new() { "Seoul", "Busan" },
        ["Turkey"] = new() { "Istanbul", "Cappadocia" },
        ["Saudi Arabia"] = new() { "Riyadh", "Jeddah" },
        ["United Arab Emirates"] = new() { "Dubai", "Abu Dhabi" },
        ["UAE"] = new() { "Dubai", "Abu Dhabi" },
        ["Greece"] = new() { "Athens", "Santorini" },
        ["Netherlands"] = new() { "Amsterdam", "Rotterdam" },
        ["Switzerland"] = new() { "Zurich", "Geneva" },
        ["Sweden"] = new() { "Stockholm", "Gothenburg" },
        ["Norway"] = new() { "Oslo", "Bergen" },
        ["Denmark"] = new() { "Copenhagen", "Aarhus" },
        ["Austria"] = new() { "Vienna", "Salzburg" },
        ["Portugal"] = new() { "Lisbon", "Porto" },
        ["Mexico"] = new() { "Mexico City", "Cancun" },
        ["South Africa"] = new() { "Cape Town", "Johannesburg" },
        ["Thailand"] = new() { "Bangkok", "Phuket" },
        ["Singapore"] = new() { "Singapore" },
        ["Malaysia"] = new() { "Kuala Lumpur", "Penang" },
        ["Vietnam"] = new() { "Hanoi", "Ho Chi Minh City" },
        ["Indonesia"] = new() { "Bali", "Jakarta" },
        ["New Zealand"] = new() { "Auckland", "Queenstown" },
        ["Argentina"] = new() { "Buenos Aires", "Bariloche" },
        ["Chile"] = new() { "Santiago", "Valparaiso" },
        ["Colombia"] = new() { "Bogota", "Medellin" },
        ["Peru"] = new() { "Lima", "Cusco" },
        ["Morocco"] = new() { "Marrakech", "Fes" },
    };

    public TripGenerationService(INerService nerService, IPoiService poiService, IOpenWeatherService weatherService, ILLMService illmService, ILogger<TripGenerationService> logger, ITripPlanRepository tripPlanRepository, AutoMapper.IMapper mapper, ICacheService cache)
    {
        _nerService = nerService;
        _poiService = poiService;
        _weatherService = weatherService;
        _illmService = illmService;
        _logger = logger;
        _tripPlanRepository = tripPlanRepository;
        _mapper = mapper;
        _cache = cache;
    }

    public async Task<Result<TripPlanResponse>> GenerateTripPlanAsync(TripPlanRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("=== [GENERATE TRIP PLAN START] Prompt: '{Prompt}' ===", request.Prompt);

        var nerRequest = new NerRequest { Inputs = request.Prompt };
        _logger.LogInformation("Calling NER Service to extract locations, budget, duration, and group size.");
        var rawEntitiesResult = await _nerService.ExtractEntitiesAsync(nerRequest, ct);

        if (rawEntitiesResult.IsFailure)
        {
            _logger.LogWarning("NER Service extraction failed: {Error}. Falling back to prompt-only LLM generation.", rawEntitiesResult.Error);
            var promptOnlyContext = new TripContext { UserPrompt = request.Prompt, NerSucceeded = false};

            _logger.LogInformation("Requesting prompt-only itinerary generation from LLM Service.");
            var promptOnlyLlmResult = await _illmService.GenerateAsync(BuildGeneratePlanLlmPrompt(promptOnlyContext), ct);
            if (promptOnlyLlmResult.IsFailure)
            {
                _logger.LogWarning("Fallback LLM generation failed: {Error}", promptOnlyLlmResult.Error);
                return Result.Failure<TripPlanResponse>(promptOnlyLlmResult.Error);
            }

            _logger.LogInformation("Parsing fallback LLM response.");
            var promptOnlyPlan = TryParseLlmResponse(promptOnlyLlmResult.Value);
            if (promptOnlyPlan == null)
            {
                _logger.LogWarning("First fallback LLM parse failed, retrying generation...");
                var retryResult = await _illmService.GenerateAsync(BuildGeneratePlanLlmPrompt(promptOnlyContext), ct);
                if (retryResult.IsSuccess)
                    promptOnlyPlan = TryParseLlmResponse(retryResult.Value);

                if (promptOnlyPlan == null)
                {
                    _logger.LogError("Fallback LLM parse failed after retry.");
                    return Result.Failure<TripPlanResponse>(Error.External("TripPlan.ParseFailed", "Failed to parse the generated itinerary after retry. Please try again."));
                }
            }

            _logger.LogInformation("=== [GENERATE TRIP PLAN SUCCESS (FALLBACK)] ===");
            promptOnlyPlan.Prompt = request.Prompt;
            return Result.Success(promptOnlyPlan);   
        }

        var rawEntities = rawEntitiesResult.Value;
        _logger.LogInformation("NER Service extraction succeeded. Checking feasibility.");
        var feasibilityResult = await _nerService.CheckFeasibilityAsync(rawEntities, ct);
        if (feasibilityResult.IsSuccess && !feasibilityResult.Value.IsFeasible)
        {
            _logger.LogWarning("Trip plan feasibility check failed: {Explanation}", feasibilityResult.Value.Explanation);
            return Result.Failure<TripPlanResponse>(Error.Validation("TripPlan.Infeasible", feasibilityResult.Value.Explanation));
        }

        var tripData = ParseTripData(rawEntities);
        if (tripData.Locations.Count == 0)
        {
            _logger.LogWarning("Could not extract any locations/cities from prompt: '{Prompt}'", request.Prompt);
            return Result.Failure<TripPlanResponse>(Error.Validation("TripPlan.NoCity", "Could not extract a destination city from your prompt. Please include a city name."));
        }

        _logger.LogInformation("NER parsed variables - Locations: [{Cities}], Duration: {Duration}, Budget: {Budget}, GroupSize: {GroupSize}",
            string.Join(", ", tripData.Locations), tripData.Durations.FirstOrDefault() ?? "N/A", tripData.Budgets.FirstOrDefault() ?? "N/A", tripData.GroupSizes.FirstOrDefault() ?? "N/A");
        
        var mappedLocations = new List<string>();
        foreach (var location in tripData.Locations)
        {
            if (CountryToCities.TryGetValue(location, out var famousCities))
            {
                _logger.LogInformation("Location '{Country}' mapped as a country. Expanding to famous cities: [{Cities}]", location, string.Join(", ", famousCities));
                foreach (var city in famousCities)
                {
                    mappedLocations.Add($"{city}, {location}");
                }
            }
            else
            {
                var country = GetCountryForCity(location);
                if (country != null)
                {
                    _logger.LogInformation("City '{City}' mapped to country '{Country}'. Using city-country query for POI and weather fetch.", location, country);
                    mappedLocations.Add($"{location}, {country}");
                }
                else
                {
                    mappedLocations.Add(location);
                }
            }
        }

        mappedLocations = mappedLocations.Select(loc => loc.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        _logger.LogInformation("Final destination query list: [{Locations}]", string.Join(", ", mappedLocations));

        var durationStr = tripData.Durations.FirstOrDefault();
        var days = ParseDurationToDays(durationStr);
        var poiLimit = Math.Clamp(days * 4, 10, 25);
        _logger.LogInformation("Calculated days={Days}, POI limit={PoiLimit} per city.", days, poiLimit);

        _logger.LogInformation("Launching parallel POI & Weather fetch tasks for all mapped destinations.");
        var osmTasks = mappedLocations.Select(city => _poiService.GetTopAttractionsAsync(city, poiLimit, ct)).ToList();
        var weatherTasks = mappedLocations.Select(city => GetWeatherForCityAsync(city, ct)).ToList();
        
        await Task.WhenAll(osmTasks.Cast<Task>().Concat(weatherTasks));
        _logger.LogInformation("Parallel POI & Weather fetch tasks completed.");
        
        var attractionsResults = osmTasks.Select(t => t.Result).ToArray();
        var weatherResults = weatherTasks.Select(t => t.Result).ToArray();
        
        var attractions = attractionsResults.Where(result => result.IsSuccess).SelectMany(result => result.Value).ToList();
        var weather = weatherResults.Where(w => w != null).ToList();

        _logger.LogInformation("Collected {AttractionCount} total attractions and {WeatherCount} weather states across destinations.", attractions.Count, weather.Count);

        var context = new TripContext
        {
            UserPrompt = request.Prompt,
            Cities = mappedLocations,
            TripData = tripData,
            Attractions = attractions.Count == 0 ? null : attractions,
            Weather = weather.Count == 0 ? null : weather,
            NerSucceeded = true
        };

        _logger.LogInformation("Requesting structured itinerary generation from LLM Service.");
        var llmResult = await _illmService.GenerateAsync(BuildGeneratePlanLlmPrompt(context), ct);
        if (llmResult.IsFailure)
        {
            _logger.LogWarning("LLM Service generation failed: {Error}", llmResult.Error);
            return Result.Failure<TripPlanResponse>(llmResult.Error);
        }

        _logger.LogInformation("Parsing structured LLM itinerary response.");
        var plan = TryParseLlmResponse(llmResult.Value);
        if (plan == null)
        {
            _logger.LogWarning("First structured LLM parse failed. Retrying generation...");
            var retryResult = await _illmService.GenerateAsync(BuildGeneratePlanLlmPrompt(context), ct);
            if (retryResult.IsSuccess)
                plan = TryParseLlmResponse(retryResult.Value);

            if (plan == null)
            {
                _logger.LogError("Structured LLM parse failed after retry.");
                return Result.Failure<TripPlanResponse>(Error.External("TripPlan.ParseFailed", "Failed to parse the generated itinerary after retry. Please try again."));
            }
        }

        _logger.LogInformation("Successfully parsed LLM plan. Patching missing fields, activity details, and destination photo.");
        PatchMissingFields(plan, context);
        PatchActivityDetails(plan, context.Attractions);
        PatchDestinationImage(plan, context.Attractions);

        plan.Prompt = request.Prompt;
        _logger.LogInformation("=== [GENERATE TRIP PLAN SUCCESS] ===");
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

        plan.Prompt = request.UserPrompt;
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

    private static string? GetCountryForCity(string city)
    {
        foreach (var kvp in CountryToCities)
        {
            if (kvp.Key.Length > 3 && kvp.Value.Contains(city, StringComparer.OrdinalIgnoreCase))
            {
                return kvp.Key;
            }
        }

        foreach (var kvp in CountryToCities)
        {
            if (kvp.Value.Contains(city, StringComparer.OrdinalIgnoreCase))
            {
                return kvp.Key;
            }
        }

        return null;
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
            if (context.Attractions != null)
            {
                sb.AppendLine($"You MUST include at least {MinPoisPerDay} of these POIs per day in your itinerary. Use the exact names provided.");
                for (var i = 0; i < context.Attractions.Count; i++)
                {
                    var a = context.Attractions[i];
                    sb.AppendLine($"{i + 1}. {a.Name} (City: {a.City}, Category: {a.Category})");
                    if (a.Rating.HasValue)
                        sb.AppendLine($"   Rating: {a.Rating.Value}/5");
                }

                sb.AppendLine();
                sb.AppendLine("IMPORTANT: The list above may NOT include every famous landmark. If a world-famous or iconic attraction for this destination is missing from the list above (e.g. Eiffel Tower for Paris, Colosseum for Rome, Pyramids for Cairo), you MUST still include it in the itinerary.");
            }
            else
            {
                sb.AppendLine("No pre-extracted points of interest are available. You MUST recommend the most famous and iconic landmarks and attractions for each destination city from your own knowledge.");
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
                  ""name"": ""exact POI name from the list above when applicable, otherwise a well-known attraction name"",
                  ""city"": ""the city this activity is located in"",
                  ""description"": ""brief description of activity"",
                  ""googleMapsLink"": null,
                  ""website"": null,
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
        sb.AppendLine("1. Keep the JSON structure exact.");
        sb.AppendLine("2. Always output null for both 'googleMapsLink' and 'website' properties in all activity objects. These will be mapped automatically by our system.");
        sb.AppendLine("3. Divide the itinerary logically based on dates and weather conditions.");
        sb.AppendLine("4. Keep descriptions brief and helpful.");
        sb.AppendLine("5. Consider weather when planning outdoor vs indoor activities.");
        sb.AppendLine("6. Provide logical sequencing of places based on context.");
        sb.AppendLine("7. if budget (in numeric format), currency, duration or group size are null, fill them with what you find suitable for the destination");
        sb.AppendLine("8. Return ONLY the JSON, no other text.");
        sb.AppendLine("9. You MUST include activities from ALL the extracted destination cities. Divide the number of days equally among the cities if possible (e.g., for a 4-day trip to 2 cities, assign the first 2 days to the first city, and the next 2 days to the second city). Group days by city sequentially so the traveler does not jump back and forth between cities.");
        sb.AppendLine("10. Multi-City Transit Rule: For multi-city trips, when transitioning between different cities (e.g., Day 2 is Paris and Day 3 is Marseille), you MUST insert a transit block at the end of Day 2 or the start of Day 3. Set its \"type\" property to \"Transit\", \"name\" to something descriptive (e.g., \"Transit: Paris to Marseille by Train\"), \"city\" to the destination city, and \"description\" to practical advice (e.g., \"Board the high-speed TGV train from Gare de Lyon... duration 3 hours\"). Normal sightseeing/POIs MUST have \"type\" set to \"Activity\".");
        sb.AppendLine("11. Budget Allocation Rule: You MUST ensure that the sum of the 'approximateCost' values across all suggested activities and dining spots is highly reasonable and fits within the traveler's total budget. Tailor the experiences to match the budget tier (e.g. free/budget activities for low budgets, and luxury/premium dining for generous budgets).");
        sb.AppendLine("12. Famous Landmarks Rule: You MUST ensure that every world-famous, iconic landmark for the destination is included in the itinerary, even if it was not in the provided POI list. A trip to Paris without the Eiffel Tower, or Rome without the Colosseum, is unacceptable.");

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
        sb.AppendLine("You may:");
        sb.AppendLine("• Add new days with FULLY POPULATED activities, meals, and POIs when the user asks to extend the trip");
        sb.AppendLine("• Remove days when the user asks to shorten the trip");
        sb.AppendLine("• Modify dates/times of existing activities");
        sb.AppendLine("• Replace an existing activity with a different one");
        sb.AppendLine("• Remove or add activities within a day");
        sb.AppendLine("• Adjust activity details (description, cost, type) while keeping the activity");
        sb.AppendLine("• Reorder existing activities");
        sb.AppendLine("• Change budget, duration, group size, or other trip-level fields as requested");
        sb.AppendLine();
        sb.AppendLine("You may NOT:");
        sb.AppendLine("• Create an entirely different itinerary unrelated to the original trip");
        sb.AppendLine("• Make changes the user did NOT ask for");
        sb.AppendLine();
        
        sb.AppendLine("=== OUTPUT FORMAT ===");
        sb.AppendLine("Return ONLY valid JSON (no markdown, no explanation, no error messages) matching the exact schema of the current JSON travel itinerary.");
        sb.AppendLine();
        sb.AppendLine("IMPORTANT RULES:");
        sb.AppendLine("1. Keep the same JSON schema/keys. You MAY add or remove items from the \"days\" array if the user explicitly asks to extend or shorten the trip.");
        sb.AppendLine("2. When ADDING new days, each new day MUST be fully populated with at least 3 activities (with name, description, googleMapsLink, approximateCost, type), and a meals object (breakfast, lunch, dinner). NEVER add an empty day.");
        sb.AppendLine("3. For any new or replaced activity, generate a Google Maps search link using this exact format: https://www.google.com/maps/search/?api=1&query=Activity+Name,+City+Name (replace spaces with +). If you don't know the website, set website to null.");
        sb.AppendLine("4. Apply ONLY the user's specific instruction. Do NOT make any additional changes to parts the user didn't mention.");
        sb.AppendLine("5. Preserve all unchanged activities, days, and trip details exactly as they are.");
        sb.AppendLine("6. If the trip duration field exists, update it to match the new number of days (e.g., \"3 days\" if there are now 3 days).");
        sb.AppendLine("7. Return ONLY the refined JSON. If the refinement is impossible or the request is out of scope, return: {\"error\": \"Refinement not possible: [reason]\"}");

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
        // Remove literal control characters (newlines/tabs) that the LLM might embed
        // inside JSON string values — these cause JsonException on parse.
        sanitized = sanitized.Replace("\r\n", " ").Replace("\r", " ");
        // Collapse runs of newlines inside values into single spaces, but keep JSON structure.
        // We do this by joining all lines and letting the JSON parser handle it.
        var lines = sanitized.Split('\n');
        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            sb.Append(line.TrimEnd());
            // Only add a newline if the line ends with a JSON structural char or is a closing brace/bracket
            var trimmed = line.TrimEnd();
            if (trimmed.EndsWith(',') || trimmed.EndsWith('{') || trimmed.EndsWith('[') 
                || trimmed.EndsWith('}') || trimmed.EndsWith(']') || trimmed.EndsWith(':'))
                sb.Append('\n');
            else
                sb.Append(' ');
        }
        return sb.ToString().Trim();
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

    private static void PatchActivityDetails(TripPlanResponse plan, List<OsmAttractionDto>? attractions)
    {
        var photoLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var mapsLinkLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var websiteLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (attractions != null)
        {
            foreach (var a in attractions)
            {
                if (!string.IsNullOrWhiteSpace(a.PhotoUrl) && !photoLookup.ContainsKey(a.Name))
                    photoLookup[a.Name] = a.PhotoUrl;
                if (!string.IsNullOrWhiteSpace(a.GoogleMapsLink) && !mapsLinkLookup.ContainsKey(a.Name))
                    mapsLinkLookup[a.Name] = a.GoogleMapsLink;
                if (!string.IsNullOrWhiteSpace(a.Website) && !websiteLookup.ContainsKey(a.Name))
                    websiteLookup[a.Name] = a.Website;
            }
        }

        foreach (var day in plan.Days)
        {
            foreach (var activity in day.Activities)
            {
                if (string.IsNullOrWhiteSpace(activity.ImageUrl) && photoLookup.TryGetValue(activity.Name, out var photoUrl))
                {
                    activity.ImageUrl = photoUrl;
                }

                if (string.IsNullOrWhiteSpace(activity.Website) && websiteLookup.TryGetValue(activity.Name, out var website))
                {
                    activity.Website = website;
                }

                if (mapsLinkLookup.TryGetValue(activity.Name, out var mapsLink) && !string.IsNullOrWhiteSpace(mapsLink))
                {
                    activity.GoogleMapsLink = mapsLink;
                }
                else if (string.IsNullOrWhiteSpace(activity.GoogleMapsLink) || activity.GoogleMapsLink == "null")
                {
                    var queryCity = activity.City ?? plan.City;
                    activity.GoogleMapsLink = $"https://www.google.com/maps/search/?api=1&query={Uri.EscapeDataString(activity.Name + ", " + queryCity)}";
                }
            }
        }
    }

    private static void PatchDestinationImage(TripPlanResponse plan, List<OsmAttractionDto>? attractions)
    {
        if (!string.IsNullOrWhiteSpace(plan.ImageUrl)) return;

        var firstPhoto = attractions?
            .FirstOrDefault(a => !string.IsNullOrWhiteSpace(a.PhotoUrl));

        if (firstPhoto != null)
        {
            plan.ImageUrl = firstPhoto.PhotoUrl;
        }
    }

    private static int ParseDurationToDays(string? durationStr)
    {
        if (string.IsNullOrWhiteSpace(durationStr)) return 3;

        var match = Regex.Match(durationStr, @"\d+");
        if (match.Success && int.TryParse(match.Value, out var val))
        {
            if (durationStr.Contains("week", StringComparison.OrdinalIgnoreCase))
                return val * 7;
            if (durationStr.Contains("month", StringComparison.OrdinalIgnoreCase))
                return val * 30;
            return val;
        }
        return 3;
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
