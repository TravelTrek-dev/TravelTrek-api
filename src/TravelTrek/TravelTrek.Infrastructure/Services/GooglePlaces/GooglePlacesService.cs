using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TravelTrek.Application.DTOs.Osm;
using TravelTrek.Application.Interfaces;
using TravelTrek.Domain.Common;
using TravelTrek.Infrastructure.Data.Configurations;

namespace TravelTrek.Infrastructure.Services.GooglePlaces;

public class GooglePlacesService : IPoiService
{
    private readonly HttpClient _httpClient;
    private readonly GooglePlacesOptions _options;
    private readonly ILogger<GooglePlacesService> _logger;
    private readonly ICacheService _cache;

    private const string TextSearchUrl = "https://places.googleapis.com/v1/places:searchText";

    private const string FieldMask =
        "places.displayName,places.location,places.types,places.rating,places.websiteUri,places.photos,places.userRatingCount,places.googleMapsUri";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly Dictionary<string, string> TypeToCategory = new(StringComparer.OrdinalIgnoreCase)
    {
        ["tourist_attraction"] = "Attraction",
        ["museum"] = "Museum",
        ["art_gallery"] = "Gallery",
        ["church"] = "Church",
        ["mosque"] = "Mosque",
        ["hindu_temple"] = "Temple",
        ["synagogue"] = "Synagogue",
        ["historical_landmark"] = "Monument",
        ["national_park"] = "Park",
        ["park"] = "Park",
        ["amusement_park"] = "Amusement Park",
        ["aquarium"] = "Aquarium",
        ["zoo"] = "Zoo",
        ["performing_arts_theater"] = "Theater",
        ["stadium"] = "Stadium",
        ["shopping_mall"] = "Shopping",
        ["market"] = "Market",
        ["restaurant"] = "Restaurant",
        ["cafe"] = "Cafe",
        ["bar"] = "Bar",
        ["bakery"] = "Bakery",
    };

    public GooglePlacesService(HttpClient httpClient, IOptions<GooglePlacesOptions> options, ILogger<GooglePlacesService> logger, ICacheService cache)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _cache = cache;
    }

    public Task<Result<List<PoiDto>>> GetTopAttractionsAsync(string cityName, int limit = 40, CancellationToken ct = default) =>
        GetPoisAsync("poi", cityName, $"top tourist attractions in {cityName}", "Attraction", limit, ct);

    public Task<Result<List<PoiDto>>> GetTopDiningAsync(string cityName, int limit = 40, CancellationToken ct = default) =>
        GetPoisAsync("dining", cityName, $"best restaurants and cafes in {cityName}", "Restaurant", limit, ct);

    private async Task<Result<List<PoiDto>>> GetPoisAsync(string cachePrefix, string cityName, string query, string fallbackCategory, int limit, CancellationToken ct)
    {
        var cacheKey = $"{cachePrefix}:{cityName.ToLowerInvariant().Trim()}:{limit}";
        var cached = await _cache.GetAsync<List<PoiDto>>(cacheKey, ct);
        if (cached != null)
        {
            return Result.Success(cached);
        }

        var pois = await TextSearchPlacesAsync(cityName, query, fallbackCategory, limit, ct);

        if (pois.Count == 0)
            return Result.Failure<List<PoiDto>>(Error.External($"{cachePrefix}.External", $"No places found for '{cityName}' via Google Places."));

        await _cache.SetAsync(cacheKey, pois, TimeSpan.FromHours(24), ct);
        return Result.Success(pois);
    }

    private async Task<List<PoiDto>> TextSearchPlacesAsync(string cityName, string query, string fallbackCategory, int limit, CancellationToken ct)
    {
        var results = new List<PoiDto>();

        try
        {
            var requestBody = new
            {
                textQuery = query,
                pageSize = Math.Min(limit, 20),
                rankPreference = "RELEVANCE"
            };

            var json = JsonSerializer.Serialize(requestBody, JsonOptions);
            using var request = new HttpRequestMessage(HttpMethod.Post, TextSearchUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-Goog-Api-Key", _options.ApiKey);
            request.Headers.Add("X-Goog-FieldMask", FieldMask);

            var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                return results;
            }

            var parsed = await response.Content.ReadFromJsonAsync<TextSearchResponse>(JsonOptions, ct);
            if (parsed?.Places == null) return results;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var place in parsed.Places)
            {
                var name = place.DisplayName?.Text ?? "";
                if (string.IsNullOrWhiteSpace(name) || name.Length < 3 || !seen.Add(name))
                    continue;

                var rating = place.Rating ?? 0;
                var ratingCount = place.UserRatingCount ?? 0;
                var score = (int)(rating * 2) + (ratingCount > 1000 ? 5 : ratingCount > 100 ? 3 : 1);

                var category = place.Types?
                    .Select(t => TypeToCategory.GetValueOrDefault(t))
                    .FirstOrDefault(c => c != null) ?? fallbackCategory;

                var lat = place.Location?.Latitude ?? 0;
                var lon = place.Location?.Longitude ?? 0;
                var googleMapsLink = place.GoogleMapsUri ?? BuildGoogleMapsLink(lat, lon);

                results.Add(new PoiDto
                {
                    Name = name,
                    City = cityName,
                    Category = category!,
                    Score = score,
                    GoogleMapsLink = googleMapsLink,
                    Website = place.WebsiteUri,
                    PhotoUrl = GetPhotoUrl(place.Photos),
                    Rating = rating > 0 ? rating : null
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error searching places for '{City}' ('{Query}').", cityName, query);
        }

        return results.OrderByDescending(r => r.Score).Take(limit).ToList();
    }

    private string? GetPhotoUrl(List<PhotoDto>? photos)
    {
        var photoName = photos?.FirstOrDefault()?.Name;
        return string.IsNullOrEmpty(photoName)
            ? null
            : $"https://places.googleapis.com/v1/{photoName}/media?maxWidthPx=800&key={_options.ApiKey}";
    }

    private static string BuildGoogleMapsLink(double lat, double lon) =>
        $"https://www.google.com/maps/search/?api=1&query={lat.ToString(CultureInfo.InvariantCulture)},{lon.ToString(CultureInfo.InvariantCulture)}";
    
}