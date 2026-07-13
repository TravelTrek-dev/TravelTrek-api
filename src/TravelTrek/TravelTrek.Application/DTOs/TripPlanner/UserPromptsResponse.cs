namespace TravelTrek.Application.DTOs.TripPlanner;

public record UserPromptsResponse
{
    public Guid UserId { get; set; }
    public List<PromptItem> Prompts { get; set; } = new();
}

public record PromptItem
{
    public Guid TripId { get; set; }
    public string Prompt { get; set; } = string.Empty;
}
