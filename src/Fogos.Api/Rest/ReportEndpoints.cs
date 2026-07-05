using Fogos.Infrastructure.Reads;

namespace Fogos.Api.Rest;

/// <summary>REST v3 access to the latest situation report (JSON, cached 5 min).</summary>
public static class ReportEndpoints
{
    private const string CacheControl = "public, max-age=300";

    public static void MapReports(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v3/reports/latest", LatestAsync);
    }

    private static async Task<IResult> LatestAsync(HttpContext http, SituationReportReads reads, CancellationToken ct)
    {
        var report = await reads.LatestOneAsync(ct);
        if (report is null)
            return Results.NotFound(new { success = false, error = "no_report" });

        http.Response.Headers.CacheControl = CacheControl;
        return Results.Json(new
        {
            id = report.Id,
            at = report.At,
            slot = report.Slot,
            body = report.Body,
            activeFires = report.ActiveFires,
            totalMan = report.TotalMan,
            totalTerrain = report.TotalTerrain,
            totalAerial = report.TotalAerial,
            topIncidentIds = report.TopIncidentIds,
        });
    }
}
