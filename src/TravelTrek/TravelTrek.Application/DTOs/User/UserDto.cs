namespace TravelTrek.Application.DTOs.User;

public record UserDto(
    Guid Id,
    string Email,
    string FullName,
    string? ProfilePictureUrl,
    string PreferredLanguage
);