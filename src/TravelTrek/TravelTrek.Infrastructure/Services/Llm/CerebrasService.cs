using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TravelTrek.Application.Interfaces;
using TravelTrek.Domain.Common;
using TravelTrek.Infrastructure.Data.Configurations;

namespace TravelTrek.Infrastructure.Services.Llm;

public class CerebrasService : ILLMService
{
    private readonly HttpClient _httpClient;
    private readonly CerebrasApiOptions _options;
    private readonly ILogger<CerebrasService> _logger;

    public CerebrasService(HttpClient httpClient, IOptions<CerebrasApiOptions> options, ILogger<CerebrasService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Result<string>> GenerateAsync(string prompt, CancellationToken ct = default)
    {
        try
        {
            var requestBody = new
            {
                model = _options.Model,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                },
                temperature = 0.7
            };

            _logger.LogDebug("Sending prompt to Cerebras model '{Model}' ({Length} chars).", _options.Model, prompt.Length);

            var response = await _httpClient.PostAsJsonAsync("", requestBody, ct);

            if ((int)response.StatusCode >= 500)
            {
                return Result.Failure<string>(Error.External("Cerebras.ServerError", $"Cerebras server error: {(int)response.StatusCode}."));
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Cerebras returned {StatusCode}: {Body}", (int)response.StatusCode, errorBody);
                return Result.Failure<string>(Error.External("Cerebras.Error", $"Cerebras responded with status {(int)response.StatusCode}."));
            }

            var responseStr = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(responseStr);

            if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var firstChoice = choices[0];
                if (firstChoice.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var contentProp))
                {
                    var text = contentProp.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                        return Result.Success(text);
                }
            }

            _logger.LogWarning("Unexpected Cerebras response shape. Raw (truncated): {Raw}", responseStr[..Math.Min(200, responseStr.Length)]);

            return Result.Failure<string>(Error.External("Cerebras.UnexpectedResponse", "Cerebras returned an unrecognized response format."));
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return Result.Failure<string>(Error.External("Cerebras.Timeout", "The request to Cerebras timed out."));
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to connect to Cerebras service.");
            return Result.Failure<string>(Error.External("Cerebras.Unavailable", "Cerebras service is unavailable."));
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse Cerebras response.");
            return Result.Failure<string>(Error.Internal("Cerebras.ParseError", "Failed to parse Cerebras response."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error communicating with Cerebras.");
            return Result.Failure<string>(Error.External("Cerebras.Exception", "Unexpected error communicating with Cerebras."));
        }
    }
}
