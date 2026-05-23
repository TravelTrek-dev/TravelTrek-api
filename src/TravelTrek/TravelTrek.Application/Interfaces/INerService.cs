using TravelTrek.Application.DTOs.Ner;
using TravelTrek.Domain.Common;

namespace TravelTrek.Application.Interfaces;

public interface INerService
{
    Task<Result<IEnumerable<NerEntity>>> ExtractEntitiesAsync(NerRequest request, CancellationToken ct = default);
    Task<Result<ExtractedTripData>> ExtractAndParseTripDataAsync(NerRequest request, CancellationToken ct = default);
    Task<Result<FeasibilityResult>> CheckFeasibilityAsync(IEnumerable<NerEntity> nerOutput, CancellationToken ct = default);
}
