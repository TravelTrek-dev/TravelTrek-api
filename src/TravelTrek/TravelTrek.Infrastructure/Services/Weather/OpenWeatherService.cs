using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TravelTrek.Application.DTOs.Weather;
using TravelTrek.Application.Interfaces;
using TravelTrek.Domain.Common;
using TravelTrek.Infrastructure.Data.Configurations;

namespace TravelTrek.Infrastructure.Services.Weather;

public class OpenWeatherService : IOpenWeatherService
{
    private readonly HttpClient _httpClient;
    private readonly OpenWeatherApiOptions _options;
    private readonly ILogger<OpenWeatherService> _logger;

    public OpenWeatherService(HttpClient httpClient, IOptions<OpenWeatherApiOptions> options, ILogger<OpenWeatherService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Result<WeatherGeocodeResponse>> GeocodeByNameAsync(string cityName, CancellationToken ct = default)
    {
        try
        {
            var name = Uri.EscapeDataString(cityName);
            var apiKey = Uri.EscapeDataString(_options.ApiKey);
            
            var url = $"{_options.GeoBaseUrl}direct?q={name}&limit=1&appid={apiKey}";
            var response = await _httpClient.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
            {
                return Result.Failure<WeatherGeocodeResponse>(Error.External("OpenWeather.GeocodeError", $"Geocoding failed with status {(int)response.StatusCode}."));
            }

            var results = await response.Content.ReadFromJsonAsync<List<WeatherGeocodeResponse>>(ct);

            if (results is null || results.Count == 0)
            {
                return Result.Failure<WeatherGeocodeResponse>(Error.NotFound("OpenWeather.CityNotFound", $"Could not geocode city '{cityName}'."));
            }

            return Result.Success(results[0]);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to geocode city '{City}' via OpenWeather.", cityName);
            return Result.Failure<WeatherGeocodeResponse>(Error.External("OpenWeather.GeocodeUnavailable", "OpenWeather geocoding API is not available."));
        }
    }

    public async Task<Result<WeatherResponse>> GetCurrentWeatherAsync(WeatherRequest request, CancellationToken ct = default)
    {
        var lat = Uri.EscapeDataString(request.Latitude.ToString(CultureInfo.InvariantCulture));
        var lon = Uri.EscapeDataString(request.Longitude.ToString(CultureInfo.InvariantCulture));
        var units = Uri.EscapeDataString("metric");
        var apiKey = Uri.EscapeDataString(_options.ApiKey);

        var response = await _httpClient.GetAsync($"weather?lat={lat}&lon={lon}&appid={apiKey}&units={units}", ct);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return Result.Failure<WeatherResponse>(Error.Unauthorized("OpenWeather.Unauthorized", "Invalid or missing OpenWeather API key."));
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return Result.Failure<WeatherResponse>(Error.TooManyRequests(
                "OpenWeather.RateLimited",
                "Rate limit exceeded."));
        }

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            return Result.Failure<WeatherResponse>(Error.Validation(
                "OpenWeather.Validation",
                "Invalid request parameters sent to OpenWeather."));
        }

        if ((int)response.StatusCode >= 500)
        {
            return Result.Failure<WeatherResponse>(Error.External(
                "OpenWeather.ServerError",
                $"OpenWeather server error: {(int)response.StatusCode}."));
        }

        if (!response.IsSuccessStatusCode)
        {
            return Result.Failure<WeatherResponse>(Error.Internal(
                "OpenWeather.Error",
                $"Unexpected response: {(int)response.StatusCode}."));
        }

        try
        {
            var value = await response.Content.ReadFromJsonAsync<WeatherResponse>(ct);

            if (value is null)
            {
                return Result.Failure<WeatherResponse>(Error.Internal(
                    "OpenWeather.EmptyResponse",
                    "Empty response from OpenWeather."));
            }

            return Result.Success(value);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse OpenWeather response.");
            return Result.Failure<WeatherResponse>(Error.Internal(
                "OpenWeather.ParseError",
                "Failed to parse OpenWeather response."));
        }
    }

    public async Task<Result<ForecastResponse>> GetForecastAsync(WeatherRequest request, CancellationToken ct = default)
    {
        var lat = Uri.EscapeDataString(request.Latitude.ToString(CultureInfo.InvariantCulture));
        var lon = Uri.EscapeDataString(request.Longitude.ToString(CultureInfo.InvariantCulture));
        var units = Uri.EscapeDataString("metric");
        var apiKey = Uri.EscapeDataString(_options.ApiKey);

        var response = await _httpClient.GetAsync(
            $"forecast?lat={lat}&lon={lon}&appid={apiKey}&units={units}",
            ct);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return Result.Failure<ForecastResponse>(Error.Unauthorized(
                "OpenWeather.Unauthorized",
                "Invalid or missing OpenWeather API key."));
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return Result.Failure<ForecastResponse>(Error.TooManyRequests(
                "OpenWeather.RateLimited",
                "Rate limit exceeded."));
        }

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            return Result.Failure<ForecastResponse>(Error.Validation(
                "OpenWeather.Validation",
                "Invalid request parameters sent to OpenWeather."));
        }

        if ((int)response.StatusCode >= 500)
        {
            return Result.Failure<ForecastResponse>(Error.External(
                "OpenWeather.ServerError",
                $"OpenWeather server error: {(int)response.StatusCode}."));
        }

        if (!response.IsSuccessStatusCode)
        {
            return Result.Failure<ForecastResponse>(Error.Internal(
                "OpenWeather.Error",
                $"Unexpected response: {(int)response.StatusCode}."));
        }

        try
        {
            var value = await response.Content.ReadFromJsonAsync<ForecastResponse>(ct);

            if (value is null)
            {
                return Result.Failure<ForecastResponse>(Error.Internal(
                    "OpenWeather.EmptyResponse",
                    "Empty response from OpenWeather."));
            }

            return Result.Success(value);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse OpenWeather forecast response.");
            return Result.Failure<ForecastResponse>(Error.Internal(
                "OpenWeather.ParseError",
                "Failed to parse OpenWeather forecast response."));
        }
    }
    
}