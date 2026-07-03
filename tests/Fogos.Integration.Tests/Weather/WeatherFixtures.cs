namespace Fogos.Integration.Tests.Weather;

/// <summary>
/// Hand-crafted IPMA fixture payloads modelled on the shapes the legacy parsers expect
/// (ANALYSIS.md §4). Coordinates are [lng,lat]; several metrics carry the -99 sentinel; the
/// homepage snippet embeds %uXXXX escapes and the exact <c>var result_warnings … //GET SEA DATA</c>
/// framing the scraper keys off.
/// </summary>
internal static class WeatherFixtures
{
    // stations.json — a bare GeoJSON feature array (Lisboa on the mainland, Funchal in Madeira).
    public const string Stations = """
    [
      {"type":"Feature","geometry":{"type":"Point","coordinates":[-9.1393,38.7223]},"properties":{"idEstacao":1200535,"localEstacao":"Lisboa (Geofísico)"}},
      {"type":"Feature","geometry":{"type":"Point","coordinates":[-16.9000,32.6500]},"properties":{"idEstacao":522,"localEstacao":"Funchal"}}
    ]
    """;

    // observations.json — timestamp → station → metrics|null; -99 appears throughout and 522 is null.
    public const string Observations = """
    {
      "2026-07-04T12:00:00": {
        "1200535": {"intensidadeVentoKM":12.5,"temperatura":31.0,"radiacao":-99.0,"idDireccVento":1,"precAcumulada":0.0,"intensidadeVento":3.5,"humidade":40.0,"pressao":1012.0},
        "522": null,
        "999": {"intensidadeVentoKM":-99.0,"temperatura":-99.0,"radiacao":-99.0,"idDireccVento":-99,"precAcumulada":-99.0,"intensidadeVento":-99.0,"humidade":-99.0,"pressao":-99.0}
      }
    }
    """;

    // daily observations — date → station → aggregates; 522's temp_max/-med/-prec are the -99 sentinel.
    public const string DailyObservations = """
    {
      "2026-07-03": {
        "1200535": {"temp_max":35.0,"temp_min":18.0,"temp_med":26.0,"prec_quant":0.0,"hum_max":80.0},
        "522": {"temp_max":-99.0,"temp_min":20.0,"temp_med":-99.0,"prec_quant":-99.0,"hum_max":75.0}
      }
    }
    """;

    // IPMA homepage snippet: STB (yellow) + AVR (orange) with %uXXXX escapes, plus a green LSB to drop.
    public const string Homepage = """
    <html><head></head><body>
    <script>
    var result_warnings = {"owner":"IPMA","country":"pt","data":[{"text":"Precau%u00E7%u00E3o com o calor","awarenessTypeName":"Tempo Quente","idAreaAviso":"STB","startTime":"2026-07-04T06:00:00","endTime":"2026-07-04T18:00:00","awarenessLevelID":"yellow"},{"text":"Sem impacto","awarenessTypeName":"Nevoeiro","idAreaAviso":"LSB","startTime":"2026-07-04T06:00:00","endTime":"2026-07-04T18:00:00","awarenessLevelID":"green"},{"text":"Ondula%u00E7%u00E3o forte","awarenessTypeName":"Agita%u00E7%u00E3o Mar%u00EDtima","idAreaAviso":"AVR","startTime":"2026-07-04T00:00:00","endTime":"2026-07-05T00:00:00","awarenessLevelID":"orange"}]};
                    //GET SEA DATA
    var sea_data = {};
    </script>
    </body></html>
    """;

    // Climate-normals page: the allstations JS literal (MTX*/MTN* monthly means for one station).
    public const string Normals = """
    <html><body><script>
    var allstations = [{"NUM_AUT":1200535,"NUM":535,"NOME":"Lisboa","MTXJAN":15.2,"MTXFEV":16.1,"MTXMAR":18.5,"MTXABR":19.8,"MTXMAI":22.4,"MTXJUN":26.1,"MTXJUL":28.9,"MTXAGO":29.2,"MTXSET":27.0,"MTXOUT":22.6,"MTXNOV":18.2,"MTXDEZ":15.6,"MTNJAN":8.3,"MTNFEV":9.1,"MTNMAR":10.6,"MTNABR":11.9,"MTNMAI":14.2,"MTNJUN":17.1,"MTNJUL":18.6,"MTNAGO":18.9,"MTNSET":17.8,"MTNOUT":14.9,"MTNNOV":11.4,"MTNDEZ":9.2}];
    </script></body></html>
    """;
}
