using TravelTrek.Application.DTOs.User;

namespace TravelTrek.Application.DTOs.Auth;

public record AuthResponse(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt,
    UserDto User
);
