using System.ComponentModel.DataAnnotations;

namespace TravelTrek.Application.DTOs.TripPlanner;

public class TripPlanRequest
{
    [Required(ErrorMessage = "A trip description prompt is required.")]
    [MinLength(3, ErrorMessage = "Prompt must be at least 10 characters.")]
    [MaxLength(500, ErrorMessage = "Prompt must be at least 10 characters.")]
    
    public string Prompt { get; set; } = string.Empty;
}
