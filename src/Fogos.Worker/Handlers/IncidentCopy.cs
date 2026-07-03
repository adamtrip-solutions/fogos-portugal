using Fogos.Domain.Incidents;
using Fogos.Domain.Social;

namespace Fogos.Worker.Handlers;

/// <summary>
/// Social/push copy for incident posts, ported verbatim (emoji, spacing and all) from the legacy jobs
/// (HandleNewIncidentSocialMedia, CheckImportantFireIncident, SaveIncidentHistory/StatusHistory,
/// ProcessICNFFireData). Kept in one place so the exact strings are auditable against the live platform.
/// </summary>
public static class IncidentCopy
{
    public static string Hashtag(Incident i) => Hashtags.ForConcelho(i.Concelho);

    private static string FogoUrl(string domain, string id) => $"https://{domain}/fogo/{id}";
    private static string DetalheUrl(string domain, string id) => $"https://{domain}/fogo/{id}/detalhe";

    /// <summary>HandleNewIncidentSocialMedia new-fire post.</summary>
    public static string NewFire(Incident i, string domain) =>
        $"🔥⚠ Novo incêndio em {i.Location} - {i.Natureza}. Saiba mais em {FogoUrl(domain, i.Id)} {Hashtag(i)} FogosPT  ⚠🔥";

    /// <summary>CheckImportantFireIncident tweet (@ProteccaoCivil mention).</summary>
    public static string ImportantTweet(Incident i, string domain) =>
        $"ℹ🔥 Segundo os critérios da @ProteccaoCivil o incêndio em {i.Location} é considerado importante. {FogoUrl(domain, i.Id)} {Hashtag(i)} #FogosPT 🔥ℹ";

    /// <summary>CheckImportantFireIncident Facebook copy (ANEPC, no mention).</summary>
    public static string ImportantFacebook(Incident i, string domain) =>
        $"ℹ🔥 Segundo os critérios da ANEPC o incêndio em {i.Location} é considerado importante. {FogoUrl(domain, i.Id)} {Hashtag(i)} #FogosPT 🔥ℹ";

    /// <summary>CheckImportantFireIncident push body.</summary>
    public static string ImportantPush(Incident i) =>
        $"ℹ🔥 Segundo os critérios da ANEPC o incêndio em {i.Location} é considerado importante 🔥ℹ";

    /// <summary>SaveIncidentHistory big-incident push body.</summary>
    public static string BigPush(Incident i) =>
        $"ℹ🚨 {i.Location} - Grande mobilização de meios:  👩‍🚒 {i.Resources.Man} 🚒 {i.Resources.Terrain} 🚁 {i.Resources.Aerial} 🚨ℹ";

    /// <summary>SaveIncidentHistory big-incident post (CR/LF layout, as the legacy tweet).</summary>
    public static string BigPost(Incident i, string domain, string hhmm) =>
        $"ℹ🚨 {hhmm} - {i.Location} - Grande mobilização de meios:\r\n 👩‍🚒 {i.Resources.Man}\r\n 🚒 {i.Resources.Terrain}\r\n 🚁 {i.Resources.Aerial}\r\n {FogoUrl(domain, i.Id)} {Hashtag(i)} #FogosPT 🚨ℹ";

    /// <summary>SaveIncidentStatusHistory reacendimento post.</summary>
    public static string Reacendimento(Incident i, string domain) =>
        $"🚨🔥 Reacendimento em {i.Location} - {i.Natureza} {DetalheUrl(domain, i.Id)} {Hashtag(i)} #FogosPT  🔥🚨";

    /// <summary>SaveIncidentStatusHistory dominado post.</summary>
    public static string Dominado(Incident i, string domain) =>
        $"✅ Dominado {i.Location} - {i.Natureza} {DetalheUrl(domain, i.Id)} {Hashtag(i)} #FogosPT  ✅";

    /// <summary>SaveIncidentStatusHistory Facebook comment documenting a transition.</summary>
    public static string StatusComment(string hhmm, string previousLabel, string currentLabel) =>
        $"🔄 Alteração de estado às {hhmm}: {previousLabel} → {currentLabel}";

    /// <summary>NotificationTool status-change push body.</summary>
    public static string StatusPush(string previousLabel, string currentLabel) =>
        $"Alteração de estado: de {previousLabel} para {currentLabel}";

    /// <summary>ProcessICNFFireData first-KML ("area ardida disponível") post.</summary>
    public static string IcnfKml(Incident i, string domain) =>
        $"ℹ🗺 Area ardida disponível {DetalheUrl(domain, i.Id)} {Hashtag(i)} #FogosPT  🗺ℹ";

    /// <summary>ProcessICNFFireData burn-area total post.</summary>
    public static string IcnfBurnArea(Incident i, string domain, double totalHa) =>
        $"ℹ Total de área ardida: {totalHa} ha {DetalheUrl(domain, i.Id)} {Hashtag(i)} #FogosPT  ℹ";

    /// <summary>ProcessICNFFireData cause + source post (both present).</summary>
    public static string IcnfCauseAndSource(Incident i, string domain) =>
        $"ℹ Alerta via: {i.Icnf?.AlertSource} - Causa: {i.Icnf?.CauseFamily}, {i.Icnf?.CauseType}, {i.Icnf?.Cause} {FogoUrl(domain, i.Id)} {Hashtag(i)} #FogosPT  ℹ";

    /// <summary>ProcessICNFFireData cause-only post.</summary>
    public static string IcnfCause(Incident i, string domain) =>
        $"ℹ Causa: {i.Icnf?.CauseFamily}, {i.Icnf?.CauseType} {FogoUrl(domain, i.Id)} {Hashtag(i)} #FogosPT  ℹ";

    /// <summary>ProcessICNFFireData source-only post.</summary>
    public static string IcnfSource(Incident i, string domain) =>
        $"ℹ Alerta via:  {i.Icnf?.AlertSource} {FogoUrl(domain, i.Id)} {Hashtag(i)} #FogosPT  ℹ";
}
