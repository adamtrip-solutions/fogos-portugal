using Fogos.Domain.Events;
using Fogos.Infrastructure.Queue;
using Fogos.Worker.Jobs.Icnf;

namespace Fogos.Worker.Handlers;

/// <summary>
/// Consumes <see cref="ProcessIcnfFireData"/> off the <c>icnf</c> stream and runs the enrichment core
/// (fetch XML → merge icnf sub-doc → download KML → first-seen detection → raise <see cref="IcnfEnriched"/>).
/// </summary>
public sealed class ProcessIcnfFireDataHandler(IcnfEnrichmentService enrichment) : IEventHandler<ProcessIcnfFireData>
{
    public Task HandleAsync(ProcessIcnfFireData evt, CancellationToken ct) =>
        enrichment.EnrichAsync(evt.IncidentId, evt.IcnfId, ct);
}
