namespace Pulse1x.App.Models;

/// <summary>Categoria de agrupamento das empresas na central "Status dos Servidores".
/// A chave de localização de cada categoria é derivada em <see cref="StatusCatalog.CategoryKey"/>.</summary>
public enum StatusCategory
{
    Games,
    Communication,
    Streaming,
    Ai,
    Infrastructure,
    CloudStorage,
}

/// <summary>Estado atual de um serviço (ou geral de uma empresa). A ordem do enum é a ordem de
/// severidade usada para calcular o indicador geral (o pior estado presente "vence"), exceto
/// <see cref="Unknown"/> que representa "status indisponível" e nunca domina um estado conhecido.</summary>
public enum ServiceState
{
    Operational,     // 🟢 tudo funcionando
    Maintenance,     // 🟠 manutenção programada
    Degraded,        // 🟡 degradação de desempenho
    PartialOutage,   // 🔴 interrupção parcial
    Unavailable,     // ⚫ indisponível (queda total)
    Unknown,         // ⚪ status indisponível (não foi possível determinar)
}

/// <summary>Mapeia <see cref="ServiceState"/> para o emoji do indicador, a cor hex e a chave de
/// rótulo localizável. Segue a paleta e o padrão de selos coloridos já usados na categoria Latência.</summary>
public static class ServiceStatusVisuals
{
    public static string Emoji(ServiceState s) => s switch
    {
        ServiceState.Operational => "🟢",
        ServiceState.Degraded => "🟡",
        ServiceState.Maintenance => "🟠",
        ServiceState.PartialOutage => "🔴",
        ServiceState.Unavailable => "⚫",
        _ => "⚪",
    };

    public static string Hex(ServiceState s) => s switch
    {
        ServiceState.Operational => "#22C55E",
        ServiceState.Degraded => "#EAB308",
        ServiceState.Maintenance => "#F97316",
        ServiceState.PartialOutage => "#DC2626",
        ServiceState.Unavailable => "#6B7280",
        _ => "#9CA3AF",
    };

    public static string LabelKey(ServiceState s) => s switch
    {
        ServiceState.Operational => "Srv_StateOperational",
        ServiceState.Degraded => "Srv_StateDegraded",
        ServiceState.Maintenance => "Srv_StateMaintenance",
        ServiceState.PartialOutage => "Srv_StatePartial",
        ServiceState.Unavailable => "Srv_StateUnavailable",
        _ => "Srv_StateUnknown",
    };

    /// <summary>Combina dois estados devolvendo o mais severo (para o indicador geral da empresa).
    /// <see cref="ServiceState.Unknown"/> só prevalece se ambos forem desconhecidos.</summary>
    public static ServiceState Worst(ServiceState a, ServiceState b)
    {
        if (a == ServiceState.Unknown) return b;
        if (b == ServiceState.Unknown) return a;
        return (ServiceState)Math.Max((int)a, (int)b);
    }
}

/// <summary>Definição estática de uma empresa monitorada. O <c>ServerStatusService</c> tenta as
/// fontes nesta ordem: (1) se <see cref="StatuspageBase"/> está preenchido, lê o status real por
/// serviço pela API pública do Atlassian Statuspage (<c>{base}/api/v2/summary.json</c>) — por padrão
/// pegando os componentes de nível superior da página, ou, quando <see cref="StatuspageComponentNames"/>
/// está preenchido, buscando exatamente esses nomes em qualquer lugar da árvore de componentes (usado
/// quando os componentes de topo da página não representam serviços de verdade — ex.: a Cloudflare
/// agrupa a página por continente/data center, então o nível superior mostraria "África", "Ásia" etc.
/// em vez de "Dashboard", "API", "DNS"); (2) senão, se <see cref="ReachabilityUrl"/> está preenchido,
/// faz uma checagem de alcance HTTP (o servidor responder = 🟢 operacional; sem resposta = ⚫
/// indisponível) e aplica o resultado a todos os serviços de <see cref="FallbackServices"/>; (3) em
/// último caso, exibe "⚪ status indisponível". A <see cref="StatusUrl"/> é sempre o link para a
/// página oficial de status.</summary>
public record StatusCompanyDef(
    string Key,
    string Name,
    string Icon,
    StatusCategory Category,
    string StatusUrl,
    string? StatuspageBase,
    string? ReachabilityUrl,
    string[] FallbackServices,
    string[]? StatuspageComponentNames = null,
    // Fontes oficiais adicionais (não-Statuspage) verificadas manualmente: cada uma dá granularidade
    // real por serviço/categoria, mais precisa que uma simples checagem de alcance. Preencher no máximo
    // uma delas por empresa — a ordem de prioridade em ServerStatusService é Statuspage > Apple > Xbox
    // > Google Cloud > Reachability > "⚪ status indisponível".
    string[]? AppleServiceNames = null,
    string[]? XboxCategoryNames = null,
    string[]? GoogleCloudProductNames = null);

/// <summary>Catálogo das empresas monitoradas pela central "Status dos Servidores", organizado nas
/// categorias pedidas (Jogos, Comunicação, Streaming, IA, Infraestrutura, Nuvem). Para ampliar a
/// cobertura em tempo real de uma empresa, basta preencher o <c>StatuspageBase</c> dela — nenhuma
/// outra alteração de código é necessária.</summary>
public static class StatusCatalog
{
    public static string CategoryKey(StatusCategory c) => c switch
    {
        StatusCategory.Games => "Srv_CatGames",
        StatusCategory.Communication => "Srv_CatComm",
        StatusCategory.Streaming => "Srv_CatStreaming",
        StatusCategory.Ai => "Srv_CatAi",
        StatusCategory.Infrastructure => "Srv_CatInfra",
        _ => "Srv_CatCloud",
    };

    public static readonly IReadOnlyList<StatusCompanyDef> Companies = new[]
    {
        // ---- Jogos ----
        new StatusCompanyDef("steam", "Steam", "🕹️", StatusCategory.Games,
            "https://steamstat.us/", null, "https://store.steampowered.com/",
            new[] { "Loja", "Comunidade", "Login", "Matchmaking" }),
        new StatusCompanyDef("epic", "Epic Games", "🎮", StatusCategory.Games,
            "https://status.epicgames.com/", "https://status.epicgames.com", "https://store.epicgames.com/",
            new[] { "Login", "Loja", "Serviços de jogo" }),
        // API oficial de status da Microsoft (a mesma usada pela página support.xbox.com/xbox-live-status).
        new StatusCompanyDef("xbox", "Xbox Network", "🎯", StatusCategory.Games,
            "https://support.xbox.com/xbox-live-status", null, "https://www.xbox.com/",
            new[] { "Login", "Loja", "Jogos e apps", "Social" },
            XboxCategoryNames: new[] { "Account & profile", "Store & subscriptions", "Multiplayer gaming", "Cloud gaming & remote play" }),
        new StatusCompanyDef("psn", "PlayStation Network", "🎮", StatusCategory.Games,
            "https://status.playstation.com/", null, "https://www.playstation.com/",
            new[] { "Conta / Login", "PlayStation Store", "Jogos", "Social" }),
        new StatusCompanyDef("nintendo", "Nintendo", "🍄", StatusCategory.Games,
            "https://www.nintendo.co.jp/netinfo/en_US/index.html", null, "https://www.nintendo.com/",
            new[] { "Nintendo Switch Online", "eShop", "Jogos online" }),
        new StatusCompanyDef("ea", "EA", "⚽", StatusCategory.Games,
            "https://help.ea.com/en/help/downloads-and-online-access/eas-server-status/", null, "https://www.ea.com/",
            new[] { "EA App", "Origin", "Serviços de jogo" }),
        new StatusCompanyDef("ubisoft", "Ubisoft", "🌀", StatusCategory.Games,
            "https://www.ubisoft.com/en-us/help/article/000064180", null, "https://www.ubisoft.com/",
            new[] { "Ubisoft Connect", "Loja", "Serviços de jogo" }),
        new StatusCompanyDef("riot", "Riot Games", "🔴", StatusCategory.Games,
            "https://status.riotgames.com/", null, "https://www.riotgames.com/",
            new[] { "League of Legends", "VALORANT", "Teamfight Tactics", "Loja" }),
        new StatusCompanyDef("battlenet", "Battle.net", "⚔️", StatusCategory.Games,
            "https://us.battle.net/support/en/help/service-status", null, "https://www.blizzard.com/",
            new[] { "Login", "Loja", "Serviços de jogo" }),
        new StatusCompanyDef("rockstar", "Rockstar Games", "⭐", StatusCategory.Games,
            "https://support.rockstargames.com/servicestatus", null, "https://www.rockstargames.com/",
            new[] { "Social Club", "GTA Online", "Launcher" }),
        new StatusCompanyDef("gog", "GOG", "🎲", StatusCategory.Games,
            "https://www.gog.com/support", null, "https://www.gog.com/",
            new[] { "GOG Galaxy", "Loja", "Login" }),
        // status.roblox.com não é uma instância Atlassian Statuspage (é uma página própria sem API
        // pública de summary.json) — usa checagem de alcance em vez de status por serviço.
        new StatusCompanyDef("roblox", "Roblox", "🧱", StatusCategory.Games,
            "https://status.roblox.com/", null, "https://www.roblox.com/",
            new[] { "Site", "Jogos", "Login", "Economia" }),
        new StatusCompanyDef("minecraft", "Minecraft Services", "⛏️", StatusCategory.Games,
            "https://help.minecraft.net/hc/en-us", null, "https://www.minecraft.net/",
            new[] { "Autenticação", "Realms", "Loja" }),

        // ---- Comunicação ----
        new StatusCompanyDef("discord", "Discord", "💬", StatusCategory.Communication,
            "https://discordstatus.com/", "https://discordstatus.com", "https://discord.com/",
            new[] { "API", "Mensagens", "Voz", "Gateway" }),
        new StatusCompanyDef("telegram", "Telegram", "✈️", StatusCategory.Communication,
            "https://telegram.org/", null, "https://telegram.org/",
            new[] { "Mensagens", "Mídia", "Chamadas" }),
        new StatusCompanyDef("whatsapp", "WhatsApp", "💚", StatusCategory.Communication,
            "https://metastatus.com/whatsapp", null, "https://www.whatsapp.com/",
            new[] { "Mensagens", "Chamadas", "Mídia" }),
        new StatusCompanyDef("teams", "Microsoft Teams", "👥", StatusCategory.Communication,
            "https://portal.office.com/servicestatus", null, "https://teams.microsoft.com/",
            new[] { "Chat", "Reuniões", "Chamadas" }),

        // ---- Streaming ----
        new StatusCompanyDef("twitch", "Twitch", "🟣", StatusCategory.Streaming,
            "https://status.twitch.tv/", "https://status.twitch.tv", "https://www.twitch.tv/",
            new[] { "Vídeo (ao vivo)", "Chat", "Player" }),
        new StatusCompanyDef("youtube", "YouTube", "▶️", StatusCategory.Streaming,
            "https://www.google.com/appsstatus/dashboard/", null, "https://www.youtube.com/",
            new[] { "Vídeos", "Ao vivo", "Studio" }),
        new StatusCompanyDef("kick", "Kick", "🟩", StatusCategory.Streaming,
            "https://kick.com/", null, "https://kick.com/",
            new[] { "Streams", "Chat", "VODs" }),

        // ---- Inteligência Artificial ----
        new StatusCompanyDef("openai", "ChatGPT", "🤖", StatusCategory.Ai,
            "https://status.openai.com/", "https://status.openai.com", "https://chatgpt.com/",
            new[] { "ChatGPT", "API", "Playground" }),
        new StatusCompanyDef("claude", "Claude", "🧠", StatusCategory.Ai,
            "https://status.anthropic.com/", "https://status.anthropic.com", "https://claude.ai/",
            new[] { "Claude (app)", "API", "Console" }),
        new StatusCompanyDef("gemini", "Google Gemini", "✨", StatusCategory.Ai,
            "https://status.cloud.google.com/", null, "https://gemini.google.com/",
            new[] { "Gemini (app)", "Gemini API" }),

        // ---- Infraestrutura ----
        // A página de status da Cloudflare agrupa os componentes de nível superior por continente
        // (África, Ásia, Europa…) — cada grupo herda o pior status de qualquer um dos ~460 data
        // centers dentro dele, então quase sempre aparece "interrupção parcial" em algum continente
        // sem que isso signifique problema real para o usuário. Por isso pedimos por nome os
        // serviços de verdade (dentro do grupo "Cloudflare Sites and Services").
        new StatusCompanyDef("cloudflare", "Cloudflare", "☁️", StatusCategory.Infrastructure,
            "https://www.cloudflarestatus.com/", "https://www.cloudflarestatus.com", "https://www.cloudflare.com/",
            new[] { "CDN/Cache", "Authoritative DNS", "Dashboard", "API", "Access", "Zero Trust" },
            StatuspageComponentNames: new[] { "CDN/Cache", "Authoritative DNS", "Dashboard", "API", "Access", "Zero Trust" }),
        new StatusCompanyDef("aws", "AWS", "📦", StatusCategory.Infrastructure,
            "https://health.aws.amazon.com/health/status", null, "https://aws.amazon.com/",
            new[] { "EC2", "S3", "RDS", "Console" }),
        new StatusCompanyDef("azure", "Microsoft Azure", "🔷", StatusCategory.Infrastructure,
            "https://status.azure.com/", null, "https://azure.microsoft.com/",
            new[] { "Compute", "Storage", "Networking" }),
        // API oficial de incidentes do Google Cloud Status Dashboard (mesma fonte da página pública).
        new StatusCompanyDef("gcp", "Google Cloud", "🌩️", StatusCategory.Infrastructure,
            "https://status.cloud.google.com/", null, "https://cloud.google.com/",
            new[] { "Compute Engine", "Cloud Storage", "Networking", "Kubernetes Engine" },
            GoogleCloudProductNames: new[] { "Google Compute Engine", "Google Cloud Storage", "Virtual Private Cloud (VPC)", "Google Kubernetes Engine" }),

        // ---- Armazenamento em Nuvem ----
        new StatusCompanyDef("onedrive", "OneDrive", "🗂️", StatusCategory.CloudStorage,
            "https://portal.office.com/servicestatus", null, "https://onedrive.live.com/",
            new[] { "Sincronização", "Web", "Compartilhamento" }),
        new StatusCompanyDef("gdrive", "Google Drive", "📁", StatusCategory.CloudStorage,
            "https://www.google.com/appsstatus/dashboard/", null, "https://drive.google.com/",
            new[] { "Sincronização", "Web", "Docs" }),
        new StatusCompanyDef("dropbox", "Dropbox", "📮", StatusCategory.CloudStorage,
            "https://status.dropbox.com/", "https://status.dropbox.com", "https://www.dropbox.com/",
            new[] { "Site", "Sincronização", "API" }),
        // API oficial da Apple (mesma usada pela página apple.com/support/systemstatus).
        new StatusCompanyDef("icloud", "iCloud", "🍏", StatusCategory.CloudStorage,
            "https://www.apple.com/support/systemstatus/", null, "https://www.icloud.com/",
            new[] { "iCloud Drive", "Photos", "iCloud Backup", "iCloud Mail", "iCloud Account & Sign In" },
            AppleServiceNames: new[] { "iCloud Drive", "Photos", "iCloud Backup", "iCloud Mail", "iCloud Account & Sign In" }),
    };
}
