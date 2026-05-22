using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TravelTrek.Domain.Abstractions;
using TravelTrek.Domain.Common;

namespace TravelTrek.API.Controllers
{
    [ApiController]
    public abstract class ApiBaseController : ControllerBase
    {
        protected IActionResult ToActionResult<T>(Result<T> result)
        {
            if (result.IsSuccess)
                return Ok(result);

            // Handle multiple validation errors
            if (result is IValidationResult validationResult && validationResult.Errors.Length > 0)
            {
                return StatusCode(GetStatusCode(result.Error), new
                {
                    code = result.Error.Code,
                    message = result.Error.Description,
                    type = result.Error.Type.ToString(),
                    timestamp = DateTime.UtcNow,
                    errors = validationResult.Errors.Select(e => new
                    {
                        code = e.Code,
                        message = e.Description
                    })
                });
            }

            // Single error
            return StatusCode(GetStatusCode(result.Error), new
            {
                code = result.Error.Code,
                message = result.Error.Description,
                type = result.Error.Type.ToString(),
                timestamp = DateTime.UtcNow
            });
        }

        protected IActionResult ToCreatedResult<T>(Result<T> result)
        {
            if (result.IsSuccess)
                return StatusCode(StatusCodes.Status201Created, result);

            return ToActionResult(result);
        }

        protected IActionResult ToActionResult(Result result)
        {
            if (result.IsSuccess)
                return NoContent();

            return StatusCode(GetStatusCode(result.Error), new
            {
                code = result.Error.Code,
                message = result.Error.Description,
                type = result.Error.Type.ToString(),
                timestamp = DateTime.UtcNow
            });
        }

        private static int GetStatusCode(Error error) => error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.External => StatusCodes.Status502BadGateway,
            ErrorType.TooManyRequests => StatusCodes.Status429TooManyRequests,
            _ => StatusCodes.Status500InternalServerError
        };

        protected Guid GetUserId()
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdStr, out var userId))
            {
                return Guid.Empty;
            }

            return userId;
        }
    }
}