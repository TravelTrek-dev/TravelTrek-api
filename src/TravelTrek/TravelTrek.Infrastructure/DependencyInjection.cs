using System.Net;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using TravelTrek.Application.Interfaces;
using TravelTrek.Domain.Entities;
using TravelTrek.Domain.Interfaces;
using TravelTrek.Infrastructure.Auth;
using TravelTrek.Infrastructure.Data;
using TravelTrek.Infrastructure.Data.Configurations;
using TravelTrek.Infrastructure.Repositories;
using TravelTrek.Infrastructure.Repositories.User;
using TravelTrek.Infrastructure.Repositories.Trip;
using TravelTrek.Infrastructure.Services;
using TravelTrek.Infrastructure.Services.Weather;
using TravelTrek.Infrastructure.Services.Ner;
using TravelTrek.Infrastructure.Services.Osm;
using TravelTrek.Infrastructure.Services.GooglePlaces;
using TravelTrek.Infrastructure.Services.Poi;
using TravelTrek.Infrastructure.Services.Llm;
using TravelTrek.Infrastructure.Services.Cache;
using TravelTrek.Application.Services;

namespace TravelTrek.Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
        {
            #region Database

            var connectionString = configuration.GetConnectionString("DefaultConnection")
                                   ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlServer(connectionString));

            #endregion

            #region Redis Cache

            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = configuration.GetConnectionString("Redis") ?? "localhost:6379";
                options.InstanceName = "TravelTrek:";
            });

            services.AddSingleton<ICacheService, RedisCacheService>();

            #endregion

            #region Identity User

            services.AddIdentity<User, IdentityRole<Guid>>(options =>
                {
                    options.Password.RequireDigit = true;
                    options.Password.RequireUppercase = true;
                    options.Password.RequireLowercase = true;
                    options.Password.RequiredLength = 8;
                    options.Password.RequireNonAlphanumeric = false;

                    options.User.RequireUniqueEmail = true;

                    options.Lockout.MaxFailedAccessAttempts = 5;
                    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                    options.Lockout.AllowedForNewUsers = true;
                })
                .AddEntityFrameworkStores<AppDbContext>()
                .AddDefaultTokenProviders();

            #endregion

            #region JWT Authentication

            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                })
                .AddJwtBearer(options =>
                {
                    var secret = configuration["JwtSettings:Secret"]!;
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        ValidIssuer = configuration["JwtSettings:Issuer"],
                        ValidAudience = configuration["JwtSettings:Audience"],
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                        ClockSkew = TimeSpan.Zero
                    };
                });

            #endregion

            #region Repositories & UOF

            services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));
            services.AddScoped<IUserRepository, UserRepository>();
            services.AddScoped<ITripPlanRepository, TripPlanRepository>();
            services.AddScoped<IExpenseRepository, ExpenseRepository>();
            services.AddScoped<IUnitOfWork, UnitOfWork>();

            #endregion

            services.Configure<JwtSettings>(configuration.GetSection("JwtSettings"));
            services.Configure<GoogleSettings>(configuration.GetSection("Google"));
            services.Configure<EmailSettings>(configuration.GetSection("EmailSettings"));
            services.Configure<OpenTripMapApiOptions>(configuration.GetSection(OpenTripMapApiOptions.SectionName));

            #region FluentEmail

            // FluentEmail configuration
            var emailSettings = configuration.GetSection("EmailSettings").Get<EmailSettings>()
                                ?? throw new InvalidOperationException("EmailSettings configuration is missing.");

            services
                .AddFluentEmail(emailSettings.SenderEmail, emailSettings.SenderName)
                .AddSmtpSender(new SmtpClient(emailSettings.SmtpServer)
                {
                    Port = emailSettings.SmtpPort,
                    Credentials = new NetworkCredential(emailSettings.Username, emailSettings.Password),
                    EnableSsl = emailSettings.EnableSsl
                });

            #endregion

            #region Services

            services.AddScoped<ITokenService, TokenService>();
            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<IEmailService, EmailService>();
            services.AddHttpClient<IIpGeolocationService, IpGeolocationService>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(5);
            });

            #endregion

            #region OpenWeather

            // OpenWeather API
            services.AddOptionsWithValidateOnStart<OpenWeatherApiOptions>()
                .Bind(configuration.GetSection(OpenWeatherApiOptions.SectionName))
                .ValidateDataAnnotations();

            services.AddHttpClient<IOpenWeatherService, OpenWeatherService>(client =>
            {
                client.BaseAddress = new Uri(configuration[$"{OpenWeatherApiOptions.SectionName}:BaseUrl"]!);
                client.Timeout = TimeSpan.FromSeconds(15);
            });

            #endregion

            #region NER API

            services.AddOptionsWithValidateOnStart<NerApiOptions>()
                .Bind(configuration.GetSection(NerApiOptions.SectionName))
                .ValidateDataAnnotations();

            services.AddHttpClient<INerService, NerService>(client =>
                {
                    client.BaseAddress = new Uri(configuration[$"{NerApiOptions.SectionName}:BaseUrl"]!);
                    client.Timeout = TimeSpan.FromSeconds(15);
                })
                .AddStandardResilienceHandler();

            #endregion

            #region POI Services (Google Places → OSM → LLM fallback)

            // Google Places (primary)
            services.AddOptionsWithValidateOnStart<GooglePlacesOptions>()
                .Bind(configuration.GetSection(GooglePlacesOptions.SectionName))
                .ValidateDataAnnotations();

            services.AddHttpClient<GooglePlacesService>(client =>
            {
                client.DefaultRequestHeaders.Add("User-Agent", "TravelApp/1.0 (educational project)");
                client.Timeout = TimeSpan.FromSeconds(15);
            });

            // OSM/Overpass (fallback)
            services.AddOptionsWithValidateOnStart<OsmApiOptions>()
                .Bind(configuration.GetSection(OsmApiOptions.SectionName))
                .ValidateDataAnnotations();

            services.AddHttpClient<OsmService>(client =>
            {
                client.DefaultRequestHeaders.Add("User-Agent", "TravelApp/1.0 (educational project)");
                client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
                client.Timeout = TimeSpan.FromSeconds(35);
            });

            // Fallback chain: Google → OSM → empty (LLM generates)
            services.AddScoped<IPoiService, FallbackPoiService>();

            #endregion

            #region Gemini LLM

            services.AddOptionsWithValidateOnStart<GeminiApiOptions>()
                .Bind(configuration.GetSection(GeminiApiOptions.SectionName))
                .ValidateDataAnnotations();

            services.AddHttpClient<ILLMService, GeminiService>(client =>
                {
                    client.BaseAddress = new Uri(configuration[$"{GeminiApiOptions.SectionName}:BaseUrl"]!);
                    client.Timeout = TimeSpan.FromSeconds(60);
                })
                .AddStandardResilienceHandler(options =>
                {
                    options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(60);
                    options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(60);
                    options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(2);
                });

            #endregion

            #region Trip Plan Services

            services.AddScoped<ITripGenerationService, TripGenerationService>();
            services.AddScoped<ITripPlanCrudService, TripPlanCrudService>();
            services.AddScoped<ITripSharingService, TripSharingService>();
            services.AddScoped<ITripExpenseService, TripExpenseService>();

            #endregion

            return services;
        }
    }
}