namespace TravelTrek.Application.Interfaces;

public interface ICurrencyService
{
    Task<decimal?> GetExchangeRateAsync(string fromCurrency, string toCurrency, CancellationToken ct);
}