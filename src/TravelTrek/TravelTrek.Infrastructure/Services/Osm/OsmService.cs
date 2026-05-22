using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
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

    public OsmService(HttpClient httpClient, IOptions<OsmApiOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<Result<List<OsmAttractionDto>>> GetTopAttractionsAsync(string cityName, int limit = 40, CancellationToken ct = default)
    {
        try
        {
            var geoUrl = $"{_options.NominatimBaseUrl}search?q={Uri.EscapeDataString(cityName)}&format=json&limit=1";
            var geoResponse = await _httpClient.GetAsync(geoUrl, ct);

            if (!geoResponse.IsSuccessStatusCode)
            {
                return Result.Failure<List<OsmAttractionDto>>(Error.External("OsmService.GeocodeError", $"Geocoding status: {geoResponse.StatusCode}"));
            }

            var geoDataStr = await geoResponse.Content.ReadAsStringAsync(ct);
            using var geoDoc = JsonDocument.Parse(geoDataStr);
            if (geoDoc.RootElement.GetArrayLength() == 0)
            {
                return Result.Failure<List<OsmAttractionDto>>(Error.NotFound("OsmService.CityNotFound", $"City '{cityName}' not found"));
            }

            var firstResult = geoDoc.RootElement[0];
            var latStr = firstResult.GetProperty("lat").GetString();
            var lonStr = firstResult.GetProperty("lon").GetString();

            if (string.IsNullOrEmpty(latStr) || string.IsNullOrEmpty(lonStr))
            {
                return Result.Failure<List<OsmAttractionDto>>(Error.External("OsmService.ParseError", "Failed to parse coordinates"));
            }

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
            if (!overpassResponse.IsSuccessStatusCode)
            {
                return Result.Failure<List<OsmAttractionDto>>(Error.External("OsmService.OverpassError", $"Overpass API error: {overpassResponse.StatusCode}"));
            }

            var overpassDataStr = await overpassResponse.Content.ReadAsStringAsync(ct);
            var attractions = ParseOverpassResponse(overpassDataStr, limit, cityName);
            return Result.Success(attractions);
        }
        catch (Exception ex)
        {
            var msg = ex.Message;
            if (ex.InnerException != null) msg += " | Inner: " + ex.InnerException.Message;
            return Result.Failure<List<OsmAttractionDto>>(Error.External("OsmService.Exception", msg));
        }
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
