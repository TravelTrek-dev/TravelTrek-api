using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TravelTrek.Application.Interfaces;

namespace TravelTrek.Infrastructure.Services
{
    public class IpGeolocationService : IIpGeolocationService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<IpGeolocationService> _logger;

        public IpGeolocationService(HttpClient httpClient, ILogger<IpGeolocationService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<string?> GetCountryByIpAsync(string? ipAddress, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(ipAddress) || ipAddress == "::1" || ipAddress == "127.0.0.1")
            {
                // Local development testing fallback (mocks an Egyptian public IP)
                ipAddress = "197.34.0.0";
            }

            try
            {
                var response = await _httpClient.GetFromJsonAsync<IpApiResponse>(
                    $"http://ip-api.com/json/{ipAddress}?fields=status,country", 
                    cancellationToken);

                if (response != null && response.Status?.Equals("success", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return response.Country;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to geolocate IP address: {IpAddress}", ipAddress);
            }

            return null;
        }

        private class IpApiResponse
        {
            public string Status { get; set; } = string.Empty;
            public string Country { get; set; } = string.Empty;
        }
    }
}
