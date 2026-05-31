using Google.Apis.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using TravelTrek.Application.DTOs.Auth;
using TravelTrek.Application.DTOs.User;
using TravelTrek.Application.Interfaces;
using TravelTrek.Domain.Common;
using TravelTrek.Domain.Entities;
using TravelTrek.Domain.Interfaces;
using TravelTrek.Infrastructure.Auth;

namespace TravelTrek.Infrastructure.Services
{
    public class AuthService : IAuthService
    {
        private readonly UserManager<User> _userManager;
        private readonly ITokenService _tokenService;
        private readonly IGenericRepository<RefreshToken> _refreshTokenRepo;
        private readonly IUnitOfWork _unitOfWork;
        private readonly JwtSettings _jwtSettings;
        private readonly IUserRepository _userRepository;
        private readonly GoogleSettings _googleSettings;
        private readonly ILogger<AuthService> _logger;
        private readonly IEmailService _emailService;

        public AuthService(
            UserManager<User> userManager,
            ITokenService tokenService,
            IGenericRepository<RefreshToken> refreshTokenRepo,
            IUnitOfWork unitOfWork,
            IOptions<JwtSettings> jwtSettings,
            IUserRepository userRepository,
            IOptions<GoogleSettings> googleSettings,
            ILogger<AuthService> logger,
            IEmailService emailService)
        {
            _userManager = userManager;
            _tokenService = tokenService;
            _refreshTokenRepo = refreshTokenRepo;
            _unitOfWork = unitOfWork;
            _jwtSettings = jwtSettings.Value;
            _userRepository = userRepository;
            _googleSettings = googleSettings.Value;
            _logger = logger;
            _emailService = emailService;
        }

        public async Task<Result<AuthResponse>> LoginAsync(LoginRequest request)
        {
            var user = await _userManager.FindByEmailAsync(request.Email);

            if (user is null)
            {
                _logger.LogWarning("Login failed — email not found. Email: {Email}", request.Email);
                return Result.Failure<AuthResponse>(Error.Unauthorized("Auth.InvalidCredentials", "Invalid email or password."));
            }

            if (await _userManager.IsLockedOutAsync(user))
            {
                _logger.LogWarning("Login failed — account locked-out. Email: {Email}", request.Email);
                return Result.Failure<AuthResponse>(Error.Unauthorized("Auth.LockedOut", "Account locked. Try again later."));
            }

            if (!user.IsActive)
            {
                _logger.LogWarning("Login failed — account inactive. Email: {Email}, UserId: {UserId}", request.Email, user.Id);
                return Result.Failure<AuthResponse>(Error.Unauthorized("Auth.InvalidCredentials", "Invalid email or password."));
            }

            if (!await _userManager.CheckPasswordAsync(user, request.Password))
            {
                _logger.LogWarning("Login failed — wrong password. Email: {Email}, UserId: {UserId}", request.Email, user.Id);
                await _userManager.AccessFailedAsync(user);

                return Result.Failure<AuthResponse>(Error.Unauthorized("Auth.InvalidCredentials", "Invalid email or password."));
            }

            if (!user.EmailConfirmed)
            {
                _logger.LogWarning("Login failed — email not confirmed. Email: {Email}, UserId: {UserId}", request.Email, user.Id);
                return Result.Failure<AuthResponse>(Error.Forbidden("Auth.EmailNotConfirmed", "Please confirm your email address before logging in."));
            }

            _logger.LogInformation("Login successful. Email: {Email}, UserId: {UserId}", request.Email, user.Id);
            return await GenerateAuthResponseAsync(user);
        }

        public async Task<Result<AuthResponse>> RegisterAsync(RegisterRequest request)
        {
            var existingUser = await _userManager.FindByEmailAsync(request.Email);
            if (existingUser != null)
            {
                _logger.LogWarning("Registration failed — email already taken. Email: {Email}", request.Email);
                return Result.Failure<AuthResponse>(Error.Conflict("Auth.EmailTaken", "A user with this email already exists."));
            }

            var user = new User
            {
                Email = request.Email,
                UserName = request.Email,
                FullName = request.FullName,
                EmailConfirmed = false
            };

            var identityResult = await _userManager.CreateAsync(user, request.Password);
            if (!identityResult.Succeeded)
            {
                _logger.LogWarning("Registration failed — identity errors. Email: {Email}, Errors: {@Errors}", request.Email, identityResult.Errors.Select(e => e.Code));
                var errors = identityResult.Errors
                    .Select(e => Error.Validation(e.Code, e.Description))
                    .ToArray();
                return ValidationResult<AuthResponse>.WithErrors(errors);
            }

            var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            await _emailService.SendConfirmationEmailAsync(user.Email!, user.FullName, user.Id, token);

            _logger.LogInformation("User registered — confirmation email sent. Email: {Email}, UserId: {UserId}", request.Email, user.Id);
            return Result.Failure<AuthResponse>(Error.Validation("Auth.EmailConfirmationRequired", "Registration successful. Please check your email to confirm your account."));
        }

        public async Task<Result<AuthResponse>> SignupWithGoogleAsync(SignupWithGoogleRequest request)
        {
            try
            {
                var allowedAudiences = new List<string>();
                if (!string.IsNullOrWhiteSpace(_googleSettings.ClientId))
                {
                    allowedAudiences.Add(_googleSettings.ClientId);
                }
                if (_googleSettings.AllowedAudiences != null)
                {
                    allowedAudiences.AddRange(_googleSettings.AllowedAudiences);
                }

                var settings = new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = allowedAudiences,
                    IssuedAtClockTolerance = TimeSpan.FromMinutes(5),
                    ExpirationTimeClockTolerance = TimeSpan.FromMinutes(5)
                };

                var payload = await GoogleJsonWebSignature.ValidateAsync(request.IdToken, settings);

                var existingUser = await _userRepository.GetByGoogleIdAsync(payload.Subject)
                    ?? await _userManager.FindByEmailAsync(payload.Email);

                if (existingUser != null)
                {
                    if (!existingUser.IsActive)
                    {
                        _logger.LogWarning("Google sign-in blocked — account deactivated. Email: {Email}, UserId: {UserId}",
                            payload.Email, existingUser.Id);
                        return Result.Failure<AuthResponse>(Error.Forbidden("Auth.AccountDeactivated", "This account has been deactivated."));
                    }

                    if (existingUser.GoogleId == null)
                    {
                        _logger.LogInformation("Google sign-in — linked existing account. Email: {Email}, UserId: {UserId}",
                            payload.Email, existingUser.Id);
                        existingUser.GoogleId = payload.Subject;
                        existingUser.ProfilePictureUrl ??= payload.Picture;
                        await _userManager.UpdateAsync(existingUser);
                    }

                    return await GenerateAuthResponseAsync(existingUser);
                }

                var user = new User
                {
                    Email = payload.Email,
                    UserName = payload.Email,
                    FullName = payload.Name,
                    EmailConfirmed = true,
                    ProfilePictureUrl = payload.Picture,
                    GoogleId = payload.Subject
                };

                var identityResult = await _userManager.CreateAsync(user);
                if (!identityResult.Succeeded)
                {
                    _logger.LogWarning("Google sign-in — user creation failed. Email: {Email}, Errors: {@Errors}",
                        payload.Email, identityResult.Errors.Select(e => e.Code));
                    var errors = identityResult.Errors
                        .Select(e => Error.Validation(e.Code, e.Description))
                        .ToArray();
                    return ValidationResult<AuthResponse>.WithErrors(errors);
                }

                _logger.LogInformation("Google sign-in — new user created. Email: {Email}, UserId: {UserId}",
                    payload.Email, user.Id);
                return await GenerateAuthResponseAsync(user);
            }
            catch (InvalidJwtException ex)
            {
                _logger.LogWarning(ex, "Google sign-in failed — invalid or expired Google token. Reason: {Reason}", ex.Message);
                return Result.Failure<AuthResponse>(Error.Unauthorized("Auth.InvalidGoogleToken", $"Invalid or expired Google token. Details: {ex.Message}"));
            }
        }

        public async Task<Result<AuthResponse>> RefreshTokenAsync(RefreshTokenRequest request)
        {
            var principal = _tokenService.GetPrincipalFromExpiredToken(request.AccessToken);
            if (principal == null)
            {
                _logger.LogWarning("Token refresh failed — invalid access token.");
                return Result.Failure<AuthResponse>(Error.Unauthorized("Auth.InvalidToken", "Invalid access token."));
            }

            var userIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userIdClaim == null || !Guid.TryParse(userIdClaim, out var userId))
            {
                _logger.LogWarning("Token refresh failed — user ID claim missing or invalid.");
                return Result.Failure<AuthResponse>(Error.Unauthorized("Auth.InvalidToken", "Invalid access token."));
            }

            var user = await _userManager.FindByIdAsync(userId.ToString());
            if (user == null)
            {
                _logger.LogWarning("Token refresh failed — user not found. UserId: {UserId}", userId);
                return Result.Failure<AuthResponse>(Error.NotFound("Auth.UserNotFound", "User not found."));
            }

            var storedToken = await _refreshTokenRepo.FindFirstOrDefaultAsync(r => r.UserId == userId && r.Token == request.RefreshToken);
            
            if(storedToken is null)
            {
                _logger.LogWarning("Token refresh failed — token not found. UserId: {UserId}", userId);
                return Result.Failure<AuthResponse>(Error.Unauthorized("Auth.InvalidRefreshToken", "Invalid or expired refresh token."));
            }

            if (storedToken.ExpiresAt < DateTime.UtcNow)
            {
                _logger.LogWarning("Token refresh failed — refresh token expired. UserId: {UserId}", userId);
                return Result.Failure<AuthResponse>(Error.Unauthorized("Auth.InvalidRefreshToken", "Invalid or expired refresh token."));
            }

            if (storedToken.IsRevoked || storedToken.ReplacedByToken != null)
            {
                _logger.LogWarning("Refresh token reuse detected — revoking all tokens. UserId: {UserId}", userId);
                var allUserTokens = await _refreshTokenRepo.FindAsync(r => r.UserId == userId && r.RevokedAt == null);
                foreach(var token in allUserTokens)
                {
                    token.RevokedAt = DateTime.UtcNow;
                }

                await _unitOfWork.SaveChangesAsync();
                return Result.Failure<AuthResponse>(Error.Unauthorized("Auth.TokenReuse", "Security violation detected. All sessions have been revoked."));
            }

            storedToken.RevokedAt = DateTime.UtcNow;

            var newRefreshToken = _tokenService.GenerateRefreshToken(userId);
            storedToken.ReplacedByToken = newRefreshToken.Token;

            _refreshTokenRepo.Update(storedToken);
            await _refreshTokenRepo.AddAsync(newRefreshToken);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation("Token refreshed successfully. UserId: {UserId}", userId);
            return Result.Success(new AuthResponse(
                _tokenService.GenerateAccessToken(user),
                newRefreshToken.Token,
                DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpiryMinutes),
                new UserDto(
                    user.Id,
                    user.Email ?? string.Empty,
                    user.FullName,
                    user.ProfilePictureUrl,
                    user.PreferredLanguage
                )
            ));
        }

        public async Task<Result> RevokeTokenAsync(string refreshToken, Guid userId)
        {
            var storedTokens = await _refreshTokenRepo.FindAsync(r => r.UserId == userId && r.Token == refreshToken);

            var storedToken = storedTokens.FirstOrDefault();
            if (storedToken == null || !storedToken.IsActive)
            {
                _logger.LogWarning("Token revoke failed — token not found or already revoked. UserId: {UserId}", userId);
                return Result.Failure(Error.NotFound("Auth.TokenNotFound", "Refresh token not found or already revoked."));
            }

            storedToken.RevokedAt = DateTime.UtcNow;
            _refreshTokenRepo.Update(storedToken);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation("Token revoked successfully. UserId: {UserId}", userId);
            return Result.Success();
        }

        public async Task<Result> RevokeAllTokensAsync(Guid userId)
        {
            var storedTokens = await _refreshTokenRepo.FindAsync(r => r.UserId == userId && r.RevokedAt == null);
            if (!storedTokens.Any())
            {
                _logger.LogInformation("All Tokens are already revoked. UserId: {UserId}", userId);
                return Result.Success();
            }

            foreach (var token in storedTokens)
            {
                token.RevokedAt = DateTime.UtcNow;
            }

            _refreshTokenRepo.UpdateRange(storedTokens);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation("Tokens revoked successfully. UserId: {UserId}", userId);
            return Result.Success();
        }

        public async Task<Result> ConfirmEmailAsync(Guid userId, string token)
        {
            var user = await _userManager.FindByIdAsync(userId.ToString());
            if (user is null)
            {
                _logger.LogWarning("Email confirmation failed — user not found. UserId: {UserId}", userId);
                return Result.Failure(Error.NotFound("Auth.UserNotFound", "User not found."));
            }

            if (user.EmailConfirmed)
            {
                _logger.LogInformation("Email already confirmed. UserId: {UserId}", userId);
                return Result.Success();
            }

            var result = await _userManager.ConfirmEmailAsync(user, token);
            if (!result.Succeeded)
            {
                _logger.LogWarning("Email confirmation failed — invalid token. UserId: {UserId}, Errors: {@Errors}",
                    userId, result.Errors.Select(e => e.Code));
                return Result.Failure(Error.Validation("Auth.InvalidToken", "Invalid or expired confirmation token."));
            }

            _logger.LogInformation("Email confirmed successfully. UserId: {UserId}", userId);
            return Result.Success();
        }

        public async Task<Result> ResendConfirmationEmailAsync(string email)
        {
            var user = await _userManager.FindByEmailAsync(email);
            if (user is null)
            {
                _logger.LogWarning("Resend confirmation — email not found. Email: {Email}", email);
                return Result.Success();
            }

            if (user.EmailConfirmed)
            {
                _logger.LogInformation("Resend confirmation — email already confirmed. Email: {Email}", email);
                return Result.Success();
            }

            var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            await _emailService.SendConfirmationEmailAsync(user.Email!, user.FullName, user.Id, token);

            _logger.LogInformation("Confirmation email resent. Email: {Email}, UserId: {UserId}", email, user.Id);
            return Result.Success();
        }

        public async Task<Result> ForgotPasswordAsync(ForgotPasswordRequest request)
        {
            var user = await _userManager.FindByEmailAsync(request.Email);
            if (user is null || !user.EmailConfirmed)
            {
                _logger.LogWarning("Forgot password — email not found or not confirmed. Email: {Email}", request.Email);
                return Result.Success();
            }

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            await _emailService.SendPasswordResetEmailAsync(user.Email!, user.FullName, token);

            _logger.LogInformation("Password reset email sent. Email: {Email}, UserId: {UserId}", request.Email, user.Id);
            return Result.Success();
        }

        public async Task<Result> ResetPasswordAsync(ResetPasswordRequest request)
        {
            var user = await _userManager.FindByEmailAsync(request.Email);
            if (user is null)
            {
                _logger.LogWarning("Reset password failed — user not found. Email: {Email}", request.Email);
                return Result.Failure(Error.Validation("Auth.InvalidToken", "Invalid or expired reset token."));
            }

            var decodedToken = Uri.UnescapeDataString(request.Token);
            var result = await _userManager.ResetPasswordAsync(user, decodedToken, request.NewPassword);
            if (!result.Succeeded)
            {
                _logger.LogWarning("Reset password failed — identity errors. Email: {Email}, Errors: {@Errors}",
                    request.Email, result.Errors.Select(e => e.Code));
                var errors = result.Errors
                    .Select(e => Error.Validation(e.Code, e.Description))
                    .ToArray();
                return ValidationResult.WithErrors(errors);
            }

            _logger.LogInformation("Password reset successfully. Email: {Email}, UserId: {UserId}", request.Email, user.Id);
            return Result.Success();
        }

        #region Helpers
        private async Task<Result<AuthResponse>> GenerateAuthResponseAsync(User user)
        {
            var accessToken = _tokenService.GenerateAccessToken(user);
            var refreshToken = _tokenService.GenerateRefreshToken(user.Id);

            await _refreshTokenRepo.AddAsync(refreshToken);
            await _unitOfWork.SaveChangesAsync();

            return Result.Success(new AuthResponse(
                accessToken,
                refreshToken.Token,
                DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpiryMinutes),
                new UserDto(
                    user.Id,
                    user.Email ?? string.Empty,
                    user.FullName,
                    user.ProfilePictureUrl,
                    user.PreferredLanguage
                )
            ));
        } 
        #endregion
    }
}