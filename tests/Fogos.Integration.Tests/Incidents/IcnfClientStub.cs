using Fogos.Infrastructure.Sources;
using Microsoft.Extensions.Options;

namespace Fogos.Integration.Tests.Incidents;

/// <summary>
/// Serves ICNF fixtures (faztable HTML, per-ncco occurrence XML, KML perimeters) to a real
/// <see cref="IcnfClient"/> over a URL-routing stub handler — no network, TLS relaxation irrelevant.
/// </summary>
internal sealed class IcnfClientStub
{
    public string Table { get; set; } = "";
    public readonly Dictionary<string, string> XmlByNcco = new(StringComparer.Ordinal);
    public readonly Dictionary<string, byte[]> KmlById = new(StringComparer.Ordinal);

    /// <summary>Every ncco requested from webserviceocorrencias, in order — lets tests assert fetch budgets.</summary>
    public readonly List<string> OccurrenceRequests = [];

    public IcnfClient Client() =>
        new(new HttpClient(new Handler(this)), Options.Create(new FogosSourcesOptions()));

    private sealed class Handler(IcnfClientStub stub) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var url = uri.ToString();

            if (url.Contains("faztable", StringComparison.Ordinal))
                return Ok(stub.Table);

            if (url.Contains("webserviceocorrencias", StringComparison.Ordinal))
            {
                var ncco = QueryValue(uri.Query, "ncco");
                stub.OccurrenceRequests.Add(ncco);
                return stub.XmlByNcco.TryGetValue(ncco, out var xml) ? Ok(xml) : NotFound();
            }

            if (url.EndsWith(".kml", StringComparison.Ordinal))
            {
                var id = System.IO.Path.GetFileNameWithoutExtension(uri.AbsolutePath);
                return stub.KmlById.TryGetValue(id, out var bytes)
                    ? Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) })
                    : NotFound();
            }

            return NotFound();
        }

        private static string QueryValue(string query, string key)
        {
            foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                if (eq > 0 && pair[..eq] == key)
                    return Uri.UnescapeDataString(pair[(eq + 1)..]);
            }
            return "";
        }

        private static Task<HttpResponseMessage> Ok(string body) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(body) });

        private static Task<HttpResponseMessage> NotFound() =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
    }
}
