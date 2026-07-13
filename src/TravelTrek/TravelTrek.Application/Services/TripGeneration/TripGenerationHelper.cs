using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TravelTrek.Application.DTOs.Ner;
using TravelTrek.Application.DTOs.Osm;
using TravelTrek.Application.DTOs.TripPlanner;
namespace TravelTrek.Application.Services.TripGeneration;

internal static class TripGenerationHelper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    
    internal static string BuildGeneratePlanLlmPrompt(TripContext context)
    {
        var sb = new StringBuilder();

        sb.AppendLine("You are an expert travel planner. Generate a detailed day-by-day trip itinerary in valid JSON format.");
        sb.AppendLine();

        sb.AppendLine("=== USER REQUEST ===");
        sb.AppendLine(context.UserPrompt);
        sb.AppendLine();

 
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
            sb.AppendLine($"You MUST include at least 3 of these POIs per day in your itinerary. Use the exact names provided.");
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

        sb.AppendLine("=== AVAILABLE DINING OPTIONS (RESTAURANTS/CAFES/BARS) ===");
        if (context.Dining != null && context.Dining.Count > 0)
        {
            sb.AppendLine("You MUST use ONLY the exact restaurant/cafe/bakery names from this list for the 'breakfast', 'lunch', and 'dinner' options under the 'meals' object. Assign them logically (e.g. cafe/bakery for breakfast, restaurant/cafe for lunch, and restaurant/bar for dinner). Do NOT invent any restaurant names.");
            for (var i = 0; i < context.Dining.Count; i++)
            {
                var d = context.Dining[i];
                sb.AppendLine($"{i + 1}. {d.Name} (City: {d.City}, Category: {d.Category})");
                if (d.Rating.HasValue)
                    sb.AppendLine($"   Rating: {d.Rating.Value}/5");
            }
        }
        else
        {
            sb.AppendLine("No pre-extracted dining options are available. Recommend popular restaurants or local dining spots matching the budget and destination city from your own knowledge.");
        }
        sb.AppendLine();
        
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
                  ""type"": ""'Activity' or 'Transit'"",
                  ""time"": ""what time to start this activity (e.g. '09:00 AM', '02:00 PM', '07:30 PM')""
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
        sb.AppendLine("7. if budget (in numeric format) or currency are null or not specified in the user request, you MUST generate a suitable budget and set its currency to the local/national currency of the destination country (e.g. EUR for France, JPY for Japan, EGP for Egypt, GBP for United Kingdom, AED for United Arab Emirates). The budget numeric value MUST represent the amount in that local currency, not USD. If duration or group size are null, fill them with what you find suitable for the destination.");
        sb.AppendLine("8. Return ONLY the JSON, no other text.");
        sb.AppendLine("9. You MUST include activities from ALL the extracted destination cities. Divide the number of days equally among the cities if possible (e.g., for a 4-day trip to 2 cities, assign the first 2 days to the first city, and the next 2 days to the second city). Group days by city sequentially so the traveler does not jump back and forth between cities.");
        sb.AppendLine("10. Multi-City Transit Rule: For multi-city trips, when transitioning between different cities (e.g., Day 2 is Paris and Day 3 is Marseille), you MUST insert a transit block at the end of Day 2 or the start of Day 3. Set its \"type\" property to \"Transit\", \"name\" to something descriptive (e.g., \"Transit: Paris to Marseille by Train\"), \"city\" to the destination city, and \"description\" to practical advice (e.g., \"Board the high-speed TGV train from Gare de Lyon... duration 3 hours\"). Normal sightseeing/POIs MUST have \"type\" set to \"Activity\".");
        sb.AppendLine("11. Budget Allocation Rule: You MUST ensure that the sum of the 'approximateCost' values across all suggested activities and dining spots is highly reasonable and fits within the traveler's total budget. Tailor the experiences to match the budget tier (e.g. free/budget activities for low budgets, and luxury/premium dining for generous budgets).");
        sb.AppendLine("12. Famous Landmarks Rule: You MUST ensure that every world-famous, iconic landmark for the destination is included in the itinerary, even if it was not in the provided POI list. A trip to Paris without the Eiffel Tower, or Rome without the Colosseum, is unacceptable.");
        sb.AppendLine("13. Activity Timing Rule: You MUST assign a realistic start time for each activity in the day (e.g., '09:00 AM', '11:30 AM', '02:00 PM', '05:00 PM', '08:00 PM'). Ensure the times are chronologically sequenced and leave reasonable gaps for travel, meals, and resting.");
        sb.AppendLine("14. Dining Suggestion Rule: For the 'meals' object (breakfast, lunch, dinner), you MUST select the exact restaurant/cafe names from the 'AVAILABLE DINING OPTIONS' section instead of inventing/assuming them. Assign them logically based on the type (e.g. cafes for breakfast/lunch, restaurants for lunch/dinner).");

        return sb.ToString();
    }
    internal static string BuildRefinePlanLlmPrompt(string tripPlan, string userPrompt)
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

    internal static TripPlanResponse? TryParseLlmResponse(string llmResponse)
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
            catch (JsonException)
            {
                return null;
            }
        }
    }
    internal static string ExtractJson(string text)
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
        
        // remove Trailing commas
        var sanitized = Regex.Replace(json, @",\s*([}\]])", "$1");
        // remove comments (//)
        sanitized = Regex.Replace(sanitized, @"//.*?$", "", RegexOptions.Multiline);
        // remove literal control characters (newlines/tabs) that the LLM might embed
        sanitized = sanitized.Replace("\r\n", " ").Replace("\r", " ");
        // Collapse runs of newlines inside values into single spaces, but keep JSON structure.
        // We do this by joining all lines and letting the JSON parser handle it.
        var lines = sanitized.Split('\n');
        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            sb.Append(line.TrimEnd());
            var trimmed = line.TrimEnd();
            if (trimmed.EndsWith(',') || trimmed.EndsWith('{') || trimmed.EndsWith('[') 
                || trimmed.EndsWith('}') || trimmed.EndsWith(']') || trimmed.EndsWith(':'))
                sb.Append('\n');
            else
                sb.Append(' ');
        }
        return sb.ToString().Trim();
    }
    
    internal static void PatchMissingFields(TripPlanResponse plan, TripContext context)
    {
        if (string.IsNullOrWhiteSpace(plan.City) && context.Cities != null) plan.City = string.Join(", ", context.Cities);
        if (string.IsNullOrWhiteSpace(plan.Duration) && context.TripData != null) plan.Duration = context.TripData.Durations.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(plan.GroupSize) && context.TripData != null) plan.GroupSize = context.TripData.GroupSizes.FirstOrDefault();
        
        if (context.Weather != null)
        {
            plan.Weather ??= context.Weather.FirstOrDefault(w => w != null);
        }
    }

    internal static void PatchActivityDetails(TripPlanResponse plan, List<PoiDto>? attractions)
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

    internal static void PatchDestinationImage(TripPlanResponse plan, List<PoiDto>? attractions)
    {
        if (!string.IsNullOrWhiteSpace(plan.ImageUrl)) return;

        var firstPhoto = attractions?
            .FirstOrDefault(a => !string.IsNullOrWhiteSpace(a.PhotoUrl));

        if (firstPhoto != null)
        {
            plan.ImageUrl = firstPhoto.PhotoUrl;
        }
    }
    
    internal static ExtractedTripData ParseTripData(List<NerEntity> entities)
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
    
    internal static string? GetCountryForCity(string city)
    {
        foreach (var kvp in TripDictionaries.CountryToCities)
        {
            if (kvp.Key.Length > 3 && kvp.Value.Contains(city, StringComparer.OrdinalIgnoreCase))
            {
                return kvp.Key;
            }
        }

        foreach (var kvp in TripDictionaries.CountryToCities)
        {
            if (kvp.Value.Contains(city, StringComparer.OrdinalIgnoreCase))
            {
                return kvp.Key;
            }
        }

        return null;
    }
    internal static string GetCurrencyForCountry(string? countryName)
    {
        if (string.IsNullOrWhiteSpace(countryName))
            return "USD";

        return TripDictionaries.CountryToCurrencyMap.GetValueOrDefault(countryName.Trim(), "USD");
    }
    
}