using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TravelTrek.Application.DTOs.Osm;
using TravelTrek.Application.Interfaces;
using TravelTrek.Domain.Common;
using TravelTrek.Infrastructure.Data.Configurations;

namespace TravelTrek.Infrastructure.Services.Osm;

public class OsmService : IPoiService
{
    private readonly HttpClient _httpClient;
    private readonly OsmApiOptions _options;
    private readonly IOpenWeatherService _weatherService;
    private readonly ILogger<OsmService> _logger;
    private readonly ICacheService _cache;

    private class CityCoordinates
    {
        public double Lat { get; set; }
        public double Lon { get; set; }
    }

    public OsmService(HttpClient httpClient, IOptions<OsmApiOptions> options, IOpenWeatherService weatherService, ILogger<OsmService> logger, ICacheService cache)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _weatherService = weatherService;
        _logger = logger;
        _cache = cache;
    }

    private async Task<Result<CityCoordinates>> GetCoordinatesAsync(string cityName, CancellationToken ct)
    {
        var cacheKey = $"geo:{cityName.ToLowerInvariant().Trim()}";

        var cached = await _cache.GetAsync<CityCoordinates>(cacheKey, ct);
        if (cached != null)
        {
            _logger.LogInformation("Cache hit for geocoding '{City}'.", cityName);
            return Result.Success(cached);
        }

        // 1. Try Nominatim Geocoding (OSM)
        try
        {
            _logger.LogInformation("Attempting Nominatim geocoding for city: {City}", cityName);
            var geoUrl = $"{_options.NominatimBaseUrl}search?q={Uri.EscapeDataString(cityName)}&format=json&limit=1";
            
            using var request = new HttpRequestMessage(HttpMethod.Get, geoUrl);
            request.Headers.Add("User-Agent", "TravelApp/1.0 (educational project)");
            
            var geoResponse = await _httpClient.SendAsync(request, ct);
 
            if (geoResponse.IsSuccessStatusCode)
            {
                var geoDataStr = await geoResponse.Content.ReadAsStringAsync(ct);
                using var geoDoc = JsonDocument.Parse(geoDataStr);
                if (geoDoc.RootElement.GetArrayLength() > 0)
                {
                    var firstResult = geoDoc.RootElement[0];
                    if (firstResult.TryGetProperty("lat", out var latProp) && firstResult.TryGetProperty("lon", out var lonProp))
                    {
                        var latStr = latProp.GetString();
                        var lonStr = lonProp.GetString();
 
                        if (!string.IsNullOrEmpty(latStr) && !string.IsNullOrEmpty(lonStr) &&
                            double.TryParse(latStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var lat) &&
                            double.TryParse(lonStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var lon))
                        {
                            _logger.LogInformation("Nominatim geocoding successful for {City}: Lat={Lat}, Lon={Lon}", cityName, lat, lon);
                            var result = new CityCoordinates { Lat = lat, Lon = lon };
                            await _cache.SetAsync(cacheKey, result, TimeSpan.FromDays(7), ct);
                            return Result.Success(result);
                        }
                    }
                }
            }
            _logger.LogWarning("Nominatim geocoding failed or returned empty for {City}.", cityName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during Nominatim geocoding for {City}.", cityName);
        }
 
        // 2. Fallback to OpenWeather Geocoding
        try
        {
            _logger.LogInformation("Falling back to OpenWeather geocoding for city: {City}", cityName);
            var openWeatherResult = await _weatherService.GeocodeByNameAsync(cityName, ct);
            if (openWeatherResult.IsSuccess)
            {
                var geo = openWeatherResult.Value;
                _logger.LogInformation("OpenWeather geocoding successful for {City}: Lat={Lat}, Lon={Lon}", cityName, geo.Lat, geo.Lon);
                var result = new CityCoordinates { Lat = geo.Lat, Lon = geo.Lon };
                await _cache.SetAsync(cacheKey, result, TimeSpan.FromDays(7), ct);
                return Result.Success(result);
            }
            _logger.LogWarning("OpenWeather geocoding failed: {Error}", openWeatherResult.Error);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during fallback OpenWeather geocoding for {City}.", cityName);
        }
 
        return Result.Failure<CityCoordinates>(Error.NotFound("OsmService.GeocodeFailed", $"Failed to geocode city '{cityName}' using all available services."));
    }
 
    public async Task<Result<List<OsmAttractionDto>>> GetTopAttractionsAsync(string cityName, int limit = 40, CancellationToken ct = default)
    {
        var cacheKey = $"poi:{cityName.ToLowerInvariant().Trim()}:{limit}";

        var cached = await _cache.GetAsync<List<OsmAttractionDto>>(cacheKey, ct);
        if (cached != null)
        {
            _logger.LogInformation("Cache hit for POIs '{City}' (limit={Limit}).", cityName, limit);
            return Result.Success(cached);
        }

        var coordsResult = await GetCoordinatesAsync(cityName, ct);
        if (coordsResult.IsFailure)
        {
            _logger.LogError("Failed to resolve coordinates for {City}.", cityName);
            return Result.Failure<List<OsmAttractionDto>>(coordsResult.Error);
        }
 
        var coords = coordsResult.Value;
        var latStr = coords.Lat.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var lonStr = coords.Lon.ToString(System.Globalization.CultureInfo.InvariantCulture);
 
        // osm
        var overpassQuery = $@"
            [out:json][timeout:30][maxsize:10485760];
            (
              node[""tourism""~""museum|attraction|monument|gallery|castle|cathedral|viewpoint""](around:10000,{latStr},{lonStr});
              way[""tourism""~""museum|attraction|monument|gallery|castle|cathedral|viewpoint""](around:10000,{latStr},{lonStr});
              relation[""tourism""~""museum|attraction|monument|gallery|castle|cathedral|viewpoint""](around:10000,{latStr},{lonStr});
            );
            out tags center;
            ";

        var overpassEndpoints = new[]
        {
            $"{_options.OverpassBaseUrl}interpreter",
            "https://overpass.kumi.systems/api/interpreter"
        };

        foreach (var endpoint in overpassEndpoints)
        {
            try
            {
                _logger.LogInformation("Attempting to fetch POIs from {Endpoint} for {City} at ({Lat}, {Lon})", endpoint, cityName, latStr, lonStr);
                var overpassContent = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("data", overpassQuery)
                });
 
                var overpassResponse = await _httpClient.PostAsync(endpoint, overpassContent, ct);
                if (overpassResponse.IsSuccessStatusCode)
                {
                    var overpassDataStr = await overpassResponse.Content.ReadAsStringAsync(ct);
                    var attractions = ParseOverpassResponse(overpassDataStr, limit, cityName);
                    if (attractions.Count > 0)
                    {
                        _logger.LogInformation("Successfully fetched {Count} POIs from {Endpoint} for {City}", attractions.Count, endpoint, cityName);
                        await _cache.SetAsync(cacheKey, attractions, TimeSpan.FromHours(24), ct);
                        return Result.Success(attractions);
                    }
                    _logger.LogWarning("Overpass API returned 0 attractions for {City} from {Endpoint}.", cityName, endpoint);
                }
                else
                {
                    _logger.LogWarning("Overpass API returned {Status} from {Endpoint} for {City}.", overpassResponse.StatusCode, endpoint, cityName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while fetching from {Endpoint} for {City}.", endpoint, cityName);
            }
        }
 
        _logger.LogError("All Overpass endpoints failed for {City}.", cityName);
        return Result.Failure<List<OsmAttractionDto>>(Error.External("GetTopAttractions.External", "Failed to fetch pois from Overpass API"));
    }

    private List<OsmAttractionDto> ParseOverpassResponse(string json, int limit, string cityName)
    {
        var attractions = new List<OsmAttractionDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("elements", out var elements))
        {
            return attractions;
        }

        foreach (var element in elements.EnumerateArray())
        {
            if (!element.TryGetProperty("tags", out var tags))
            {
                continue;
            }

            var wikiTag = tags.TryGetProperty("wikipedia", out var wp) ? wp.GetString() ?? "" : "";
            var wikidataTag = tags.TryGetProperty("wikidata", out var wd) ? wd.GetString() ?? "" : "";

            if (string.IsNullOrEmpty(wikiTag) && string.IsNullOrEmpty(wikidataTag))
            {
                continue;
            }

            string name = "";
            if (tags.TryGetProperty("name:en", out var nameEn)) name = nameEn.GetString() ?? "";
            else if (tags.TryGetProperty("int_name", out var intName)) name = intName.GetString() ?? "";

            if (string.IsNullOrEmpty(name) && wikiTag.StartsWith("en:"))
            {
                name = wikiTag.Substring(3).Replace('_', ' ');
            }

            if (string.IsNullOrEmpty(name))
            {
                if (tags.TryGetProperty("name", out var localName)) name = localName.GetString() ?? "";
            }

            if (string.IsNullOrEmpty(name) || name.Length < 3 || seen.Contains(name))
            {
                continue;
            }

            seen.Add(name);

            int score = 0;
            if (!string.IsNullOrEmpty(wikidataTag)) score += 5;
            if (!string.IsNullOrEmpty(wikiTag)) score += 5;
            
            var tourism = tags.TryGetProperty("tourism", out var tr) ? tr.GetString() ?? "" : "";
            if (tourism == "museum" || tourism == "attraction" || tourism == "cathedral" || tourism == "castle") score += 3;
            
            var website = tags.TryGetProperty("website", out var ws) ? ws.GetString() : null;
            if (website != null) score += 1;
            
            if (tags.TryGetProperty("image", out _)) score += 1;

            double lat = 0;
            double lon = 0;

            if (element.TryGetProperty("lat", out var latProp) && element.TryGetProperty("lon", out var lonProp))
            {
                lat = latProp.GetDouble();
                lon = lonProp.GetDouble();
            }
            else if (element.TryGetProperty("center", out var center))
            {
                if (center.TryGetProperty("lat", out var clat) && center.TryGetProperty("lon", out var clon))
                {
                    lat = clat.GetDouble();
                    lon = clon.GetDouble();
                }
            }

            var category = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase((tourism != "" ? tourism : "attraction").Replace("_", " "));

            var googleMapsLink = $"https://www.google.com/maps/search/?api=1&query={lat.ToString(System.Globalization.CultureInfo.InvariantCulture)},{lon.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

            attractions.Add(new OsmAttractionDto
            {
                Name = name,
                City = cityName,
                Category = category,
                Score = score,
                GoogleMapsLink = googleMapsLink,
                Website = website,
                Wikipedia = wikiTag,
                Wikidata = wikidataTag
            });
        }

        return attractions.OrderByDescending(a => a.Score).Take(limit).ToList();
    }

    public async Task<Result<List<OsmAttractionDto>>> GetTopDiningAsync(string cityName, int limit = 40, CancellationToken ct = default)
    {
        var cacheKey = $"dining:{cityName.ToLowerInvariant().Trim()}:{limit}";

        var cached = await _cache.GetAsync<List<OsmAttractionDto>>(cacheKey, ct);
        if (cached != null)
        {
            _logger.LogInformation("Cache hit for dining '{City}' (limit={Limit}).", cityName, limit);
            return Result.Success(cached);
        }

        var coordsResult = await GetCoordinatesAsync(cityName, ct);
        if (coordsResult.IsFailure)
        {
            _logger.LogError("Failed to resolve coordinates for {City} for dining.", cityName);
            return Result.Failure<List<OsmAttractionDto>>(coordsResult.Error);
        }
 
        var coords = coordsResult.Value;
        var latStr = coords.Lat.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var lonStr = coords.Lon.ToString(System.Globalization.CultureInfo.InvariantCulture);
 
        var overpassQuery = $@"
            [out:json][timeout:30][maxsize:10485760];
            (
              node[""amenity""~""restaurant|cafe|fast_food|pub|bar""](around:10000,{latStr},{lonStr});
              way[""amenity""~""restaurant|cafe|fast_food|pub|bar""](around:10000,{latStr},{lonStr});
              relation[""amenity""~""restaurant|cafe|fast_food|pub|bar""](around:10000,{latStr},{lonStr});
            );
            out tags center;
            ";

        var overpassEndpoints = new[]
        {
            $"{_options.OverpassBaseUrl}interpreter",
            "https://overpass.kumi.systems/api/interpreter"
        };

        foreach (var endpoint in overpassEndpoints)
        {
            try
            {
                _logger.LogInformation("Attempting to fetch dining from {Endpoint} for {City} at ({Lat}, {Lon})", endpoint, cityName, latStr, lonStr);
                var overpassContent = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("data", overpassQuery)
                });
 
                var overpassResponse = await _httpClient.PostAsync(endpoint, overpassContent, ct);
                if (overpassResponse.IsSuccessStatusCode)
                {
                    var overpassDataStr = await overpassResponse.Content.ReadAsStringAsync(ct);
                    var dining = ParseOverpassDiningResponse(overpassDataStr, limit, cityName);
                    if (dining.Count > 0)
                    {
                        _logger.LogInformation("Successfully fetched {Count} dining from {Endpoint} for {City}", dining.Count, endpoint, cityName);
                        await _cache.SetAsync(cacheKey, dining, TimeSpan.FromHours(24), ct);
                        return Result.Success(dining);
                    }
                    _logger.LogWarning("Overpass API returned 0 dining for {City} from {Endpoint}.", cityName, endpoint);
                }
                else
                {
                    _logger.LogWarning("Overpass API returned {Status} from {Endpoint} for {City}.", overpassResponse.StatusCode, endpoint, cityName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while fetching dining from {Endpoint} for {City}.", endpoint, cityName);
            }
        }
 
        _logger.LogError("All Overpass endpoints failed for dining in {City}.", cityName);
        return Result.Failure<List<OsmAttractionDto>>(Error.External("GetTopDining.External", "Failed to fetch dining from Overpass API"));
    }

    private List<OsmAttractionDto> ParseOverpassDiningResponse(string json, int limit, string cityName)
    {
        var dining = new List<OsmAttractionDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("elements", out var elements))
        {
            return dining;
        }

        foreach (var element in elements.EnumerateArray())
        {
            if (!element.TryGetProperty("tags", out var tags))
            {
                continue;
            }

            var wikiTag = tags.TryGetProperty("wikipedia", out var wp) ? wp.GetString() ?? "" : "";
            var wikidataTag = tags.TryGetProperty("wikidata", out var wd) ? wd.GetString() ?? "" : "";

            string name = "";
            if (tags.TryGetProperty("name:en", out var nameEn)) name = nameEn.GetString() ?? "";
            else if (tags.TryGetProperty("int_name", out var intName)) name = intName.GetString() ?? "";

            if (string.IsNullOrEmpty(name) && wikiTag.StartsWith("en:"))
            {
                name = wikiTag.Substring(3).Replace('_', ' ');
            }

            if (string.IsNullOrEmpty(name))
            {
                if (tags.TryGetProperty("name", out var localName)) name = localName.GetString() ?? "";
            }

            if (string.IsNullOrEmpty(name) || name.Length < 3 || seen.Contains(name))
            {
                continue;
            }

            seen.Add(name);

            int score = 0;
            if (!string.IsNullOrEmpty(wikidataTag)) score += 5;
            if (!string.IsNullOrEmpty(wikiTag)) score += 5;
            
            var amenity = tags.TryGetProperty("amenity", out var am) ? am.GetString() ?? "" : "";
            if (amenity == "restaurant") score += 3;
            else if (amenity == "cafe") score += 2;
            
            var website = tags.TryGetProperty("website", out var ws) ? ws.GetString() : null;
            if (website != null) score += 1;
            
            if (tags.TryGetProperty("image", out _)) score += 1;

            double lat = 0;
            double lon = 0;

            if (element.TryGetProperty("lat", out var latProp) && element.TryGetProperty("lon", out var lonProp))
            {
                lat = latProp.GetDouble();
                lon = lonProp.GetDouble();
            }
            else if (element.TryGetProperty("center", out var center))
            {
                if (center.TryGetProperty("lat", out var clat) && center.TryGetProperty("lon", out var clon))
                {
                    lat = clat.GetDouble();
                    lon = clon.GetDouble();
                }
            }

            var category = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase((amenity != "" ? amenity : "restaurant").Replace("_", " "));

            var googleMapsLink = $"https://www.google.com/maps/search/?api=1&query={lat.ToString(System.Globalization.CultureInfo.InvariantCulture)},{lon.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

            dining.Add(new OsmAttractionDto
            {
                Name = name,
                City = cityName,
                Category = category,
                Score = score,
                GoogleMapsLink = googleMapsLink,
                Website = website,
                Wikipedia = wikiTag,
                Wikidata = wikidataTag
            });
        }

        return dining.OrderByDescending(a => a.Score).Take(limit).ToList();
    }
}
