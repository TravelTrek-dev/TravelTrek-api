using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TravelTrek.Application.DTOs.Auth;
using TravelTrek.Application.Interfaces;
using TravelTrek.Domain.Common;

namespace TravelTrek.API.Controllers
{
    [Route("api/auth")]
    public class AuthController : ApiBaseController
    {
        private readonly IAuthService _authService;

        public AuthController(IAuthService authService)
        {
            _authService = authService;
        }

        [EnableRateLimiting("auth-register")]
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            var result = await _authService.RegisterAsync(request);
            return ToActionResult(result);
        }

        [EnableRateLimiting("auth-login")]
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            var result = await _authService.LoginAsync(request);
            return ToActionResult(result);
        }

        [EnableRateLimiting("auth-google")]
        [HttpPost("google")]
        public async Task<IActionResult> Google([FromBody] SignupWithGoogleRequest request)
        {
            var result = await _authService.SignupWithGoogleAsync(request);
            return ToActionResult(result);
        }

        [EnableRateLimiting("auth-refresh")]
        [HttpPost("refresh-token")]
        public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
        {
            var result = await _authService.RefreshTokenAsync(request);
            return ToActionResult(result);
        }

        [Authorize]
        [EnableRateLimiting("auth-revoke")]
        [HttpPost("revoke-token")]
        public async Task<IActionResult> RevokeToken([FromBody] RevokeTokenRequest request)
        {
            var userId = GetUserId();

            if (userId == Guid.Empty)
            {
                return ToActionResult(Result.Failure(Error.Unauthorized("Auth.InvalidToken", "Invalid access token.")));
            }

            var result = await _authService.RevokeTokenAsync(request.RefreshToken, userId);
            return ToActionResult(result);
        }

        [Authorize]
        [EnableRateLimiting("revoke-all")]
        [HttpPost("revoke-all")]
        public async Task<IActionResult> RevokeAll()
        {
            var userId = GetUserId();

            if (userId == Guid.Empty)
            {
                return ToActionResult(Result.Failure(Error.Unauthorized("Auth.InvalidToken", "Invalid access token.")));
            }

            var result = await _authService.RevokeAllTokensAsync(userId);
            return ToActionResult(result);
        }

        [HttpGet("confirm-email")]
        public async Task<IActionResult> ConfirmEmail([FromQuery] Guid userId, [FromQuery] string token)
        {
            if (userId == Guid.Empty || string.IsNullOrWhiteSpace(token))
            {
                return ToActionResult(Result.Failure(Error.Validation("Auth.InvalidRequest", "User ID and token are required.")));
            }

            var result = await _authService.ConfirmEmailAsync(userId, token);
            return ToActionResult(result);
        }

        [EnableRateLimiting("auth-register")]
        [HttpPost("resend-confirmation")]
        public async Task<IActionResult> ResendConfirmation([FromBody] ResendConfirmationRequest request)
        {
            var result = await _authService.ResendConfirmationEmailAsync(request.Email);
            return ToActionResult(result);
        }

        [EnableRateLimiting("auth-register")]
        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
        {
            var result = await _authService.ForgotPasswordAsync(request);
            return ToActionResult(result);
        }

        [EnableRateLimiting("auth-register")]
        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
        {
            var result = await _authService.ResetPasswordAsync(request);
            return ToActionResult(result);
        }
    }
}