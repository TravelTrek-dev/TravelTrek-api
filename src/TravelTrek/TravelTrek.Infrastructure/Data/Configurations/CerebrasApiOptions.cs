using System.ComponentModel.DataAnnotations;

namespace TravelTrek.Infrastructure.Data.Configurations;

public class CerebrasApiOptions
{
    public const string SectionName = "Cerebras";

    [Required(ErrorMessage = "Cerebras BaseUrl is required.")]
    public string BaseUrl { get; set; } = "https://api.cerebras.ai/v1/chat/completions";

    [Required(ErrorMessage = "Cerebras ApiKey is required.")]
    public string ApiKey { get; set; } = default!;

    [Required(ErrorMessage = "Cerebras Model is required.")]
    public string Model { get; set; } = "gpt-oss-120b";
}
