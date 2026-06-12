using System.Threading;
using System.Threading.Tasks;

namespace TravelTrek.Application.Interfaces
{
    public interface IIpGeolocationService
    {
        Task<string?> GetCountryByIpAsync(string? ipAddress, CancellationToken cancellationToken = default);
    }
}
