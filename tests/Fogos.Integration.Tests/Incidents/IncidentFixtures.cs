namespace Fogos.Integration.Tests.Incidents;

/// <summary>
/// A realistic ArcGIS OcorrenciasSite FeatureServer page (attribute names ported verbatim from
/// ProcessOcorrenciasSite.php) plus ICNF faztable/occurrence fixtures. One page, exceededTransferLimit
/// false, so the client stops after the first fetch.
/// </summary>
internal static class IncidentFixtures
{
    // 2025-08-01T00:00:00Z as ms epoch — year ≥ 2022 so the history/social/notify guards fire.
    private const long OccurredMs = 1754006400000;

    private static string Feature(string numero, string concelho, string freguesia, string estado,
        int codNatureza, string natureza, int man, int terrain, int aerial, double lat, double lng) =>
        $$"""
        { "attributes": {
            "Numero": "{{numero}}", "Concelho": "{{concelho}}", "Freguesia": "{{freguesia}}",
            "Localidade": "Casal", "Endereco": "EN110", "EstadoOcorrencia": "{{estado}}",
            "CodNatureza": {{codNatureza}}, "Natureza": "{{codNatureza}} - {{natureza}}",
            "MeiosAereos": {{aerial}}, "MeiosTerrestres": {{terrain}}, "Operacionais": {{man}},
            "Latitude": {{lat.ToString(System.Globalization.CultureInfo.InvariantCulture)}},
            "Longitude": {{lng.ToString(System.Globalization.CultureInfo.InvariantCulture)}},
            "DataOcorrencia": {{OccurredMs}}, "EstadoAgrupado": "{{estado}}", "Regiao": "Centro" } }
        """;

    /// <summary>Full page: new fire, a status change, a resources change, an FMA, a dico-pad concelho, a dirty status.</summary>
    public static string FeaturePage() =>
        "{ \"exceededTransferLimit\": false, \"features\": [" +
        string.Join(",", new[]
        {
            Feature("NEW1", "Ourém", "Freixianda", "Em Curso", 3101, "Incêndio Florestal", 45, 12, 2, 39.66, -8.45),
            Feature("SEEDED_STATUS", "Ourém", "Freixianda", "Em Resolução", 3101, "Incêndio Florestal", 30, 8, 1, 39.60, -8.40),
            Feature("SEEDED_RES", "Ourém", "Freixianda", "Em Curso", 3101, "Incêndio Florestal", 50, 20, 3, 39.61, -8.41),
            Feature("FMA1", "Lisboa", "Benfica", "Em Curso", 3315, "Inundação", 4, 2, 0, 38.75, -9.20),
            Feature("PAD1", "Fafe", "Medelo", "Em Curso", 3101, "Incêndio Florestal", 6, 3, 0, 41.45, -8.17),
            Feature("DIRTY1", "Ourém", "Freixianda", "Despacho de 1º Alerta", 3101, "Incêndio Florestal", 1, 0, 0, 39.62, -8.42),
        }) +
        "] }";

    // ── ICNF ────────────────────────────────────────────────────────────────
    /// <summary>faztable: two header rows, then one occurrence (field 0 = id, field 12 = estado).</summary>
    public static string IcnfTable(string id) =>
        "resultado = [" +
        "['head','','','','','','','','','','','','',''],['head2','','','','','','','','','','','','','']," +
        $"['{id}','x','','','','','','','','','','','Em Curso','']];";

    public static string IcnfNewFireXml(string id) => $"""
        <RESULT><CODIGO>
            <DATAALERTA>01-08-2026</DATAALERTA><HORAALERTA>14:20</HORAALERTA>
            <DISTRITO>SANTAREM</DISTRITO><CONCELHO>OUREM</CONCELHO><FREGUESIA>FREIXIANDA</FREGUESIA>
            <LOCAL>Casal do Mato</LOCAL><INE>1408</INE><LAT>39.66</LAT><LON>-8.45</LON>
        </CODIGO></RESULT>
        """;

    /// <summary>Enrichment XML with burn area, cause, source, and a KML url — triggers all three first-seen signals.</summary>
    public static string IcnfEnrichmentXml() => """
        <RESULT><CODIGO>
            <LOCAL>Casal do Mato</LOCAL>
            <AREATOTAL>12.5</AREATOTAL><AREAPOV>10</AREAPOV><AREAAGRIC>1.5</AREAAGRIC><AREAMATO>1</AREAMATO>
            <FONTEALERTA>112</FONTEALERTA><CAUSA>Fogueira</CAUSA><TIPOCAUSA>Negligente</TIPOCAUSA><CAUSAFAMILIA>Uso do fogo</CAUSAFAMILIA>
            <AREASFICHEIROS_GTF>https://fogos.icnf.pt/sgif2010/ficheiroskml/ICNF1.kml</AREASFICHEIROS_GTF>
        </CODIGO></RESULT>
        """;
}
