using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TravelTrek.Application.DTOs.Ner;
using TravelTrek.Application.Interfaces;
using TravelTrek.Domain.Common;
using TravelTrek.Infrastructure.Data.Configurations;

namespace TravelTrek.Infrastructure.Services.Ner;

public class NerService : INerService
{
    private readonly HttpClient _httpClient;
    private readonly NerApiOptions _options;
    private readonly ILogger<NerService> _logger;

    public NerService(HttpClient httpClient, IOptions<NerApiOptions> options, ILogger<NerService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Result<List<NerEntity>>> ExtractEntitiesAsync(NerRequest request, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("extract", request, ct);

            if ((int)response.StatusCode >= 500)
            {
                return Result.Failure<List<NerEntity>>(Error.External("NerApi.ServerError", $"NER API server error: {(int)response.StatusCode}."));
            }

            if (!response.IsSuccessStatusCode)
            {
                return Result.Failure<List<NerEntity>>(Error.Internal("NerApi.Error", $"Unexpected response: {(int)response.StatusCode}."));
            }

            var value = await response.Content.ReadFromJsonAsync<IEnumerable<NerEntity>>(cancellationToken: ct);
            if (value is null)
            {
                return Result.Failure<List<NerEntity>>(Error.Internal("NerApi.EmptyResponse", "Empty response from NER API."));
            }

            return Result.Success(value.ToList());
        }
        catch(HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to connect to ner service");
            return Result.Failure<List<NerEntity>>(Error.External("NerApi.ServerError", "NER API is not available at the moment."));
            
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse NER API response.");
            return Result.Failure<List<NerEntity>>(Error.Internal("NerApi.ParseError", "Failed to parse NER API response."));
        }
    }

    public async Task<Result<ExtractedTripData>> ExtractAndParseTripDataAsync(NerRequest request, CancellationToken ct = default)
    {
        var rawEntitiesResult = await ExtractEntitiesAsync(request, ct);
        if (rawEntitiesResult.IsFailure)
        {
            return Result.Failure<ExtractedTripData>(rawEntitiesResult.Error);
        }

        var data = new ExtractedTripData();
        foreach (var entity in rawEntitiesResult.Value)
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

        return Result.Success(data);
    }

    public async Task<Result<FeasibilityResult>> CheckFeasibilityAsync(List<NerEntity> nerOutput, CancellationToken ct = default)
    {
        try
        {
            var payload = new { ner_output = nerOutput };
            var response = await _httpClient.PostAsJsonAsync("check-feasibility", payload, ct);

            if ((int)response.StatusCode >= 500)
            {
                return Result.Failure<FeasibilityResult>(Error.External("NerApi.ServerError", $"Feasibility API server error: {(int)response.StatusCode}."));
            }

            if (!response.IsSuccessStatusCode)
            {
                return Result.Failure<FeasibilityResult>(Error.Internal("NerApi.Error", $"Unexpected feasibility response: {(int)response.StatusCode}."));
            }

            var value = await response.Content.ReadFromJsonAsync<FeasibilityResult>(cancellationToken: ct);
            if (value is null)
            {
                return Result.Failure<FeasibilityResult>(Error.Internal("NerApi.EmptyResponse", "Empty response from Feasibility API."));
            }

            return Result.Success(value);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to connect to feasibility service");
            return Result.Failure<FeasibilityResult>(Error.External("NerApi.ServerError", "Feasibility service is not available at the moment."));
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse Feasibility API response.");
            return Result.Failure<FeasibilityResult>(Error.Internal("NerApi.ParseError", "Failed to parse Feasibility API response."));
        }
    }
}
