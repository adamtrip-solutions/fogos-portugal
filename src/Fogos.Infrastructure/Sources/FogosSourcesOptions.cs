namespace Fogos.Infrastructure.Sources;

/// <summary>
/// Configuration for every external source HTTP client. Defaults point at the real endpoints from
/// <c>ANALYSIS.md §4</c>; keys/budgets are empty/zero until provided. Bound from the "Sources" section.
/// </summary>
public sealed class FogosSourcesOptions
{
    public const string SectionName = "Sources";

    public ArcGisOptions ArcGis { get; set; } = new();
    public AnepcOptions Anepc { get; set; } = new();
    public IcnfOptions Icnf { get; set; } = new();
    public IpmaOptions Ipma { get; set; } = new();
    public FirmsOptions Firms { get; set; } = new();
    public Fr24Options Fr24 { get; set; } = new();
    public PlaneSourceOptions AdsbFi { get; set; } = new();
    public PlaneSourceOptions AirplanesLive { get; set; } = new();
    public GitHubOptions GitHub { get; set; } = new();
}

/// <summary>ANEPC ArcGIS FeatureServer (OcorrenciasSite).</summary>
public sealed class ArcGisOptions
{
    /// <summary>FeatureServer layer 0 base (the <c>/query</c> suffix is appended by the client).</summary>
    public string FeatureServerUrl { get; set; } =
        "https://services-eu1.arcgis.com/VlrHb7fn5ewYhX6y/arcgis/rest/services/OcorrenciasSite/FeatureServer/0";

    /// <summary>Records per page (legacy paged 1000/page via <c>resultOffset</c>).</summary>
    public int PageSize { get; set; } = 1000;
}

/// <summary>ANEPC direct API (Basic-auth JSON). The registered-but-unscheduled fallback ingester.</summary>
public sealed class AnepcOptions
{
    /// <summary>ANEPC_API_URL — empty by default (the source is selectable via config, not scheduled).</summary>
    public string ApiUrl { get; set; } = "";

    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

/// <summary>ICNF fire data (HTML table, per-occurrence XML, KML perimeters). TLS relaxed behind a flag.</summary>
public sealed class IcnfOptions
{
    public string TableUrl { get; set; } = "https://fogos.icnf.pt/localizador/faztable.asp";
    public string OccurrenceUrl { get; set; } = "https://fogos.icnf.pt/localizador/webserviceocorrencias.asp";
    public string KmlBaseUrl { get; set; } = "https://fogos.icnf.pt/sgif2010/ficheiroskml";

    /// <summary>Relax TLS validation for the ICNF hosts (their chain is broken). Scoped to this client only.</summary>
    public bool AllowInsecureTls { get; set; } = true;

    /// <summary>
    /// Only create incidents for ICNF occurrences newer than this. The faztable lists the whole
    /// season, so without a window a fresh database would ingest months of history as "new" fires.
    /// Older occurrences are remembered in Redis and never re-fetched.
    /// </summary>
    public int NewFireLookbackDays { get; set; } = 3;

    /// <summary>
    /// Per-run ceiling on per-occurrence XML fetches — a politeness/backstop cap so a backlog
    /// (fresh DB, long outage) drains across runs instead of hammering fogos.icnf.pt in one burst.
    /// </summary>
    public int MaxOccurrenceFetchesPerRun { get; set; } = 30;
}

/// <summary>IPMA open-data + scraped pages.</summary>
public sealed class IpmaOptions
{
    public string StationsUrl { get; set; } = "https://api.ipma.pt/open-data/observation/meteorology/stations/stations.json";
    public string ObservationsUrl { get; set; } = "https://api.ipma.pt/open-data/observation/meteorology/stations/observations.json";
    public string DailyObservationsUrl { get; set; } = "https://api.ipma.pt/open-data/observation/meteorology/stations/obs-daily.json";
    public string HomepageUrl { get; set; } = "https://www.ipma.pt/pt/index.html";
    public string RcmUrl { get; set; } = "https://www.ipma.pt/pt/riscoincendio/rcm.pt/index.jsp";
    public string WmsBaseUrl { get; set; } = "https://mf2.ipma.pt";
}

/// <summary>NASA FIRMS active-fire CSV area API.</summary>
public sealed class FirmsOptions
{
    public string BaseUrl { get; set; } = "https://firms.modaps.eosdis.nasa.gov/api/area/csv";
    public string Key { get; set; } = "";
}

/// <summary>Flightradar24 API + shared monthly credit budget.</summary>
public sealed class Fr24Options
{
    public string BaseUrl { get; set; } = "https://fr24api.flightradar24.com";
    public string ApiKey { get; set; } = "";

    /// <summary>Monthly credit budget (shared with the live platform); 0 disables the budget guard.</summary>
    public int MonthlyBudget { get; set; } = 0;

    /// <summary>Fraction of the budget at which the guard trips (legacy 95%).</summary>
    public double BudgetGuardFraction { get; set; } = 0.95;
}

/// <summary>adsb.fi / airplanes.live share the same shape (a single base URL, no key).</summary>
public sealed class PlaneSourceOptions
{
    public string BaseUrl { get; set; } = "";
}

/// <summary>GitHub contributors API.</summary>
public sealed class GitHubOptions
{
    public string ApiBaseUrl { get; set; } = "https://api.github.com";
    public string Repository { get; set; } = "fogospt/fogos";
    public string? Token { get; set; }
    public string UserAgent { get; set; } = "fogos-api";
}
