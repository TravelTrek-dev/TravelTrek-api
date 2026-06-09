using Microsoft.Extensions.Logging;
using TravelTrek.Application.DTOs.Osm;
using TravelTrek.Application.Interfaces;
using TravelTrek.Domain.Common;
using TravelTrek.Infrastructure.Services.GooglePlaces;
using TravelTrek.Infrastructure.Services.Osm;

namespace TravelTrek.Infrastructure.Services.Poi;
public class FallbackPoiService : IPoiService
{
    private readonly GooglePlacesService _googlePlaces;
    private readonly OsmService _osmService;
    private readonly ILogger<FallbackPoiService> _logger;

    public FallbackPoiService(GooglePlacesService googlePlaces, OsmService osmService, ILogger<FallbackPoiService> logger)
    {
        _googlePlaces = googlePlaces;
        _osmService = osmService;
        _logger = logger;
    }

    public async Task<Result<List<OsmAttractionDto>>> GetTopAttractionsAsync(string cityName, int limit = 40, CancellationToken ct = default)
    {
        // 1. try Google Places first
        try
        {
            _logger.LogInformation("Attempting Google Places for '{City}'...", cityName);
            var googleResult = await _googlePlaces.GetTopAttractionsAsync(cityName, limit, ct);
            if (googleResult.IsSuccess && googleResult.Value.Count > 0)
            {
                _logger.LogInformation("Google Places returned {Count} POIs for '{City}'.", googleResult.Value.Count, cityName);
                return googleResult;
            }
            _logger.LogWarning("Google Places returned 0 results for '{City}', falling back to OSM.", cityName);
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogWarning("Google Places timed out for '{City}', falling back to OSM.", cityName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Google Places failed for '{City}', falling back to OSM.", cityName);
        }

        // 2. fallback to OSM/Overpass
        try
        {
            _logger.LogInformation("Attempting OSM/Overpass for '{City}'...", cityName);
            var osmResult = await _osmService.GetTopAttractionsAsync(cityName, limit, ct);
            if (osmResult.IsSuccess && osmResult.Value.Count > 0)
            {
                _logger.LogInformation("OSM returned {Count} POIs for '{City}'.", osmResult.Value.Count, cityName);
                return osmResult;
            }
            _logger.LogWarning("OSM returned 0 results for '{City}', LLM will generate POIs.", cityName);
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogWarning("OSM timed out for '{City}', LLM will generate POIs.", cityName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OSM failed for '{City}', LLM will generate POIs.", cityName);
        }

        // 3. both failed — return empty. tell the llm to generate the plan
        _logger.LogWarning("All POI providers failed for '{City}'. LLM will generate attractions from its own knowledge.", cityName);
        return Result.Success(new List<OsmAttractionDto>());
    }
}
