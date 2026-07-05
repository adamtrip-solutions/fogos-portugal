using Fogos.Infrastructure.Mongo;
using Fogos.Worker.Jobs.Risk;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fogos.Integration.Tests.Risk;

/// <summary>Builds an <see cref="RcmProcessor"/> wired to the given Mongo context for ingest tests.</summary>
internal static class RiskTestHost
{
    public static RcmProcessor BuildProcessor(MongoContext mongo)
        => new(mongo, new ConcelhoPolygons(), NullLogger<RcmProcessor>.Instance);
}
