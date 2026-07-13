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

    public async Task<Result<List<PoiDto>>> GetTopAttractionsAsync(string cityName, int limit, CancellationToken ct = default)
    {
        var errors = new List<Error>();
        var googleSuccess = false;
        var osmSuccess = false;

        try
        {
            var googleResult = await _googlePlaces.GetTopAttractionsAsync(cityName, limit, ct);
            if (googleResult.IsSuccess)
            {
                googleSuccess = true;
                if (googleResult.Value.Count > 0)
                {
                    return googleResult;
                }
            }
            else
            {
                if (googleResult.Error.Type == ErrorType.Validation || googleResult.Error.Code == "TripPlan.InvalidCity")
                {
                    return googleResult;
                }
                errors.Add(googleResult.Error);
            }
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            errors.Add(Error.External("GooglePlaces.Timeout", "Google Places request timed out."));
        }
        catch (Exception ex)
        {
            errors.Add(Error.External("GooglePlaces.Error", ex.Message));
        }

        try
        {
            var osmResult = await _osmService.GetTopAttractionsAsync(cityName, limit, ct);
            if (osmResult.IsSuccess)
            {
                osmSuccess = true;
                if (osmResult.Value.Count > 0)
                {
                    return osmResult;
                }
            }
            else
            {
                errors.Add(osmResult.Error);
            }
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            errors.Add(Error.External("Osm.Timeout", "OSM request timed out."));
        }
        catch (Exception ex)
        {
            errors.Add(Error.External("Osm.Error", ex.Message));
        }

        if (googleSuccess || osmSuccess)
        {
            return Result.Success(new List<PoiDto>());
        }

        var primaryError = errors.FirstOrDefault() ?? Error.External("Poi.ServiceError", "All POI providers failed to retrieve attractions.");
        return Result.Failure<List<PoiDto>>(primaryError);
    }

    public async Task<Result<List<PoiDto>>> GetTopDiningAsync(string cityName, int limit = 40, CancellationToken ct = default)
    {
        var errors = new List<Error>();
        bool googleSuccess = false;
        bool osmSuccess = false;

        try
        {
            var googleResult = await _googlePlaces.GetTopDiningAsync(cityName, limit, ct);
            if (googleResult.IsSuccess)
            {
                googleSuccess = true;
                if (googleResult.Value.Count > 0)
                {
                    return googleResult;
                }
            }
            else
            {
                if (googleResult.Error.Type == ErrorType.Validation || googleResult.Error.Code == "TripPlan.InvalidCity")
                {
                    return googleResult;
                }
                errors.Add(googleResult.Error);
            }
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            errors.Add(Error.External("GooglePlaces.DiningTimeout", "Google Places dining request timed out."));
        }
        catch (Exception ex)
        {
            errors.Add(Error.External("GooglePlaces.DiningError", ex.Message));
        }

        try
        {
            var osmResult = await _osmService.GetTopDiningAsync(cityName, limit, ct);
            if (osmResult.IsSuccess)
            {
                osmSuccess = true;
                if (osmResult.Value.Count > 0)
                {
                    return osmResult;
                }
            }
            else
            {
                errors.Add(osmResult.Error);
            }
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            errors.Add(Error.External("Osm.DiningTimeout", "OSM dining request timed out."));
        }
        catch (Exception ex)
        {
            errors.Add(Error.External("Osm.DiningError", ex.Message));
        }

        if (googleSuccess || osmSuccess)
        {
            return Result.Success(new List<PoiDto>());
        }

        var primaryError = errors.FirstOrDefault() ?? Error.External("Poi.DiningServiceError", "All dining providers failed to retrieve dining locations.");
        return Result.Failure<List<PoiDto>>(primaryError);
    }
}
