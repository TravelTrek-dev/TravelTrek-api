using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using System.Threading.RateLimiting;

namespace TravelTrek.API.Extensions
{
    internal sealed class RateLimitingLog { }

    public static class RateLimitingExtensions
    {
        public static IServiceCollection AddAuthRateLimiting(this IServiceCollection services)
        {
            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                options.OnRejected = async (context, cancellationToken) =>
                {
                    var ip = context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                    var path = context.HttpContext.Request.Path;
                    var logger = context.HttpContext.RequestServices
                        .GetRequiredService<ILogger<RateLimitingLog>>();

                    logger.LogWarning("Rate limit exceeded. IP: {IP}, Endpoint: {Path}", ip, path);

                    context.HttpContext.Response.ContentType = "application/json";
                    await context.HttpContext.Response.WriteAsJsonAsync(new
                    {
                        code = "RateLimit.Exceeded",
                        type = "RateLimit",
                        message = "Too many requests. Please slow down and try again later.",
                        timestamp = DateTime.UtcNow
                    }, cancellationToken);
                };

                static RateLimitPartition<string> SlidingWindow(
                    string key, int permitLimit, int windowSeconds, int segments) =>
                    RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = permitLimit,
                        Window = TimeSpan.FromSeconds(windowSeconds),
                        SegmentsPerWindow = segments,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    });

                // 10 req / 1 min
                options.AddPolicy("auth-login", context =>
                    SlidingWindow(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", 10, 60, 2));

                // 5 req / 1 min
                options.AddPolicy("auth-register", context =>
                    SlidingWindow(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", 5, 60, 2));

                // 10 req / 1 min
                options.AddPolicy("auth-google", context =>
                    SlidingWindow(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", 10, 60, 2));

                // 20 req / 1 min
                options.AddPolicy("auth-refresh", context =>
                    SlidingWindow(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", 20, 60, 2));

                // 10 req / 1 min
                options.AddPolicy("auth-revoke", context =>
                    SlidingWindow(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", 10, 60, 2));

                options.AddPolicy("revoke-all", context =>
                    SlidingWindow(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", 10, 60, 2));

                // Trip Generation & Refinement (Expensive LLM ops) - 3 req / 1 min
                options.AddPolicy("trip-generate", context =>
                    SlidingWindow(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", 3, 60, 2));

                // Trip DB/Standard operations - 30 req / 1 min
                options.AddPolicy("trip-db", context =>
                    SlidingWindow(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", 30, 60, 2));
            });

            return services;
        }
    }
}
