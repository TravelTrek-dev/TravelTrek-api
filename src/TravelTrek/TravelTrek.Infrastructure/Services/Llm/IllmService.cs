using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TravelTrek.Application.Interfaces;
using TravelTrek.Domain.Common;
using TravelTrek.Infrastructure.Data.Configurations;

namespace TravelTrek.Infrastructure.Services.Llm;

public class OpenAiService : ILLMService
{
    private readonly HttpClient _httpClient;
    private readonly OpenAIApiOptions _options;
    private readonly ILogger<OpenAiService> _logger;

    public OpenAiService(HttpClient httpClient, IOptions<OpenAIApiOptions> options, ILogger<OpenAiService> logger)
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

            _logger.LogDebug("Sending prompt to LLM model '{Model}' ({Length} chars).", _options.Model, prompt.Length);

            var response = await _httpClient.PostAsJsonAsync("", requestBody, ct);

            if ((int)response.StatusCode >= 500)
            {
                return Result.Failure<string>(Error.External("LLM.ServerError", $"LLM server error: {(int)response.StatusCode}."));
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("LLM returned {StatusCode}: {Body}", (int)response.StatusCode, errorBody);
                return Result.Failure<string>(Error.External("LLM.Error", $"LLM responded with status {(int)response.StatusCode}."));
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

            _logger.LogWarning("Unexpected LLM response shape. Raw (truncated): {Raw}", responseStr[..Math.Min(200, responseStr.Length)]);

            return Result.Failure<string>(Error.External("LLM.UnexpectedResponse", "LLM returned an unrecognized response format."));
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return Result.Failure<string>(Error.External("LLM.Timeout", "The request to the LLM timed out."));
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to connect to LLM service.");
            return Result.Failure<string>(Error.External("LLM.Unavailable", "LLM service is unavailable."));
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse LLM response.");
            return Result.Failure<string>(Error.Internal("LLM.ParseError", "Failed to parse LLM response."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error communicating with LLM.");
            return Result.Failure<string>(Error.External("LLM.Exception", "Unexpected error communicating with LLM."));
        }
    }
}