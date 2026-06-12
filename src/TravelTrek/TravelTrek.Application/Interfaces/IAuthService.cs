using TravelTrek.Application.DTOs.Auth;
using TravelTrek.Domain.Common;

namespace TravelTrek.Application.Interfaces
{
    public interface IAuthService
    {
        Task<Result<AuthResponse>> RegisterAsync(RegisterRequest request, string? ipAddress);
        Task<Result<AuthResponse>> LoginAsync(LoginRequest request);
        Task<Result<AuthResponse>> SignupWithGoogleAsync(SignupWithGoogleRequest request, string? ipAddress);
        Task<Result<AuthResponse>> RefreshTokenAsync(RefreshTokenRequest request);
        Task<Result> RevokeTokenAsync(string refreshToken, Guid userId);
        Task<Result> RevokeAllTokensAsync(Guid userId);
        Task<Result> ConfirmEmailAsync(Guid userId, string token);
        Task<Result> ResendConfirmationEmailAsync(string email);
        Task<Result> ForgotPasswordAsync(ForgotPasswordRequest request);
        Task<Result> ResetPasswordAsync(ResetPasswordRequest request);
    }
}
