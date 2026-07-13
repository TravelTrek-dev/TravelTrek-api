using System.Text.Json.Serialization;

namespace TravelTrek.Application.DTOs.Ner;

public record NerRequest
{
    [JsonPropertyName("inputs")]
    public string Inputs { get; set; } = string.Empty;
}
