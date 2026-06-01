using Microsoft.AspNetCore.Diagnostics;
using TravelTrek.Domain.Common;

namespace TravelTrek.API.Middleware
{
    /// <summary>
    /// Catches any unhandled exception in the pipeline and returns a clean JSON response.
    /// Prevents stack traces and internal details from leaking to the client in production.
    /// </summary>
    public class GlobalExceptionHandler : IExceptionHandler
    {
        private readonly ILogger<GlobalExceptionHandler> _logger;
        private readonly IHostEnvironment _env;

        public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IHostEnvironment env)
        {
            _logger = logger;
            _env = env;
        }

        public async ValueTask<bool> TryHandleAsync(
            HttpContext httpContext,
            Exception exception,
            CancellationToken cancellationToken)
        {
            _logger.LogError(exception, "Unhandled exception. TraceId: {TraceId}", httpContext.TraceIdentifier);

            var error = exception switch
            {
                ArgumentNullException => Error.Validation("Validation.ArgumentNull", "Required argument was null"),
                ArgumentException => Error.Validation("Validation.ArgumentInvalid", exception.Message),
                KeyNotFoundException => Error.NotFound("Resource.NotFound", exception.Message),
                UnauthorizedAccessException => Error.Unauthorized("Auth.Unauthorized", "Not authorized"),
                
                _ => Error.Internal("Internal.Error", _env.IsDevelopment() ? exception.Message : "An unexpected error occurred")
            };

            var statusCode = error.Type switch
            {
                ErrorType.Validation => StatusCodes.Status400BadRequest,
                ErrorType.NotFound => StatusCodes.Status404NotFound,
                ErrorType.Conflict => StatusCodes.Status409Conflict,
                ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
                ErrorType.Forbidden => StatusCodes.Status403Forbidden,
                ErrorType.External => StatusCodes.Status502BadGateway,
                _ => StatusCodes.Status500InternalServerError
            };

            var response = new
            {
                code = error.Code,
                message = error.Description,
                type = error.Type.ToString(),
                timestamp = DateTime.UtcNow,
                traceId = httpContext.TraceIdentifier,
                details = _env.IsDevelopment() ? exception.StackTrace : null
            };

            httpContext.Response.StatusCode = statusCode;
            httpContext.Response.ContentType = "application/json";

            await httpContext.Response.WriteAsJsonAsync(response, cancellationToken);

            return true; 
        }
    }
}