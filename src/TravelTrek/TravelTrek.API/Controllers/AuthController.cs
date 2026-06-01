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

        /// <summary>
        /// Registers a new user account.
        /// </summary>
        [EnableRateLimiting("auth-register")]
        [HttpPost("register", Name = "Register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            var result = await _authService.RegisterAsync(request);
            return ToActionResult(result);
        }

        /// <summary>
        /// Authenticates a user and returns an access token.
        /// </summary>
        [EnableRateLimiting("auth-login")]
        [HttpPost("login", Name = "Login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            var result = await _authService.LoginAsync(request);
            return ToActionResult(result);
        }

        /// <summary>
        /// Authenticates or registers a user via Google OAuth.
        /// </summary>
        [EnableRateLimiting("auth-google")]
        [HttpPost("google", Name = "GoogleLogin")]
        public async Task<IActionResult> Google([FromBody] SignupWithGoogleRequest request)
        {
            var result = await _authService.SignupWithGoogleAsync(request);
            return ToActionResult(result);
        }

        /// <summary>
        /// Refreshes the user's access token using a refresh token.
        /// </summary>
        [EnableRateLimiting("auth-refresh")]
        [HttpPost("refresh-token", Name = "RefreshToken")]
        public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
        {
            var result = await _authService.RefreshTokenAsync(request);
            return ToActionResult(result);
        }

        /// <summary>
        /// Revokes a specific refresh token.
        /// </summary>
        [Authorize]
        [EnableRateLimiting("auth-revoke")]
        [HttpPost("revoke-token", Name = "RevokeToken")]
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

        /// <summary>
        /// Revokes all active refresh tokens for the user.
        /// </summary>
        [Authorize]
        [EnableRateLimiting("revoke-all")]
        [HttpPost("revoke-all", Name = "RevokeAllTokens")]
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

        /// <summary>
        /// Confirms a user's email address using a verification token.
        /// </summary>
        [HttpGet("confirm-email", Name = "ConfirmEmail")]
        public async Task<IActionResult> ConfirmEmail([FromQuery] Guid userId, [FromQuery] string token)
        {
            if (userId == Guid.Empty || string.IsNullOrWhiteSpace(token))
            {
                return ToActionResult(Result.Failure(Error.Validation("Auth.InvalidRequest", "User ID and token are required.")));
            }

            var result = await _authService.ConfirmEmailAsync(userId, token);
            return ToActionResult(result);
        }

        /// <summary>
        /// Resends the email confirmation link to a user.
        /// </summary>
        [EnableRateLimiting("auth-register")]
        [HttpPost("resend-confirmation", Name = "ResendConfirmation")]
        public async Task<IActionResult> ResendConfirmation([FromBody] ResendConfirmationRequest request)
        {
            var result = await _authService.ResendConfirmationEmailAsync(request.Email);
            return ToActionResult(result);
        }

        /// <summary>
        /// Sends a password reset link to the user's email.
        /// </summary>
        [EnableRateLimiting("auth-register")]
        [HttpPost("forgot-password", Name = "ForgotPassword")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
        {
            var result = await _authService.ForgotPasswordAsync(request);
            return ToActionResult(result);
        }

        /// <summary>
        /// Resets a user's password using a reset token.
        /// </summary>
        [EnableRateLimiting("auth-register")]
        [HttpPost("reset-password", Name = "ResetPassword")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
        {
            var result = await _authService.ResetPasswordAsync(request);
            return ToActionResult(result);
        }
    }
}