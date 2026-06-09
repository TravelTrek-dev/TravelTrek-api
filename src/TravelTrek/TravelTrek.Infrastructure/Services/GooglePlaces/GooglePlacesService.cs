using System.Globalization;
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

    private const string NearbySearchUrl = "https://places.googleapis.com/v1/places:searchNearby";
    private const string TextSearchUrl = "https://places.googleapis.com/v1/places:searchText";

    // field masks to only get what i need.
    private const string NearbyFieldMask =
        "places.displayName,places.location,places.types,places.rating,places.websiteUri,places.photos,places.userRatingCount,places.googleMapsUri";

    private const string TextSearchFieldMask =
        "places.location,places.types,places.displayName,places.formattedAddress";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly HashSet<string> CityTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "locality",
        "political",
        "administrative_area_level_1",
        "administrative_area_level_2",
        "administrative_area_level_3",
        "colloquial_area",
        "country"
    };

    // map google places types to user-friendly names
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
    };

    public GooglePlacesService(HttpClient httpClient, IOptions<GooglePlacesOptions> options, ILogger<GooglePlacesService> logger, ICacheService cache)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _cache = cache;
    }

    public async Task<Result<List<OsmAttractionDto>>> GetTopAttractionsAsync(string cityName, int limit = 40, CancellationToken ct = default)
    {
        var cacheKey = $"poi:{cityName.ToLowerInvariant().Trim()}:{limit}";

        var cached = await _cache.GetAsync<List<OsmAttractionDto>>(cacheKey, ct);
        if (cached != null)
        {
            _logger.LogInformation("Cache HIT for POIs of '{City}' (limit={Limit}). Returning cached results.", cityName, limit);
            return Result.Success(cached);
        }

        _logger.LogInformation("Cache MISS for POIs of '{City}' (limit={Limit}). Initiating Google Places retrieval.", cityName, limit);

        var coordsResult = await GeocodeCityAsync(cityName, ct);
        if (coordsResult.IsFailure)
        {
            _logger.LogError("Failed to geocode city '{City}' via Google Places.", cityName);
            return Result.Failure<List<OsmAttractionDto>>(coordsResult.Error);
        }

        var (lat, lon) = coordsResult.Value;
        _logger.LogInformation("Geocoded '{City}' to ({Lat}, {Lon})", cityName, lat, lon);

        var attractions = await SearchNearbyAttractionsAsync(cityName, lat, lon, limit, ct);

        if (attractions.Count == 0)
        {
            _logger.LogWarning("Google Places returned 0 attractions for '{City}'.", cityName);
            return Result.Failure<List<OsmAttractionDto>>(Error.External("GetTopAttractions.External", $"No attractions found for '{cityName}' via Google Places."));
        }

        _logger.LogInformation("Successfully fetched {Count} POIs from Google Places for '{City}'. Saving to cache.", attractions.Count, cityName);
        await _cache.SetAsync(cacheKey, attractions, TimeSpan.FromHours(24), ct);
        return Result.Success(attractions);
    }

    private async Task<Result<(double Lat, double Lon)>> GeocodeCityAsync(string cityName, CancellationToken ct)
    {
        var geoCacheKey = $"geo:{cityName.ToLowerInvariant().Trim()}";
        var cached = await _cache.GetAsync<double[]>(geoCacheKey, ct);
        if (cached is { Length: 2 })
        {
            _logger.LogInformation("Cache HIT for geocoding '{City}': ({Lat}, {Lon})", cityName, cached[0], cached[1]);
            return Result.Success((cached[0], cached[1]));
        }

        _logger.LogInformation("Cache MISS for geocoding '{City}'. Querying Google Places Text Search API.", cityName);

        // 1. try with the original name
        var searchResult = await SearchPlacesAsync(cityName, ct);
        if (searchResult.IsSuccess && searchResult.Value.Count > 0)
        {
            var cityPlace = searchResult.Value.FirstOrDefault(p => p.Types.Any(t => CityTypes.Contains(t)));
            if (cityPlace != null)
            {
                _logger.LogInformation("Geocoded '{City}' to '{DisplayName}' ({Address}) at ({Lat}, {Lon}) via CityTypes match.",
                    cityName, cityPlace.DisplayName, cityPlace.FormattedAddress, cityPlace.Lat, cityPlace.Lon);
                await _cache.SetAsync(geoCacheKey, new[] { cityPlace.Lat, cityPlace.Lon }, TimeSpan.FromDays(7), ct);
                return Result.Success((cityPlace.Lat, cityPlace.Lon));
            }
        }

        // 2. try with name + " city" 
        if (!cityName.Contains("city", StringComparison.OrdinalIgnoreCase) &&
            !cityName.Contains("country", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("No city/political type found for '{City}', retrying with '{City} city'...", cityName, cityName);
            var citySearchResult = await SearchPlacesAsync(cityName + " city", ct);
            if (citySearchResult.IsSuccess && citySearchResult.Value.Count > 0)
            {
                var cityPlace = citySearchResult.Value.FirstOrDefault(p => p.Types.Any(t => CityTypes.Contains(t)));
                if (cityPlace != null)
                {
                    _logger.LogInformation("Geocoded '{City}' to '{DisplayName}' ({Address}) at ({Lat}, {Lon}) via city retry.",
                        cityName, cityPlace.DisplayName, cityPlace.FormattedAddress, cityPlace.Lat, cityPlace.Lon);
                    await _cache.SetAsync(geoCacheKey, new[] { cityPlace.Lat, cityPlace.Lon }, TimeSpan.FromDays(7), ct);
                    return Result.Success((cityPlace.Lat, cityPlace.Lon));
                }
            }
        }

        // 3. fallback:
        if (searchResult.IsSuccess && searchResult.Value.Count > 0)
        {
            var fallbackPlace = searchResult.Value[0];
            _logger.LogWarning("Could not find a strict city/political type for '{City}'. Falling back to first search result: '{DisplayName}' at ({Lat}, {Lon})",
                cityName, fallbackPlace.DisplayName, fallbackPlace.Lat, fallbackPlace.Lon);
            await _cache.SetAsync(geoCacheKey, new[] { fallbackPlace.Lat, fallbackPlace.Lon }, TimeSpan.FromDays(7), ct);
            return Result.Success((fallbackPlace.Lat, fallbackPlace.Lon));
        }

        return Result.Failure<(double, double)>(Error.NotFound("GooglePlaces.GeocodeFailed", $"Could not geocode '{cityName}'."));
    }

    private async Task<Result<List<GeocodedPlace>>> SearchPlacesAsync(string query, CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Sending POST to Google Places Text Search: '{Url}' | Query: '{Query}'", TextSearchUrl, query);
            var requestBody = new { textQuery = query };
            var json = JsonSerializer.Serialize(requestBody, JsonOptions);
            using var request = new HttpRequestMessage(HttpMethod.Post, TextSearchUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-Goog-Api-Key", _options.ApiKey);
            request.Headers.Add("X-Goog-FieldMask", TextSearchFieldMask);

            var response = await _httpClient.SendAsync(request, ct);
            var responseStr = await response.Content.ReadAsStringAsync(ct);

            _logger.LogInformation("Google Places Text Search responded with status: {StatusCode}", response.StatusCode);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Google Places Text Search returned {Status}: {Body}", response.StatusCode, responseStr);
                return Result.Failure<List<GeocodedPlace>>(
                    Error.External("GooglePlaces.GeocodeFailed", $"Text Search API returned {response.StatusCode}."));
            }

            using var doc = JsonDocument.Parse(responseStr);
            if (!doc.RootElement.TryGetProperty("places", out var places) || places.GetArrayLength() == 0)
            {
                return Result.Success(new List<GeocodedPlace>());
            }

            var resultList = new List<GeocodedPlace>();
            foreach (var place in places.EnumerateArray())
            {
                if (place.TryGetProperty("location", out var location))
                {
                    var lat = location.GetProperty("latitude").GetDouble();
                    var lon = location.GetProperty("longitude").GetDouble();

                    var displayName = "";
                    if (place.TryGetProperty("displayName", out var dn) && dn.TryGetProperty("text", out var txt))
                    {
                        displayName = txt.GetString() ?? "";
                    }
                    var formattedAddress = place.TryGetProperty("formattedAddress", out var fa) ? fa.GetString() ?? "" : "";

                    var types = new List<string>();
                    if (place.TryGetProperty("types", out var typesProp))
                    {
                        foreach (var type in typesProp.EnumerateArray())
                        {
                            var t = type.GetString();
                            if (t != null) types.Add(t);
                        }
                    }

                    resultList.Add(new GeocodedPlace
                    {
                        Lat = lat,
                        Lon = lon,
                        DisplayName = displayName,
                        FormattedAddress = formattedAddress,
                        Types = types
                    });
                }
            }

            return Result.Success(resultList);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error searching places for query '{Query}' via Google Places.", query);
            return Result.Failure<List<GeocodedPlace>>(
                Error.External("GooglePlaces.GeocodeError", ex.Message));
        }
    }

    private class GeocodedPlace
    {
        public double Lat { get; set; }
        public double Lon { get; set; }
        public string DisplayName { get; set; } = "";
        public string FormattedAddress { get; set; } = "";
        public List<string> Types { get; set; } = new();
    }

    private async Task<List<OsmAttractionDto>> SearchNearbyAttractionsAsync(string cityName, double lat, double lon, int limit, CancellationToken ct)
    {
        var attractions = new List<OsmAttractionDto>();

        try
        {
            _logger.LogInformation("Sending POST to Google Places Nearby Search: '{Url}' | Center: ({Lat}, {Lon}) | Radius: 15km", NearbySearchUrl, lat, lon);
            var maxResults = Math.Min(limit, 20);

            var requestBody = new
            {
                includedTypes = new[]
                {
                    "tourist_attraction", "museum", "art_gallery", "church",
                    "mosque", "hindu_temple", "historical_landmark",
                    "national_park", "amusement_park", "aquarium", "zoo"
                },
                maxResultCount = maxResults,
                rankPreference = "POPULARITY",
                locationRestriction = new
                {
                    circle = new
                    {
                        center = new
                        {
                            latitude = lat,
                            longitude = lon
                        },
                        radius = 15000.0 
                    }
                }
            };

            var json = JsonSerializer.Serialize(requestBody, JsonOptions);
            using var request = new HttpRequestMessage(HttpMethod.Post, NearbySearchUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-Goog-Api-Key", _options.ApiKey);
            request.Headers.Add("X-Goog-FieldMask", NearbyFieldMask);

            var response = await _httpClient.SendAsync(request, ct);
            var responseStr = await response.Content.ReadAsStringAsync(ct);

            _logger.LogInformation("Google Places Nearby Search responded with status: {StatusCode}", response.StatusCode);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Google Places Nearby Search returned {Status}: {Body}", response.StatusCode, responseStr);
                return attractions;
            }

            using var doc = JsonDocument.Parse(responseStr);
            if (!doc.RootElement.TryGetProperty("places", out var places))
            {
                return attractions;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var place in places.EnumerateArray())
            {
                var name = GetDisplayName(place);
                if (string.IsNullOrWhiteSpace(name) || name.Length < 3 || seen.Contains(name))
                    continue;

                seen.Add(name);

                var rating = place.TryGetProperty("rating", out var ratingProp) ? ratingProp.GetDouble() : 0;
                var ratingCount = place.TryGetProperty("userRatingCount", out var rcProp) ? rcProp.GetInt32() : 0;

                var score = (int)(rating * 2) + (ratingCount > 1000 ? 5 : ratingCount > 100 ? 3 : 1);

                var category = GetCategory(place);
                var website = place.TryGetProperty("websiteUri", out var wsProp) ? wsProp.GetString() : null;
                var googleMapsLink = place.TryGetProperty("googleMapsUri", out var gmProp)
                    ? gmProp.GetString() ?? BuildGoogleMapsLink(lat, lon)
                    : BuildGoogleMapsLink(lat, lon);

                var photoUrl = GetPhotoUrl(place);

                var placeLat = lat;
                var placeLon = lon;
                if (place.TryGetProperty("location", out var loc))
                {
                    placeLat = loc.TryGetProperty("latitude", out var pLat) ? pLat.GetDouble() : lat;
                    placeLon = loc.TryGetProperty("longitude", out var pLon) ? pLon.GetDouble() : lon;
                }

                if (googleMapsLink == BuildGoogleMapsLink(lat, lon))
                {
                    googleMapsLink = BuildGoogleMapsLink(placeLat, placeLon);
                }

                attractions.Add(new OsmAttractionDto
                {
                    Name = name,
                    City = cityName,
                    Category = category,
                    Score = score,
                    GoogleMapsLink = googleMapsLink,
                    Website = website,
                    PhotoUrl = photoUrl,
                    Rating = rating > 0 ? rating : null
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error fetching nearby attractions for '{City}'.", cityName);
        }

        return attractions.OrderByDescending(a => a.Score).Take(limit).ToList();
    }

    private static string GetDisplayName(JsonElement place)
    {
        if (place.TryGetProperty("displayName", out var displayName))
        {
            if (displayName.TryGetProperty("text", out var text))
                return text.GetString() ?? "";
        }
        return "";
    }

    private static string GetCategory(JsonElement place)
    {
        if (place.TryGetProperty("types", out var types))
        {
            foreach (var type in types.EnumerateArray())
            {
                var typeStr = type.GetString() ?? "";
                if (TypeToCategory.TryGetValue(typeStr, out var category))
                    return category;
            }
        }
        return "Attraction";
    }

    private string? GetPhotoUrl(JsonElement place)
    {
        if (place.TryGetProperty("photos", out var photos) && photos.GetArrayLength() > 0)
        {
            var firstPhoto = photos[0];
            if (firstPhoto.TryGetProperty("name", out var nameProp))
            {
                var photoName = nameProp.GetString();
                if (!string.IsNullOrEmpty(photoName))
                {
                    return $"https://places.googleapis.com/v1/{photoName}/media?maxWidthPx=800&key={_options.ApiKey}";
                }
            }
        }
        return null;
    }

    private static string BuildGoogleMapsLink(double lat, double lon)
    {
        return $"https://www.google.com/maps/search/?api=1&query={lat.ToString(CultureInfo.InvariantCulture)},{lon.ToString(CultureInfo.InvariantCulture)}";
    }
}
