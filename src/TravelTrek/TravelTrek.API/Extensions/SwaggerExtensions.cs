using Microsoft.OpenApi.Models;
using Microsoft.Extensions.Configuration;

namespace TravelTrek.API.Extensions
{
    public static class SwaggerExtensions
    {
        public static IServiceCollection AddSwaggerWithAuth(this IServiceCollection services)
        {
            services.AddEndpointsApiExplorer();

            services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "TravelTrek API",
                    Version = "v1",
                    Description = "AI-powered travel planning API"
                });

                options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Name = "Authorization",
                    Type = SecuritySchemeType.Http,
                    Scheme = "Bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header,
                    Description = "Enter your JWT token below.\r\nExample: eyJhbGci..."
                });

                options.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.SecurityScheme,
                                Id = "Bearer"
                            }
                        },
                        Array.Empty<string>()
                    }
                });
            });

            return services;
        }

        public static WebApplication UseSwaggerInDevelopment(this WebApplication app)
        {
            var enableSwaggerInProduction = app.Configuration.GetValue<bool>("EnableSwaggerInProduction");
            if (app.Environment.IsDevelopment() || enableSwaggerInProduction)
            {
                app.UseSwagger();
                app.UseSwaggerUI(options =>
                {
                    options.SwaggerEndpoint("/swagger/v1/swagger.json", "TravelTrek API v1");
                    options.RoutePrefix = "swagger";
                    options.DisplayRequestDuration();
                    options.EnableDeepLinking();
                });
            }

            return app;
        }
    }
}
