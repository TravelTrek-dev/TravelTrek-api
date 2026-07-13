using System.Text.Json.Serialization;

namespace TravelTrek.Application.DTOs.Ner;

public record NerEntity
{
    [JsonPropertyName("entity_group")]
    public string EntityGroup { get; set; } = string.Empty;

    [JsonPropertyName("score")]
    public double Score { get; set; }

    [JsonPropertyName("word")]
    public string Word { get; set; } = string.Empty;

    [JsonPropertyName("start")]
    public int Start { get; set; }

    [JsonPropertyName("end")]
    public int End { get; set; }
}
