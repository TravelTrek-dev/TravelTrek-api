using Microsoft.EntityFrameworkCore;
using Serilog;
using TravelTrek.API.Extensions;
using TravelTrek.API.Middleware;
using TravelTrek.Infrastructure;
using TravelTrek.Infrastructure.Data;

namespace TravelTrek.API
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

            Log.Information("TravelTrek API starting up...");

            try
            {
                var builder = WebApplication.CreateBuilder(args);

                builder.Host.AddSerilogLogging();

                builder.Services.AddApiControllers();
                builder.Services.AddSwaggerWithAuth();
                builder.Services.AddAutoMapper(cfg => {
                    cfg.AddProfile<TravelTrek.Application.Mappings.TripPlanProfile>();
                });
                builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
                builder.Services.AddProblemDetails();
                builder.Services.AddAuthRateLimiting();
                builder.Services.AddInfrastructureServices(builder.Configuration);

                var app = builder.Build();

                using (var scope = app.Services.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    Log.Information("Applying database migrations...");
                    await db.Database.MigrateAsync();
                    Log.Information("Database migrations applied successfully.");
                }

                app.UseExceptionHandler();
                app.UseSerilog();
                app.UseSwaggerInDevelopment();
                app.UseHttpsRedirection();
                app.UseRateLimiter();
                app.UseAuthentication();
                app.UseAuthorization();
                app.MapControllers();

                Log.Information("TravelTrek API started successfully.");
                app.Run();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "TravelTrek API failed to start.");
            }
            finally
            {
                await Log.CloseAndFlushAsync();
            }
        }
    }
}