using System.Net.Http.Json;
using TravelTrek.Application.DTOs.Currency;
using TravelTrek.Application.Interfaces;

namespace TravelTrek.Infrastructure.Services.Currency;

public class CurrencyService : ICurrencyService
{
    private readonly ICacheService _cache;
    private readonly IHttpClientFactory _httpClientFactory;

    public CurrencyService(ICacheService cache, IHttpClientFactory httpClientFactory)
    {
        _cache = cache;
        _httpClientFactory = httpClientFactory;
    }
    
    public async Task<decimal?> GetExchangeRateAsync(string fromCurrency, string toCurrency, CancellationToken ct)
    {
        if (string.Equals(fromCurrency, toCurrency, StringComparison.OrdinalIgnoreCase))
            return 1.0m;

        try
        {
            var cacheKey = $"exrate:{fromCurrency.ToUpperInvariant()}:{toCurrency.ToUpperInvariant()}";
            var cachedRate = await _cache.GetAsync<decimal?>(cacheKey, ct);
            if (cachedRate.HasValue)
            {
                return cachedRate.Value;
            }

            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TravelTrek/1.0");
            client.DefaultRequestVersion = System.Net.HttpVersion.Version11;
            client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;

            var response = await client.GetFromJsonAsync<ExchangeRateResponse>(
                $"https://open.er-api.com/v6/latest/{fromCurrency.ToUpperInvariant()}",
                ct);

            if (response != null && response.Result?.Equals("success", StringComparison.OrdinalIgnoreCase) == true && response.Rates != null)
            {
                if (response.Rates.TryGetValue(toCurrency.ToUpperInvariant(), out var rateValue))
                {
                    var rate = (decimal)rateValue;
                    await _cache.SetAsync(cacheKey, (decimal?)rate, TimeSpan.FromHours(12), ct);
                    return rate;
                }
            }
        }
        catch (Exception ex)
        {
            throw;
        }

        return null;
    }

}