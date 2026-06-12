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
        var errors = new List<Error>();
        bool googleSuccess = false;
        bool osmSuccess = false;

        // 1. try Google Places first
        try
        {
            _logger.LogInformation("Attempting Google Places for '{City}'...", cityName);
            var googleResult = await _googlePlaces.GetTopAttractionsAsync(cityName, limit, ct);
            if (googleResult.IsSuccess)
            {
                googleSuccess = true;
                if (googleResult.Value.Count > 0)
                {
                    _logger.LogInformation("Google Places returned {Count} POIs for '{City}'.", googleResult.Value.Count, cityName);
                    return googleResult;
                }
                _logger.LogWarning("Google Places returned 0 results for '{City}', falling back to OSM.", cityName);
            }
            else
            {
                if (googleResult.Error.Type == ErrorType.Validation || googleResult.Error.Code == "TripPlan.InvalidCity")
                {
                    _logger.LogWarning("Google Places validation failed for '{City}': {@Error}. Aborting fallback to OSM.", cityName, googleResult.Error);
                    return googleResult;
                }
                errors.Add(googleResult.Error);
            }
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogWarning("Google Places timed out for '{City}', falling back to OSM.", cityName);
            errors.Add(Error.External("GooglePlaces.Timeout", "Google Places request timed out."));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Google Places failed for '{City}', falling back to OSM.", cityName);
            errors.Add(Error.External("GooglePlaces.Error", ex.Message));
        }

        // 2. fallback to OSM/Overpass
        try
        {
            _logger.LogInformation("Attempting OSM/Overpass for '{City}'...", cityName);
            var osmResult = await _osmService.GetTopAttractionsAsync(cityName, limit, ct);
            if (osmResult.IsSuccess)
            {
                osmSuccess = true;
                if (osmResult.Value.Count > 0)
                {
                    _logger.LogInformation("OSM returned {Count} POIs for '{City}'.", osmResult.Value.Count, cityName);
                    return osmResult;
                }
                _logger.LogWarning("OSM returned 0 results for '{City}'.", cityName);
            }
            else
            {
                errors.Add(osmResult.Error);
            }
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogWarning("OSM timed out for '{City}'.", cityName);
            errors.Add(Error.External("Osm.Timeout", "OSM request timed out."));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OSM failed for '{City}'.", cityName);
            errors.Add(Error.External("Osm.Error", ex.Message));
        }

        // If at least one provider succeeded with a response, we return success with an empty list.
        if (googleSuccess || osmSuccess)
        {
            _logger.LogInformation("At least one POI provider succeeded with 0 results for '{City}'.", cityName);
            return Result.Success(new List<OsmAttractionDto>());
        }

        // If both failed, return the primary error.
        var primaryError = errors.FirstOrDefault() ?? Error.External("Poi.ServiceError", "All POI providers failed to retrieve attractions.");
        _logger.LogError("All POI providers failed for '{City}': {@Error}", cityName, primaryError);
        return Result.Failure<List<OsmAttractionDto>>(primaryError);
    }

    public async Task<Result<List<OsmAttractionDto>>> GetTopDiningAsync(string cityName, int limit = 40, CancellationToken ct = default)
    {
        var errors = new List<Error>();
        bool googleSuccess = false;
        bool osmSuccess = false;

        // 1. try Google Places first
        try
        {
            _logger.LogInformation("Attempting Google Places (Dining) for '{City}'...", cityName);
            var googleResult = await _googlePlaces.GetTopDiningAsync(cityName, limit, ct);
            if (googleResult.IsSuccess)
            {
                googleSuccess = true;
                if (googleResult.Value.Count > 0)
                {
                    _logger.LogInformation("Google Places (Dining) returned {Count} dining places for '{City}'.", googleResult.Value.Count, cityName);
                    return googleResult;
                }
                _logger.LogWarning("Google Places (Dining) returned 0 results for '{City}', falling back to OSM.", cityName);
            }
            else
            {
                if (googleResult.Error.Type == ErrorType.Validation || googleResult.Error.Code == "TripPlan.InvalidCity")
                {
                    _logger.LogWarning("Google Places (Dining) validation failed for '{City}': {@Error}. Aborting fallback to OSM.", cityName, googleResult.Error);
                    return googleResult;
                }
                errors.Add(googleResult.Error);
            }
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogWarning("Google Places (Dining) timed out for '{City}', falling back to OSM.", cityName);
            errors.Add(Error.External("GooglePlaces.DiningTimeout", "Google Places dining request timed out."));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Google Places (Dining) failed for '{City}', falling back to OSM.", cityName);
            errors.Add(Error.External("GooglePlaces.DiningError", ex.Message));
        }

        // 2. fallback to OSM/Overpass
        try
        {
            _logger.LogInformation("Attempting OSM/Overpass (Dining) for '{City}'...", cityName);
            var osmResult = await _osmService.GetTopDiningAsync(cityName, limit, ct);
            if (osmResult.IsSuccess)
            {
                osmSuccess = true;
                if (osmResult.Value.Count > 0)
                {
                    _logger.LogInformation("OSM (Dining) returned {Count} dining places for '{City}'.", osmResult.Value.Count, cityName);
                    return osmResult;
                }
                _logger.LogWarning("OSM (Dining) returned 0 results for '{City}'.", cityName);
            }
            else
            {
                errors.Add(osmResult.Error);
            }
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogWarning("OSM (Dining) timed out for '{City}'.", cityName);
            errors.Add(Error.External("Osm.DiningTimeout", "OSM dining request timed out."));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OSM (Dining) failed for '{City}'.", cityName);
            errors.Add(Error.External("Osm.DiningError", ex.Message));
        }

        // If at least one provider succeeded with a response, we return success with an empty list.
        if (googleSuccess || osmSuccess)
        {
            _logger.LogInformation("At least one dining provider succeeded with 0 results for '{City}'.", cityName);
            return Result.Success(new List<OsmAttractionDto>());
        }

        // If both failed, return the primary error.
        var primaryError = errors.FirstOrDefault() ?? Error.External("Poi.DiningServiceError", "All dining providers failed to retrieve dining locations.");
        _logger.LogError("All dining providers failed for '{City}': {@Error}", cityName, primaryError);
        return Result.Failure<List<OsmAttractionDto>>(primaryError);
    }
}
