using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Xml.Linq;
using Pulse1x.App.Models;

namespace Pulse1x.App.Services;

/// <summary>Detalhes de um incidente ativo de um serviço, para a janela "Detalhes".</summary>
public record ServiceIncident(
    string Title,
    string Impact,
    IReadOnlyList<string> AffectedServices,
    IReadOnlyList<string> AffectedRegions,
    DateTimeOffset? StartedAt,
    DateTimeOffset? UpdatedAt,
    string LatestUpdate);

/// <summary>Status atual de um serviço de uma empresa (nome + estado + incidente ativo, se houver).</summary>
public record ServiceStatusInfo(string Name, ServiceState State, ServiceIncident? Incident);

/// <summary>Resultado agregado do status de uma empresa: estado geral, lista de serviços e o resumo
/// de uma linha do incidente ativo mais relevante (quando existe). <see cref="Live"/> indica se os
/// dados vieram de uma fonte em tempo real (Statuspage) ou se é o modo "somente link" (fallback).</summary>
public record CompanyStatusResult(
    ServiceState Overall,
    IReadOnlyList<ServiceStatusInfo> Services,
    string? IncidentSummary,
    bool Live);

/// <summary>
/// Consulta o status em tempo real das empresas do <see cref="StatusCatalog"/>. Tenta, em ordem de
/// precisão, as fontes preenchidas na definição de cada empresa: (1) API pública do Atlassian
/// Statuspage — status real por serviço, usada por várias plataformas monitoradas; (2) API oficial da
/// Apple (system status); (3) API oficial de status da Xbox Network; (4) API de incidentes do Google
/// Cloud Status Dashboard; (5) checagem de alcance HTTP com latência (🟢/🟡/🔴/⚫, sem granularidade
/// por serviço); (6) "⚪ status indisponível" — nenhuma fonte disponível. Qualquer falha de rede numa
/// etapa cai para a próxima da lista; nunca lança para a UI.
/// </summary>
public class ServerStatusService
{
    // HttpClient único por aplicação (boa prática). Timeout curto: o painel prioriza responder
    // rápido — se uma fonte demora, aquela empresa cai para a próxima fonte neste ciclo.
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    // Acima disso, uma resposta bem-sucedida ainda conta como degradada (🟡) em vez de operacional —
    // sinal honesto de lentidão quando não há API oficial com granularidade por serviço.
    private static readonly TimeSpan DegradedLatencyThreshold = TimeSpan.FromMilliseconds(2500);

    static ServerStatusService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("Pulse1x/1.0 (+https://github.com/Edu091)");
    }

    public async Task<CompanyStatusResult> FetchAsync(StatusCompanyDef def, CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(def.StatuspageBase))
        {
            try
            {
                var live = await FetchStatuspageAsync(def, ct);
                if (live is not null) return live;
            }
            catch { /* rede/parse falhou — cai para a próxima fonte */ }
        }

        if (def.AppleServiceNames is { Length: > 0 })
        {
            try
            {
                var live = await FetchAppleAsync(def, ct);
                if (live is not null) return live;
            }
            catch { /* rede/parse falhou — cai para a próxima fonte */ }
        }

        if (def.XboxCategoryNames is { Length: > 0 })
        {
            try
            {
                var live = await FetchXboxAsync(def, ct);
                if (live is not null) return live;
            }
            catch { /* rede/parse falhou — cai para a próxima fonte */ }
        }

        if (def.GoogleCloudProductNames is { Length: > 0 })
        {
            try
            {
                var live = await FetchGoogleCloudAsync(def, ct);
                if (live is not null) return live;
            }
            catch { /* rede/parse falhou — cai para a próxima fonte */ }
        }

        if (!string.IsNullOrEmpty(def.ReachabilityUrl))
        {
            try { return await FetchReachabilityAsync(def, ct); }
            catch { /* falha inesperada — cai para o ⚪ abaixo */ }
        }

        return Fallback(def);
    }

    // Modo "somente link": todos os serviços curados aparecem como "status indisponível".
    private static CompanyStatusResult Fallback(StatusCompanyDef def)
    {
        var services = def.FallbackServices
            .Select(n => new ServiceStatusInfo(n, ServiceState.Unknown, null))
            .ToList();
        return new CompanyStatusResult(ServiceState.Unknown, services, null, Live: false);
    }

    // ===================== Checagem de alcance (best-effort, com latência) =====================

    // Para empresas sem API pública de status, mede um sinal honesto: tempo de resposta de um GET curto
    // na URL representativa, com uma segunda tentativa antes de declarar indisponível (evita falso
    // negativo por uma única falha de rede passageira do próprio usuário). Classifica: sem resposta em
    // nenhuma tentativa → ⚫ indisponível; 5xx → 🔴 interrupção parcial; respondeu mas devagar (acima de
    // 2,5s) → 🟡 degradado; caso contrário → 🟢 operacional. O resultado vale para todos os serviços
    // curados da empresa — sem API oficial não há como saber qual serviço específico está afetado.
    private static async Task<CompanyStatusResult> FetchReachabilityAsync(StatusCompanyDef def, CancellationToken ct)
    {
        ServiceState state = ServiceState.Unavailable;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                using var req = new HttpRequestMessage(HttpMethod.Get, def.ReachabilityUrl);
                using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                sw.Stop();

                state = (int)resp.StatusCode >= 500
                    ? ServiceState.PartialOutage
                    : sw.Elapsed > DegradedLatencyThreshold
                        ? ServiceState.Degraded
                        : ServiceState.Operational;
                break;
            }
            catch
            {
                state = ServiceState.Unavailable; // tenta de novo (se for a 1ª tentativa) antes de desistir
            }
        }

        var services = def.FallbackServices
            .Select(n => new ServiceStatusInfo(n, state, null))
            .ToList();
        return new CompanyStatusResult(state, services, null, Live: true);
    }

    // ===================== Atlassian Statuspage (summary.json) =====================

    private static async Task<CompanyStatusResult?> FetchStatuspageAsync(StatusCompanyDef def, CancellationToken ct)
    {
        string url = def.StatuspageBase!.TrimEnd('/') + "/api/v2/summary.json";
        using var stream = await Http.GetStreamAsync(url, ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;

        // Incidentes ativos primeiro: usados tanto para o resumo da empresa quanto para anexar o
        // detalhe a cada serviço afetado (casando pelo nome do componente).
        var incidents = ParseIncidents(root);

        if (!root.TryGetProperty("components", out var comps) || comps.ValueKind != JsonValueKind.Array)
            return null;

        var services = def.StatuspageComponentNames is { Length: > 0 } names
            // Página com estrutura enganosa (ex.: Cloudflare agrupa por continente/data center) — busca
            // pelos nomes reais de serviço em qualquer lugar da árvore, na ordem curada do catálogo.
            ? SelectByName(comps, names, incidents)
            // Caminho padrão: componentes de nível superior (grupo OU folha sem grupo pai). Evita
            // despejar dezenas de subcomponentes (ex.: data centers) num único cartão.
            : SelectTopLevel(comps, incidents);

        if (services.Count == 0) return null; // resposta inesperada — deixa cair para o fallback

        ServiceState overall;
        string? summary;
        if (def.StatuspageComponentNames is { Length: > 0 })
        {
            // Modo curado: o indicador da página inteira reflete componentes fora da nossa lista
            // (ex.: os grupos por continente da Cloudflare), então o geral vem só dos serviços
            // exibidos — e o resumo só aparece se um dos incidentes afetar algum deles.
            overall = services.Aggregate(ServiceState.Unknown, (acc, x) => ServiceStatusVisuals.Worst(acc, x.State));
            summary = services.FirstOrDefault(s => s.Incident is not null)?.Incident?.Title;
        }
        else
        {
            // Indicador geral: prefere o nível declarado pela própria página; se ausente, deriva do pior serviço.
            overall = ServiceState.Unknown;
            if (root.TryGetProperty("status", out var st) && st.TryGetProperty("indicator", out var ind))
                overall = MapIndicator(ind.GetString());
            if (overall == ServiceState.Unknown)
                overall = services.Aggregate(ServiceState.Unknown, (acc, x) => ServiceStatusVisuals.Worst(acc, x.State));
            summary = incidents.Count > 0 ? incidents[0].Title : null;
        }

        return new CompanyStatusResult(overall, services, summary, Live: true);
    }

    // Caminho padrão: só entradas de topo (grupo OU componente-folha sem grupo pai).
    private static List<ServiceStatusInfo> SelectTopLevel(JsonElement comps, List<ServiceIncident> incidents)
    {
        var services = new List<ServiceStatusInfo>();
        foreach (var c in comps.EnumerateArray())
        {
            bool isGroup = c.TryGetProperty("group", out var g) && g.ValueKind == JsonValueKind.True;
            bool hasParent = c.TryGetProperty("group_id", out var gid) && gid.ValueKind == JsonValueKind.String;
            if (!isGroup && hasParent) continue;

            string name = c.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(name)) continue;

            var state = MapComponentStatus(c.TryGetProperty("status", out var s) ? s.GetString() : null);
            var incident = incidents.FirstOrDefault(i => i.AffectedServices.Contains(name, StringComparer.OrdinalIgnoreCase));
            services.Add(new ServiceStatusInfo(name, state, incident));

            if (services.Count >= 25) break; // teto de segurança para páginas muito grandes
        }
        return services;
    }

    // Busca por nome exato (case-insensitive) em qualquer lugar da árvore de componentes, na ordem
    // curada do catálogo — usado quando o nível superior da página não representa serviços de verdade.
    private static List<ServiceStatusInfo> SelectByName(JsonElement comps, string[] names, List<ServiceIncident> incidents)
    {
        var byName = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in comps.EnumerateArray())
        {
            string name = c.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            if (!string.IsNullOrWhiteSpace(name) && !byName.ContainsKey(name))
                byName[name] = c;
        }

        var services = new List<ServiceStatusInfo>();
        foreach (var wanted in names)
        {
            if (!byName.TryGetValue(wanted, out var c)) continue;
            var state = MapComponentStatus(c.TryGetProperty("status", out var s) ? s.GetString() : null);
            var incident = incidents.FirstOrDefault(i => i.AffectedServices.Contains(wanted, StringComparer.OrdinalIgnoreCase));
            services.Add(new ServiceStatusInfo(wanted, state, incident));
        }
        return services;
    }

    // ===================== Apple (system status oficial) =====================

    // Mesma fonte usada pela página apple.com/support/systemstatus: um JSON com todos os serviços da
    // Apple e, por serviço, uma lista de "events" (problemas relatados). Cada evento tem eventStatus
    // ("resolved" ou ativo) — só contam para o estado atual os que ainda não foram marcados resolvidos.
    private static async Task<CompanyStatusResult?> FetchAppleAsync(StatusCompanyDef def, CancellationToken ct)
    {
        const string url = "https://www.apple.com/support/systemstatus/data/system_status_en_US.js";
        using var stream = await Http.GetStreamAsync(url, ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;

        if (!root.TryGetProperty("services", out var svcArray) || svcArray.ValueKind != JsonValueKind.Array)
            return null;

        var byName = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in svcArray.EnumerateArray())
            if (s.TryGetProperty("serviceName", out var n) && n.GetString() is { } name)
                byName[name] = s;

        var services = new List<ServiceStatusInfo>();
        foreach (var wanted in def.AppleServiceNames!)
        {
            if (!byName.TryGetValue(wanted, out var s)) continue;

            ServiceState state = ServiceState.Operational;
            ServiceIncident? incident = null;
            if (s.TryGetProperty("events", out var events) && events.ValueKind == JsonValueKind.Array)
            {
                foreach (var ev in events.EnumerateArray())
                {
                    string evStatus = ev.TryGetProperty("eventStatus", out var es) ? es.GetString() ?? "" : "";
                    if (evStatus.Equals("resolved", StringComparison.OrdinalIgnoreCase)) continue; // já resolvido — não conta

                    string statusType = ev.TryGetProperty("statusType", out var st) ? st.GetString() ?? "" : "";
                    state = statusType.Equals("Outage", StringComparison.OrdinalIgnoreCase)
                        ? ServiceState.PartialOutage : ServiceState.Degraded;

                    string message = ev.TryGetProperty("message", out var m) ? m.GetString() ?? "" : wanted;
                    DateTimeOffset? started = TryEpoch(ev, "epochStartDate");
                    incident = new ServiceIncident(message, statusType, new[] { wanted }, Array.Empty<string>(), started, started, message);
                    break; // primeiro evento ativo já basta para classificar o serviço
                }
            }

            services.Add(new ServiceStatusInfo(wanted, state, incident));
        }

        if (services.Count == 0) return null;

        var overall = services.Aggregate(ServiceState.Unknown, (acc, x) => ServiceStatusVisuals.Worst(acc, x.State));
        string? summary = services.FirstOrDefault(s => s.Incident is not null)?.Incident?.Title;
        return new CompanyStatusResult(overall, services, summary, Live: true);
    }

    private static DateTimeOffset? TryEpoch(JsonElement obj, string prop) =>
        obj.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var ms)
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : null;

    // ===================== Xbox Network (API oficial da Microsoft) =====================

    // Mesma API XML usada pela página support.xbox.com/xbox-live-status. Cada categoria de nível
    // superior (Conta, Loja, Multiplayer...) tem um Status/Name — "None" é o único valor observado em
    // operação normal; qualquer outro valor indica algum grau de problema na categoria.
    private static async Task<CompanyStatusResult?> FetchXboxAsync(StatusCompanyDef def, CancellationToken ct)
    {
        const string url = "https://xnotify.xboxlive.com/servicestatusv6/US/en-US";
        using var stream = await Http.GetStreamAsync(url, ct);
        var doc = await XDocument.LoadAsync(stream, LoadOptions.None, ct);

        var categories = doc.Root?.Element("CoreServices")?.Elements("Category");
        if (categories is null) return null;

        var byName = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var cat in categories)
        {
            string name = cat.Element("Name")?.Value ?? "";
            if (!string.IsNullOrWhiteSpace(name) && !byName.ContainsKey(name))
                byName[name] = cat;
        }

        var services = new List<ServiceStatusInfo>();
        foreach (var wanted in def.XboxCategoryNames!)
        {
            if (!byName.TryGetValue(wanted, out var cat)) continue;

            string statusName = cat.Element("Status")?.Element("Name")?.Value ?? "None";
            ServiceState state = MapXboxStatus(statusName);

            ServiceIncident? incident = state == ServiceState.Operational ? null
                : new ServiceIncident(wanted + ": " + statusName, statusName, new[] { wanted }, Array.Empty<string>(), null, null, "");

            services.Add(new ServiceStatusInfo(wanted, state, incident));
        }

        if (services.Count == 0) return null;

        var overall = services.Aggregate(ServiceState.Unknown, (acc, x) => ServiceStatusVisuals.Worst(acc, x.State));
        string? summary = services.FirstOrDefault(s => s.Incident is not null)?.Incident?.Title;
        return new CompanyStatusResult(overall, services, summary, Live: true);
    }

    private static ServiceState MapXboxStatus(string statusName)
    {
        if (statusName.Equals("None", StringComparison.OrdinalIgnoreCase)) return ServiceState.Operational;
        if (statusName.Contains("Outage", StringComparison.OrdinalIgnoreCase)) return ServiceState.Unavailable;
        if (statusName.Contains("Impact", StringComparison.OrdinalIgnoreCase) || statusName.Contains("Limited", StringComparison.OrdinalIgnoreCase))
            return ServiceState.PartialOutage;
        return ServiceState.Degraded; // "Investigating", "Warning" etc. — algo fora do normal, mas não confirmado como queda
    }

    // ===================== Google Cloud Status Dashboard (incidents.json oficial) =====================

    // Mesma fonte usada pela página status.cloud.google.com: lista de incidentes (passados e em
    // andamento). Um incidente "em andamento" é aquele sem campo "end" preenchido. Cruza pelo título
    // exato em "affected_products" para saber se afeta um dos produtos curados no catálogo.
    private static async Task<CompanyStatusResult?> FetchGoogleCloudAsync(StatusCompanyDef def, CancellationToken ct)
    {
        const string url = "https://status.cloud.google.com/incidents.json";
        using var stream = await Http.GetStreamAsync(url, ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;

        // Só incidentes ainda em andamento (sem "end") e que representam disrupção real (não meramente informativos).
        var active = doc.RootElement.EnumerateArray().Where(i =>
            !(i.TryGetProperty("end", out var e) && e.ValueKind == JsonValueKind.String) &&
            i.TryGetProperty("status_impact", out var si) && si.GetString() == "SERVICE_DISRUPTION").ToList();

        var services = new List<ServiceStatusInfo>();
        foreach (var wanted in def.GoogleCloudProductNames!)
        {
            JsonElement? match = null;
            foreach (var inc in active)
            {
                if (!inc.TryGetProperty("affected_products", out var prods) || prods.ValueKind != JsonValueKind.Array) continue;
                bool hit = prods.EnumerateArray().Any(p =>
                    p.TryGetProperty("title", out var t) && string.Equals(t.GetString(), wanted, StringComparison.OrdinalIgnoreCase));
                if (hit) { match = inc; break; }
            }

            if (match is not { } inc2)
            {
                services.Add(new ServiceStatusInfo(wanted, ServiceState.Operational, null));
                continue;
            }

            string severity = inc2.TryGetProperty("severity", out var sv) ? sv.GetString() ?? "medium" : "medium";
            var state = severity is "high" or "critical" ? ServiceState.PartialOutage : ServiceState.Degraded;

            string title = inc2.TryGetProperty("external_desc", out var ed) ? ed.GetString() ?? wanted : wanted;
            var affectedProducts = inc2.TryGetProperty("affected_products", out var ap) && ap.ValueKind == JsonValueKind.Array
                ? ap.EnumerateArray().Select(p => p.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "").Where(s => s.Length > 0).ToList()
                : new List<string>();
            var regions = inc2.TryGetProperty("currently_affected_locations", out var loc) && loc.ValueKind == JsonValueKind.Array
                ? loc.EnumerateArray().Select(l => l.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "").Where(s => s.Length > 0).ToList()
                : new List<string>();
            DateTimeOffset? began = TryDate(inc2, "begin");
            DateTimeOffset? modified = TryDate(inc2, "modified");

            var incident = new ServiceIncident(title, severity, affectedProducts, regions, began, modified, "");
            services.Add(new ServiceStatusInfo(wanted, state, incident));
        }

        var overall = services.Aggregate(ServiceState.Unknown, (acc, x) => ServiceStatusVisuals.Worst(acc, x.State));
        string? summary = services.FirstOrDefault(s => s.Incident is not null)?.Incident?.Title;
        return new CompanyStatusResult(overall, services, summary, Live: true);
    }

    private static List<ServiceIncident> ParseIncidents(JsonElement root)
    {
        var list = new List<ServiceIncident>();
        if (!root.TryGetProperty("incidents", out var incs) || incs.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var i in incs.EnumerateArray())
        {
            string title = i.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "";
            string impact = i.TryGetProperty("impact", out var im) ? im.GetString() ?? "none" : "none";

            var affected = new List<string>();
            if (i.TryGetProperty("components", out var comps) && comps.ValueKind == JsonValueKind.Array)
                foreach (var c in comps.EnumerateArray())
                    if (c.TryGetProperty("name", out var cn) && cn.GetString() is { } cName)
                        affected.Add(cName);

            DateTimeOffset? started = TryDate(i, "started_at");
            DateTimeOffset? updated = TryDate(i, "updated_at");

            // Corpo da atualização mais recente (o primeiro item de incident_updates é o mais novo).
            string latest = "";
            if (i.TryGetProperty("incident_updates", out var ups) && ups.ValueKind == JsonValueKind.Array)
                foreach (var u in ups.EnumerateArray())
                {
                    if (u.TryGetProperty("body", out var b)) latest = b.GetString() ?? "";
                    break;
                }

            list.Add(new ServiceIncident(title, impact, affected, Array.Empty<string>(), started, updated, latest));
        }
        return list;
    }

    private static DateTimeOffset? TryDate(JsonElement obj, string prop) =>
        obj.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String &&
        DateTimeOffset.TryParse(v.GetString(), out var dt) ? dt : null;

    private static ServiceState MapComponentStatus(string? status) => status switch
    {
        "operational" => ServiceState.Operational,
        "degraded_performance" => ServiceState.Degraded,
        "under_maintenance" => ServiceState.Maintenance,
        "partial_outage" => ServiceState.PartialOutage,
        "major_outage" => ServiceState.Unavailable,
        _ => ServiceState.Unknown,
    };

    private static ServiceState MapIndicator(string? indicator) => indicator switch
    {
        "none" => ServiceState.Operational,
        "minor" => ServiceState.Degraded,
        "maintenance" => ServiceState.Maintenance,
        "major" => ServiceState.PartialOutage,
        "critical" => ServiceState.Unavailable,
        _ => ServiceState.Unknown,
    };
}
