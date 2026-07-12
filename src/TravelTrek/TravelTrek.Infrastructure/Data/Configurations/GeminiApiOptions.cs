using System.ComponentModel.DataAnnotations;

namespace TravelTrek.Infrastructure.Data.Configurations;

public class GeminiApiOptions
{
    public const string SectionName = "Gemini";

    [Required(ErrorMessage = "Gemini BaseUrl is required.")]
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta/models/";

    [Required(ErrorMessage = "Gemini ApiKey is required.")]
    public string ApiKey { get; set; } = default!;

    [Required(ErrorMessage = "Gemini Model is required.")]
    public string Model { get; set; } = "gemini-2.5-flash";
}
