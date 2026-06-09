using System.ComponentModel.DataAnnotations;

namespace TravelTrek.Infrastructure.Data.Configurations;

public class GooglePlacesOptions
{
    public const string SectionName = "GooglePlaces";

    [Required(ErrorMessage = "Google Places API key is required.")]
    public string ApiKey { get; set; } = default!;
}
