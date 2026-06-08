namespace TravelTrek.Application.DTOs.TripPlanner;

public class UserPromptsResponse
{
    public Guid UserId { get; set; }
    public List<PromptItem> Prompts { get; set; } = new();
}

public class PromptItem
{
    public Guid TripId { get; set; }
    public string Prompt { get; set; } = string.Empty;
}
