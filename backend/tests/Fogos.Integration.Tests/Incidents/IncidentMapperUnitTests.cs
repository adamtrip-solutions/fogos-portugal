using System.Text.Json;
using Fogos.Domain.Incidents;
using Fogos.Infrastructure.Ingest;
using Fogos.Worker.Jobs.Icnf;

namespace Fogos.Integration.Tests.Incidents;

/// <summary>
/// Pure (container-free) tests for the ingest mapping edge: status aliases → codes, natureza → kind,
/// ms-epoch parsing, coordinate guard, Spain override, ArcGIS attribute mapping, and the ICNF parsers.
/// </summary>
public sealed class IncidentMapperUnitTests
{
    private static LocationInfo Loc() => new("Santarém", "Ourém", "Freixianda", "1408");

    [Theory]
    [InlineData("Despacho de 1.º Alerta", 4)]
    [InlineData("Despacho de 1º Alerta", 4)]
    [InlineData("Em Conclusão", 8)]
    [InlineData("Em Curso", 5)]
    [InlineData("Vigilância", 9)]
    public void Map_normalizes_status_aliases(string label, int expectedCode)
    {
        var raw = Raw() with { StatusLabel = label };
        var result = IncidentMapper.Map(raw, Loc());
        Assert.True(result.Ok);
        Assert.Equal(expectedCode, result.Incident!.Status.Code);
    }

    [Fact]
    public void Map_rejects_unknown_status()
    {
        var result = IncidentMapper.Map(Raw() with { StatusLabel = "Não é um estado" }, Loc());
        Assert.False(result.Ok);
        Assert.Contains("unknown status", result.Rejection);
    }

    [Theory]
    [InlineData("3101", IncidentKind.Fire)]
    [InlineData("3103", IncidentKind.Fire)]
    [InlineData("2101", IncidentKind.UrbanFire)]
    [InlineData("2301", IncidentKind.TransportFire)]
    [InlineData("3201", IncidentKind.OtherFire)]
    [InlineData("3315", IncidentKind.Fma)]
    [InlineData("9999", IncidentKind.Other)]
    public void Map_classifies_natureza(string code, IncidentKind expected)
    {
        var result = IncidentMapper.Map(Raw() with { NaturezaCode = code }, Loc());
        Assert.Equal(expected, result.Incident!.Kind);
    }

    [Fact]
    public void Map_sets_active_from_status_code()
    {
        Assert.True(IncidentMapper.Map(Raw() with { StatusLabel = "Em Curso" }, Loc()).Incident!.Active);   // 5
        Assert.False(IncidentMapper.Map(Raw() with { StatusLabel = "Conclusão" }, Loc()).Incident!.Active); // 8
    }

    [Fact]
    public void Map_guards_invalid_and_zero_coordinates()
    {
        Assert.Null(IncidentMapper.Map(Raw() with { Lat = 0, Lng = 0 }, Loc()).Incident!.Coordinates);
        Assert.Null(IncidentMapper.Map(Raw() with { Lat = null, Lng = null }, Loc()).Incident!.Coordinates);
        var ok = IncidentMapper.Map(Raw() with { Lat = 39.7, Lng = -8.5 }, Loc()).Incident!;
        Assert.Equal(39.7, ok.Coordinates!.Value.Latitude, 4);
    }

    [Fact]
    public void Map_builds_legacy_location_line_with_raw_concelho()
    {
        var raw = Raw() with { Concelho = "OURÉM" };
        var incident = IncidentMapper.Map(raw, Loc()).Incident!;
        Assert.Equal("Santarém, OURÉM, Freixianda", incident.Location);
        Assert.Equal("Ourém", incident.Concelho); // canonical field is title-cased
        Assert.Equal("1408", incident.Dico);
    }

    [Fact]
    public void ArcGis_maps_ms_epoch_and_attributes()
    {
        var json = """
        { "attributes": {
            "Numero": "2026080100123", "Concelho": "Ourém", "Freguesia": "Freixianda",
            "Localidade": "Casal", "Endereco": "EN110", "EstadoOcorrencia": "Em Curso",
            "CodNatureza": 3101, "Natureza": "3101 - Incêndio Florestal",
            "MeiosAereos": 2, "MeiosTerrestres": 12, "Operacionais": 45,
            "OperacionaisTerrestres": 40, "OPAereos": 5, "QuantEntidades": 3,
            "Latitude": 39.6, "Longitude": -8.4, "DataOcorrencia": 1754006400000,
            "EstadoAgrupado": "Em Curso", "FaseIncendio": "Curso", "Regiao": "Centro" } }
        """;
        var attrs = ReadAttributes(json);
        var raw = ArcGisOcorrenciasSource.Map(attrs)!;

        Assert.Equal("2026080100123", raw.Id);
        Assert.Equal("3101", raw.NaturezaCode);
        Assert.Equal("Incêndio Florestal", raw.Natureza);      // part after " - "
        Assert.Equal("Em Curso", raw.StatusLabel);
        Assert.Equal(45, raw.Resources.Man);
        Assert.Equal(12, raw.Resources.Terrain);
        Assert.Equal(2, raw.Resources.Aerial);
        Assert.Equal(40, raw.Resources.ManGround);
        Assert.Equal(5, raw.Resources.ManAerial);
        Assert.Equal(3, raw.Resources.Entities);
        Assert.Equal(14, raw.Resources.TotalAssets);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1754006400000), raw.OccurredAt);
        Assert.Equal("Casal EN110", raw.Localidade);
        Assert.Equal("Centro", raw.Region);
    }

    [Fact]
    public void ArcGis_defaults_means_fields_to_zero_when_absent()
    {
        var json = """
        { "attributes": {
            "Numero": "2026080100124", "EstadoOcorrencia": "Em Curso",
            "CodNatureza": 3101, "Natureza": "3101 - Incêndio Florestal",
            "MeiosAereos": 2, "MeiosTerrestres": 12, "Operacionais": 45,
            "Latitude": 39.6, "Longitude": -8.4, "DataOcorrencia": 1754006400000 } }
        """;
        var raw = ArcGisOcorrenciasSource.Map(ReadAttributes(json))!;

        Assert.Equal(0, raw.Resources.ManGround);
        Assert.Equal(0, raw.Resources.ManAerial);
        Assert.Equal(0, raw.Resources.Entities);
    }

    [Fact]
    public void Icnf_table_parser_skips_headers_and_maps_status()
    {
        // Two header rows then two occurrence rows; field 0 = id, field 12 = ICNF estado.
        var html = "var d = [" +
            "['h','','','','','','','','','','','','',''],['h2','','','','','','','','','','','','','']," +
            "['2026999001','x','','','','','','','','','','','Em Curso',''],\n" +
            "['2026999002','y','','','','','','','','','','','Extinto','']," +
            "['<b>notnumeric</b>','z','','','','','','','','','','','Dominado','']];";

        var rows = IcnfTableParser.Parse(html);
        Assert.Equal(2, rows.Count);
        Assert.Equal("2026999001", rows[0].Id);
        Assert.Equal("Em Curso", rows[0].StatusLabel);
        Assert.Equal("2026999002", rows[1].Id);
        Assert.Equal("Conclusão", rows[1].StatusLabel); // Extinto → Conclusão
    }

    [Fact]
    public void Icnf_occurrence_xml_parses_fields()
    {
        var xml = """
        <RESULT><CODIGO>
            <DATAALERTA>01-08-2026</DATAALERTA><HORAALERTA>14:20</HORAALERTA>
            <DISTRITO>SANTAREM</DISTRITO><CONCELHO>OUREM</CONCELHO><FREGUESIA>FREIXIANDA</FREGUESIA>
            <LOCAL>Casal</LOCAL><INE>1408</INE><LAT>39.6</LAT><LON>-8.4</LON>
            <AREATOTAL>12.5</AREATOTAL><AREAPOV>10</AREAPOV><AREAAGRIC>1.5</AREAAGRIC><AREAMATO>1</AREAMATO>
            <FONTEALERTA>112</FONTEALERTA><CAUSA>Fogueira</CAUSA><TIPOCAUSA>Negligente</TIPOCAUSA><CAUSAFAMILIA>Uso do fogo</CAUSAFAMILIA>
            <AREASFICHEIROS_GTF>https://icnf/x.kml</AREASFICHEIROS_GTF>
        </CODIGO></RESULT>
        """;
        var occ = IcnfOccurrenceXml.Parse(xml)!;
        Assert.Equal("1408", occ.Ine);
        Assert.Equal(12.5, occ.AreaTotal);
        Assert.Equal("112", occ.FonteAlerta);
        Assert.Equal("Fogueira", occ.Causa);
        Assert.Equal("https://icnf/x.kml", occ.KmlUrl);
    }

    private static RawIncident Raw() => new()
    {
        Id = "1",
        OccurredAt = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
        NaturezaCode = "3101",
        Natureza = "Incêndio Florestal",
        StatusLabel = "Em Curso",
        Concelho = "Ourém",
        Freguesia = "Freixianda",
        Lat = 39.6,
        Lng = -8.4,
        Resources = new Resources { Man = 10, Terrain = 5, Aerial = 1 },
    };

    private static IReadOnlyDictionary<string, JsonElement> ReadAttributes(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var attrs = doc.RootElement.GetProperty("attributes");
        var dict = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var p in attrs.EnumerateObject())
            dict[p.Name] = p.Value.Clone();
        return dict;
    }
}
