using TravelTrek.Application.DTOs.Osm;
using TravelTrek.Domain.Common;

namespace TravelTrek.Application.Interfaces;

public interface IPoiService
{
    Task<Result<List<OsmAttractionDto>>> GetTopAttractionsAsync(string cityName, int limit = 40, CancellationToken ct = default);
}
