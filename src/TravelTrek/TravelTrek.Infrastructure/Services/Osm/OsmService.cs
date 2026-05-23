using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TravelTrek.Application.DTOs.Osm;
using TravelTrek.Application.Interfaces;
using TravelTrek.Domain.Common;
using TravelTrek.Infrastructure.Data.Configurations;

namespace TravelTrek.Infrastructure.Services.Osm;

public class OsmService : IOsmService
{
    private readonly HttpClient _httpClient;
    private readonly OsmApiOptions _options;
    private readonly OpenTripMapApiOptions _otmOptions;
    private readonly IOpenWeatherService _weatherService;
    private readonly ILogger<OsmService> _logger;

    private class CityCoordinates
    {
        public double Lat { get; set; }
        public double Lon { get; set; }
    }

    public OsmService(
        HttpClient httpClient, 
        IOptions<OsmApiOptions> options,
        IOptions<OpenTripMapApiOptions> otmOptions,
        IOpenWeatherService weatherService,
        ILogger<OsmService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _otmOptions = otmOptions.Value;
        _weatherService = weatherService;
        _logger = logger;
    }

    private async Task<Result<CityCoordinates>> GetCoordinatesAsync(string cityName, CancellationToken ct)
    {
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
                            return Result.Success(new CityCoordinates { Lat = lat, Lon = lon });
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
                return Result.Success(new CityCoordinates { Lat = geo.Lat, Lon = geo.Lon });
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
        try
        {
            _logger.LogInformation("Attempting to fetch POIs from Overpass API (OSM) for {City} at ({Lat}, {Lon})", cityName, latStr, lonStr);
            var overpassQuery = $@"
            [out:json][timeout:90][maxsize:1073741824];
            (
              node[""tourism""~""museum|attraction|monument|gallery|castle|cathedral|viewpoint""](around:20000,{latStr},{lonStr});
              way[""tourism""~""museum|attraction|monument|gallery|castle|cathedral|viewpoint""](around:20000,{latStr},{lonStr});
              relation[""tourism""~""museum|attraction|monument|gallery|castle|cathedral|viewpoint""](around:20000,{latStr},{lonStr});
            );
            out tags center;
            ";
            
            var overpassContent = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("data", overpassQuery)
            });

            var overpassResponse = await _httpClient.PostAsync($"{_options.OverpassBaseUrl}interpreter", overpassContent, ct);
            if (overpassResponse.IsSuccessStatusCode)
            {
                var overpassDataStr = await overpassResponse.Content.ReadAsStringAsync(ct);
                var attractions = ParseOverpassResponse(overpassDataStr, limit, cityName);
                if (attractions.Count > 0)
                {
                    _logger.LogInformation("Successfully fetched {Count} POIs from Overpass API for {City}", attractions.Count, cityName);
                    return Result.Success(attractions);
                }
                _logger.LogWarning("Overpass API returned 0 attractions for {City}.", cityName);
            }
            else
            {
                _logger.LogWarning("Overpass API returned non-success status code: {Status}", overpassResponse.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error while fetching from Overpass API for {City}.", cityName);
        }

        // fallback to otm
        try
        {
            _logger.LogInformation("Falling back to OpenTripMap API for {City} at ({Lat}, {Lon})", cityName, latStr, lonStr);
            var otmUrl = $"{_otmOptions.BaseUrl}radius?radius=20000&lon={lonStr}&lat={latStr}&kinds=interesting_places&apikey={_otmOptions.ApiKey}&limit={limit * 2}";
            
            var otmResponse = await _httpClient.GetAsync(otmUrl, ct);
            if (otmResponse.IsSuccessStatusCode)
            {
                var otmDataStr = await otmResponse.Content.ReadAsStringAsync(ct);
                var attractions = ParseOpenTripMapResponse(otmDataStr, limit, cityName);
                if (attractions.Count > 0)
                {
                    _logger.LogInformation("Successfully fetched {Count} POIs from OpenTripMap API for {City}", attractions.Count, cityName);
                    return Result.Success(attractions);
                }
                _logger.LogWarning("OpenTripMap API returned 0 attractions for {City}.", cityName);
            }
            else
            {
                _logger.LogWarning("OpenTripMap API returned non-success status code: {Status}", otmResponse.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error while fetching from OpenTripMap API for {City}.", cityName);
        }

        _logger.LogError("All POI services failed to fetch attractions for {City}. Returning empty attractions list to proceed with LLM-based planning.", cityName);
        return Result.Success(new List<OsmAttractionDto>());
    }

    private List<OsmAttractionDto> ParseOpenTripMapResponse(string json, int limit, string cityName)
    {
        var attractions = new List<OsmAttractionDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("features", out var features))
        {
            return attractions;
        }

        foreach (var feature in features.EnumerateArray())
        {
            if (!feature.TryGetProperty("properties", out var properties) || 
                !feature.TryGetProperty("geometry", out var geometry))
            {
                continue;
            }

            // Get Name
            if (!properties.TryGetProperty("name", out var nameProp) || string.IsNullOrEmpty(nameProp.GetString()))
            {
                continue;
            }
            var name = nameProp.GetString()!;

            if (name.Length < 3 || seen.Contains(name))
            {
                continue;
            }

            seen.Add(name);

            // Get Coordinates from geometry.coordinates [lon, lat]
            double lat = 0;
            double lon = 0;
            if (geometry.TryGetProperty("coordinates", out var coordinatesProp) && coordinatesProp.GetArrayLength() >= 2)
            {
                lon = coordinatesProp[0].GetDouble();
                lat = coordinatesProp[1].GetDouble();
            }

            // Get Wikidata and other properties
            var wikidata = properties.TryGetProperty("wikidata", out var wd) ? wd.GetString() : null;

            // Get Rate
            int rate = properties.TryGetProperty("rate", out var rProp) ? rProp.GetInt32() : 1;

            // Determine Score based on rate (OpenTripMap rate is 1-3, map to 1-10 scale)
            int score = rate * 3; 
            if (!string.IsNullOrEmpty(wikidata)) score += 2;

            // Determine Category from kinds string
            var kinds = properties.TryGetProperty("kinds", out var kProp) ? kProp.GetString() ?? "" : "";
            var category = "Attraction";

            if (kinds.Contains("museums", StringComparison.OrdinalIgnoreCase)) category = "Museum";
            else if (kinds.Contains("monuments", StringComparison.OrdinalIgnoreCase)) category = "Monument";
            else if (kinds.Contains("castles", StringComparison.OrdinalIgnoreCase)) category = "Castle";
            else if (kinds.Contains("churches", StringComparison.OrdinalIgnoreCase) || kinds.Contains("cathedrals", StringComparison.OrdinalIgnoreCase) || kinds.Contains("religion", StringComparison.OrdinalIgnoreCase)) category = "Cathedral";
            else if (kinds.Contains("viewpoints", StringComparison.OrdinalIgnoreCase)) category = "Viewpoint";
            else if (kinds.Contains("historic", StringComparison.OrdinalIgnoreCase)) category = "Historic Site";
            else if (kinds.Contains("architecture", StringComparison.OrdinalIgnoreCase)) category = "Architecture";

            var googleMapsLink = $"https://www.google.com/maps/search/?api=1&query={lat.ToString(System.Globalization.CultureInfo.InvariantCulture)},{lon.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

            attractions.Add(new OsmAttractionDto
            {
                Name = name,
                City = cityName,
                Category = category,
                Score = score,
                GoogleMapsLink = googleMapsLink,
                Website = null,
                Wikipedia = null,
                Wikidata = wikidata
            });
        }

        return attractions.OrderByDescending(a => a.Score).Take(limit).ToList();
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
}
