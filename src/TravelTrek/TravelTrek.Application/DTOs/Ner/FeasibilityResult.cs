using System.Text.Json.Serialization;

namespace TravelTrek.Application.DTOs.Ner;

public record FeasibilityResult
{
    [JsonPropertyName("verdict")]
    public string Verdict { get; set; } = string.Empty;

    [JsonPropertyName("is_feasible")]
    public bool IsFeasible { get; set; }

    [JsonPropertyName("explanation")]
    public string Explanation { get; set; } = string.Empty;
}
