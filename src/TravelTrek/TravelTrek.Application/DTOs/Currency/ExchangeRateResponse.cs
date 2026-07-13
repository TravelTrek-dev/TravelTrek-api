using System.Text.Json.Serialization;

namespace TravelTrek.Application.DTOs.Currency;

public record ExchangeRateResponse
{
    [JsonPropertyName("result")]
    public string Result { get; set; } = string.Empty;

    [JsonPropertyName("base_code")]
    public string BaseCode { get; set; } = string.Empty;

    [JsonPropertyName("rates")]
    public Dictionary<string, double>? Rates { get; set; }
}