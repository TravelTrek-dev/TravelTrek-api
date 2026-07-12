using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TravelTrek.Application.Interfaces;
using TravelTrek.Domain.Common;
using TravelTrek.Infrastructure.Data.Configurations;

namespace TravelTrek.Infrastructure.Services.Llm;

public class GeminiService : ILLMService
{
    private readonly HttpClient _httpClient;
    private readonly GeminiApiOptions _options;
    private readonly ILogger<GeminiService> _logger;

    public GeminiService(HttpClient httpClient, IOptions<GeminiApiOptions> options, ILogger<GeminiService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Result<string>> GenerateAsync(string prompt, CancellationToken ct = default)
    {
        try
        {
            // Gemini native REST API: POST /v1beta/models/{model}:generateContent?key={apiKey}
            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = prompt }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.7,
                    responseMimeType = "application/json"
                }
            };

            var url = $"{_options.BaseUrl}{_options.Model}:generateContent?key={_options.ApiKey}";

            _logger.LogDebug("Sending prompt to Gemini model '{Model}' ({Length} chars).", _options.Model, prompt.Length);

            var response = await _httpClient.PostAsJsonAsync(url, requestBody, ct);

            if ((int)response.StatusCode >= 500)
            {
                return Result.Failure<string>(Error.External("Gemini.ServerError", $"Gemini server error: {(int)response.StatusCode}."));
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Gemini returned {StatusCode}: {Body}", (int)response.StatusCode, errorBody);
                return Result.Failure<string>(Error.External("Gemini.Error", $"Gemini responded with status {(int)response.StatusCode}."));
            }

            var responseStr = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(responseStr);

            // Gemini response: { "candidates": [{ "content": { "parts": [{ "text": "..." }] } }] }
            if (doc.RootElement.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
            {
                var firstCandidate = candidates[0];
                if (firstCandidate.TryGetProperty("content", out var content) &&
                    content.TryGetProperty("parts", out var parts) &&
                    parts.GetArrayLength() > 0)
                {
                    var firstPart = parts[0];
                    if (firstPart.TryGetProperty("text", out var textProp))
                    {
                        var text = textProp.GetString();
                        if (!string.IsNullOrWhiteSpace(text))
                            return Result.Success(text);
                    }
                }
            }

            _logger.LogWarning("Unexpected Gemini response shape. Raw (truncated): {Raw}", responseStr[..Math.Min(200, responseStr.Length)]);

            return Result.Failure<string>(Error.External("Gemini.UnexpectedResponse", "Gemini returned an unrecognized response format."));
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return Result.Failure<string>(Error.External("Gemini.Timeout", "The request to Gemini timed out."));
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to connect to Gemini service.");
            return Result.Failure<string>(Error.External("Gemini.Unavailable", "Gemini service is unavailable."));
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse Gemini response.");
            return Result.Failure<string>(Error.Internal("Gemini.ParseError", "Failed to parse Gemini response."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error communicating with Gemini.");
            return Result.Failure<string>(Error.External("Gemini.Exception", "Unexpected error communicating with Gemini."));
        }
    }
}
