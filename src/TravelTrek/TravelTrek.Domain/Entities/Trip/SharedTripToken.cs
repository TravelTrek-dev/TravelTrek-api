namespace TravelTrek.Domain.Entities.Trip;

public class SharedTripToken
{
    public Guid Id { get; set; }
    public Guid TripPlanId { get; set; }
    public string Token { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsRevoked { get; set; }

    // Computed — not mapped to DB
    public bool IsActive => !IsRevoked && (ExpiresAt == null || ExpiresAt > DateTime.UtcNow);

    // Navigation
    public TripPlan TripPlan { get; set; } = null!;
}
