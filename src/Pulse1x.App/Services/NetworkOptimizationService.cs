using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Pulse1x.App.Services;

/// <summary>Provedor de DNS escolhível na categoria Latência (nomes iguais aos de
/// <see cref="NetworkLatencyService.PublicDnsServers"/>).</summary>
public enum DnsProvider { Automatic, Google, Cloudflare, Quad9, OpenDNS, AdGuard }

/// <summary>Resultado de uma ação de otimização: sucesso + chave de mensagem + detalhe opcional.
/// <paramref name="Args"/> preenche os {0}, {1} da mensagem.</summary>
public record OpResult(bool Success, string MessageKey, string? Detail = null, object?[]? Args = null)
{
    /// <summary>Mensagem já traduzida no idioma atual.</summary>
    public string Text => Args is { Length: > 0 } ? Localization.Loc.F(MessageKey, Args) : Localization.Loc.S(MessageKey);
}

/// <summary>Ajustes de driver aplicados pelas propriedades avançadas da placa de rede.</summary>
public enum AdapterTweak { WifiRoaming, WifiPrefer5GHz, PowerSaving, InterruptModeration, LargeSendOffload, FlowControl }

/// <summary>Uma propriedade avançada do driver (Get-NetAdapterAdvancedProperty).</summary>
public sealed class AdapterProperty
{
    public string DisplayName { get; set; } = "";
    public string DisplayValue { get; set; } = "";
    public string RegistryKeyword { get; set; } = "";
    public string RegistryValue { get; set; } = "";
    public string[]? ValidDisplayValues { get; set; }
    public string[]? ValidRegistryValues { get; set; }

    // O PowerShell 5.1 serializa listas de propriedades calculadas como {"value":[...],"Count":n};
    // por isso o script manda cada lista como texto separado pelo caractere 0x1F.
    public string? ValidDisplayJoined { get => null; set => ValidDisplayValues = Split(value); }
    public string? ValidRegistryJoined { get => null; set => ValidRegistryValues = Split(value); }

    private static string[] Split(string? joined) =>
        string.IsNullOrEmpty(joined) ? Array.Empty<string>() : joined.Split('\u001f');
}

/// <summary>
/// Aplica e desfaz otimizações de rede da categoria Latência. Toda alteração persistente é
/// registrada em um <see cref="OptimizationChangeLog"/> próprio (network-changes.json) para que
/// "Desfazer Todas as Alterações" restaure o estado original. Os resets de Winsock e TCP/IP guardam
/// antes uma cópia (.reg) das chaves que o Windows apaga, para o "Reverter" importá-la de volta.
/// As demais ações pontuais (flush DNS, renovar IP, reiniciar adaptador/serviços) não mudam
/// configuração persistente — apenas executam um comando do Windows.
/// </summary>
public class NetworkOptimizationService
{
    private readonly OptimizationChangeLog _log;
    public OptimizationChangeLog ChangeLog => _log;

    // Subgrupos/configurações do powercfg (GUIDs oficiais do Windows).
    private const string WirelessSub = "19cbb8fa-5279-450e-9fac-8a3d5fedd0c1";
    private const string WirelessPowerSaving = "12bbebe6-58d6-4636-95bb-3217ef867c1a";
    private const string UsbSub = "2a737441-1930-4402-8d77-b2bebba308a3";
    private const string UsbSelectiveSuspend = "48e6b7a6-50f5-4782-a5d4-53bb8f07e226";

    private const string SystemProfile = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
    private const string GamesProfile = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games";

    public NetworkOptimizationService(OptimizationChangeLog log) => _log = log;

    // ===================== Ações pontuais (sem estado persistente) =====================

    /// <summary>Limpa o cache de resolução de nomes (DNS).</summary>
    public async Task<OpResult> FlushDnsAsync()
    {
        var r = await RunAsync("ipconfig", "/flushdns");
        return new OpResult(r.code == 0, r.code == 0 ? "Lat_DoneFlushDns" : "Lat_OpFailed", r.output.Trim());
    }

    /// <summary>Libera e renova o endereço IP via DHCP.</summary>
    public async Task<OpResult> RenewIpAsync()
    {
        await RunAsync("ipconfig", "/release");
        var r = await RunAsync("ipconfig", "/renew");
        return new OpResult(r.code == 0, r.code == 0 ? "Lat_DoneRenewIp" : "Lat_OpFailed", r.output.Trim());
    }

    private const string WinsockKey = @"HKLM\SYSTEM\CurrentControlSet\Services\WinSock2\Parameters";
    private const string TcpipKey = @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters";
    private const string Tcpip6Key = @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters";

    /// <summary>Restaura o catálogo Winsock ao padrão (corrige pilha de sockets corrompida). Requer reinício.
    /// Antes, exporta o catálogo atual — o "Reverter" o importa de volta (VPNs, filtros de rede).</summary>
    public async Task<OpResult> ResetWinsockAsync()
    {
        if (!await BackupRegistryAsync(WinsockKey, "winsock", "Lat_ToolResetWinsock"))
            return new OpResult(false, "Lat_BackupFailed");
        var r = await RunAsync("netsh", "winsock reset");
        return new OpResult(r.code == 0, r.code == 0 ? "Lat_DoneResetWinsock" : "Lat_OpFailed", r.output.Trim());
    }

    /// <summary>Reseta a pilha TCP/IP ao padrão. Requer reinício para efeito completo. Antes, exporta
    /// os parâmetros de IPv4 e IPv6 (IP fixo, DNS manual, ajustes de cada placa) para o "Reverter".</summary>
    public async Task<OpResult> ResetTcpIpAsync()
    {
        if (!await BackupRegistryAsync(TcpipKey, "tcpip", "Lat_ToolResetTcp")
            || !await BackupRegistryAsync(Tcpip6Key, "tcpip6", "Lat_ToolResetTcp"))
            return new OpResult(false, "Lat_BackupFailed");
        var r = await RunAsync("netsh", "int ip reset");
        return new OpResult(r.code == 0, r.code == 0 ? "Lat_DoneResetTcp" : "Lat_OpFailed", r.output.Trim());
    }

    /// <summary>Exporta uma chave do registro para AppData\Pulse1x\network-backups e registra o arquivo
    /// no log. Se já há uma cópia ativa da mesma chave, mantém a mais antiga (o estado original).</summary>
    private async Task<bool> BackupRegistryAsync(string regKey, string label, string titleKey)
    {
        if (_log.GetAllActive().Any(c => c.ValueKind == "reg-backup" && c.ValueName == regKey && File.Exists(c.OldValue)))
            return true;
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pulse1x", "network-backups");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, $"{label}-{DateTime.Now:yyyyMMdd-HHmmss}.reg");
        var r = await RunAsync("reg", $"export \"{regKey}\" \"{file}\" /y");
        if (r.code != 0 || !File.Exists(file)) return false;
        Record("reg-backup", regKey, regKey, file, null, titleKey);
        return true;
    }

    /// <summary>Desabilita e reabilita o adaptador ativo (reconexão limpa). Pontual, não persistente.</summary>
    public async Task<OpResult> RestartAdapterAsync()
    {
        var nic = NetworkLatencyService.ActiveAdapter();
        if (nic is null) return new OpResult(false, "Lat_NoAdapter");

        string name = nic.Name;
        await RunAsync("netsh", $"interface set interface name=\"{name}\" admin=disabled");
        await Task.Delay(1500);
        var r = await RunAsync("netsh", $"interface set interface name=\"{name}\" admin=enabled");
        return new OpResult(r.code == 0, r.code == 0 ? "Lat_DoneRestartAdapter" : "Lat_OpFailed", name);
    }

    /// <summary>Reinicia serviços de rede seguros (cache DNS e cliente DHCP). Ignora falhas de serviço protegido.</summary>
    public async Task<OpResult> RestartNetworkServicesAsync()
    {
        // Restart-Service com -Force lida com dependências; serviços protegidos apenas falham em silêncio.
        const string ps = "foreach ($s in 'Dnscache','Dhcp') { try { Restart-Service -Name $s -Force -ErrorAction Stop } catch {} }";
        var r = await RunAsync("powershell", $"-NoProfile -ExecutionPolicy Bypass -Command \"{ps}\"");
        return new OpResult(true, "Lat_DoneRestartServices", r.output.Trim());
    }

    // ===================== Tweaks reversíveis (registrados no log) =====================

    /// <summary>Desativa a economia de energia do adaptador Wi-Fi (modo Máximo Desempenho, índice 0).</summary>
    public async Task<OpResult> DisableWifiPowerSavingAsync()
    {
        await DisablePowerIndexAsync(WirelessSub, WirelessPowerSaving, "WifiPowerSaving", "Lat_ToolWifiPower");
        return new OpResult(true, "Lat_DoneWifiPower");
    }

    /// <summary>Desativa a suspensão seletiva de USB (evita o adaptador USB Wi-Fi "dormir").</summary>
    public async Task<OpResult> DisableSelectiveSuspendAsync()
    {
        await DisablePowerIndexAsync(UsbSub, UsbSelectiveSuspend, "SelectiveSuspend", "Lat_ToolSelectiveSuspend");
        return new OpResult(true, "Lat_DoneSelectiveSuspend");
    }

    /// <summary>Habilita Receive Side Scaling (distribui o tráfego de rede entre vários núcleos).</summary>
    public async Task<OpResult> EnableRssAsync()
    {
        var settings = await ReadTcpAsync();
        await RunAsync("netsh", "int tcp set global rss=enabled");
        // Só registra quando o estado anterior era CLARAMENTE desligado. Antes, qualquer coisa que
        // não contivesse "enabled" (leitura "—", "habilitado" de um Windows traduzido) virava
        // "estava desligado", e Desfazer tudo DESLIGAVA um RSS que sempre esteve ligado.
        bool wasDisabled = settings.rss.StartsWith("disabled", StringComparison.OrdinalIgnoreCase)
                           || settings.rss.StartsWith("desabilitad", StringComparison.OrdinalIgnoreCase);
        bool alreadyLogged = _log.GetAllActive().Any(c => c.ValueKind == "netsh-rss");
        if (wasDisabled && !alreadyLogged)
            Record("netsh-rss", "rss", "Rss", "disabled", "enabled", "Lat_ToolRss");
        return new OpResult(true, "Lat_DoneRss");
    }

    /// <summary>Ajusta o Auto-Tuning da janela de recepção para "normal" (recomendado para a maioria).</summary>
    public async Task<OpResult> SetAutoTuningNormalAsync()
    {
        var settings = await ReadTcpAsync();
        string old = ParseAutoTuning(settings.autotune);
        await RunAsync("netsh", "int tcp set global autotuninglevel=normal");
        bool autoLogged = _log.GetAllActive().Any(c => c.ValueKind == "netsh-autotuning");
        if (!old.Equals("normal", StringComparison.OrdinalIgnoreCase) && old != "—" && !autoLogged)
            Record("netsh-autotuning", "autotuning", "AutoTuning", old, "normal", "Lat_ToolAutoTuning");
        return new OpResult(true, "Lat_DoneAutoTuning");
    }

    /// <summary>Troca os servidores DNS do adaptador ativo (Automático/Google/Cloudflare), guardando o anterior.</summary>
    public async Task<OpResult> SetDnsAsync(DnsProvider provider)
    {
        var nic = NetworkLatencyService.ActiveAdapter();
        if (nic is null) return new OpResult(false, "Lat_NoAdapter");
        string name = nic.Name;

        // Guarda a configuração anterior só uma vez (a primeira troca). DnsAddresses mistura os
        // servidores entregues pelo DHCP com os manuais — gravar esses como "anterior" fazia o
        // desfazer fixar o DNS do roteador como estático. O que conta é o NameServer da interface:
        // vazio = DHCP; preenchido = DNS manual do usuário.
        bool alreadyRecorded = _log.GetAllActive().Any(c => c.ValueKind == "netsh-dns" && c.KeyPath == name);
        string oldDns = ReadStaticDns(nic.Id);

        if (provider == DnsProvider.Automatic)
        {
            var rr = await RunAsync("netsh", $"interface ip set dns name=\"{name}\" source=dhcp");
            if (rr.code != 0) return new OpResult(false, "Lat_OpFailed", rr.output.Trim());
            await RunAsync("ipconfig", "/flushdns");
            // Antes não gravava nada: quem tinha DNS manual e escolhia "Automático" perdia o DNS
            // antigo sem ter como voltar.
            if (!alreadyRecorded && oldDns.Length > 0)
                Record("netsh-dns", name, "Dns", oldDns, "dhcp", "Lat_ToolDns");
            return new OpResult(true, "Lat_DoneDnsAuto");
        }

        var server = NetworkLatencyService.PublicDnsServers.First(s => s.Name == provider.ToString());
        var (primary, secondary) = (server.Primary, server.Secondary);

        var r1 = await RunAsync("netsh", $"interface ip set dns name=\"{name}\" static {primary} primary validate=no");
        await RunAsync("netsh", $"interface ip add dns name=\"{name}\" {secondary} index=2 validate=no");
        if (r1.code != 0) return new OpResult(false, "Lat_OpFailed", r1.output.Trim());
        // Sem limpar o cache, nomes já resolvidos continuavam vindo do DNS antigo por horas.
        await RunAsync("ipconfig", "/flushdns");

        if (!alreadyRecorded)
            Record("netsh-dns", name, "Dns", string.IsNullOrEmpty(oldDns) ? "dhcp" : oldDns, $"{primary},{secondary}", "Lat_ToolDns");
        return new OpResult(true, "Lat_DoneDnsSet", Args: new object?[] { server.Name, $"{primary} / {secondary}" });
    }

    /// <summary>Qual provedor público está configurado agora (pelo DNS manual da interface), para a
    /// lista da tela abrir mostrando o que vale de verdade em vez de sempre "Automático".</summary>
    /// <returns>null quando é um DNS manual que não está na lista (a tela fica sem seleção).</returns>
    public static DnsProvider? CurrentDnsProvider()
    {
        var nic = NetworkLatencyService.ActiveAdapter();
        if (nic is null) return DnsProvider.Automatic;
        string first = ReadStaticDns(nic.Id).Split(',').FirstOrDefault() ?? "";
        if (first.Length == 0) return DnsProvider.Automatic;
        var match = NetworkLatencyService.PublicDnsServers.FirstOrDefault(s => s.Primary == first || s.Secondary == first);
        return match.Name is not null && Enum.TryParse<DnsProvider>(match.Name, out var p) ? p : null;
    }

    /// <summary>Perfil Competitivo: foca latência mínima — desativa o algoritmo de Nagle no adaptador,
    /// remove o limite de throttling de rede e maximiza a responsividade do agendador multimídia.</summary>
    public async Task<OpResult> ApplyCompetitiveProfileAsync()
    {
        // Responsividade do sistema e throttling de rede (afetam latência em jogos/streaming).
        SetDword(SystemProfile, "SystemResponsiveness", 0, "Lat_ToolCompetitive");
        await RemoveNetworkThrottlingAsync();
        // DWORD, como no perfil original do Windows (texto aqui era ignorado pelo MMCSS e, ao
        // desfazer, apagava os valores de fábrica).
        SetDword(GamesProfile, "GPU Priority", 8, "Lat_ToolCompetitive");
        SetDword(GamesProfile, "Priority", 6, "Lat_ToolCompetitive");
        SetString(GamesProfile, "Scheduling Category", "High", "Lat_ToolCompetitive");

        // Algoritmo de Nagle (junta pacotes pequenos e adiciona atraso) e coalescência de recepção.
        await DisableNagleAsync();
        await EnableRssAsync();
        await SetAutoTuningNormalAsync();
        await DisableRscAsync();
        // Upload P2P do Windows Update em segundo plano enche a fila do roteador (bufferbloat).
        await DisableDeliveryOptimizationP2PAsync();
        return new OpResult(true, "Lat_DoneCompetitive");
    }

    /// <summary>Perfil Estabilidade: prioriza uma conexão constante — Auto-Tuning normal, RSS ligado e
    /// economia de energia do Wi-Fi desligada (evita micro-quedas), sem tweaks agressivos de latência.</summary>
    public async Task<OpResult> ApplyStabilityProfileAsync()
    {
        await SetAutoTuningNormalAsync();
        await EnableRssAsync();
        await DisableWifiPowerSavingAsync();
        await DisableSelectiveSuspendAsync();
        await PreventAdapterPowerOffAsync();
        return new OpResult(true, "Lat_DoneStability");
    }

    /// <summary>Aplica o conjunto seguro recomendado de uma vez ("Aplicar Todas as Otimizações").
    /// Fica de fora o que derruba a conexão por alguns segundos (propriedades do driver) ou muda o
    /// comportamento do Wi-Fi (busca em segundo plano) — esses o usuário aplica um a um.</summary>
    public async Task<OpResult> ApplyAllAsync()
    {
        await FlushDnsAsync();
        await SetAutoTuningNormalAsync();
        await EnableRssAsync();
        await DisableWifiPowerSavingAsync();
        await DisableSelectiveSuspendAsync();
        await PreventAdapterPowerOffAsync();
        await DisableDeliveryOptimizationP2PAsync();
        await RemoveNetworkThrottlingAsync();
        await DisableNagleAsync();
        return new OpResult(true, "Lat_DoneApplyAll");
    }

    // ===================== Pilha TCP: Nagle, throttling, RSC =====================

    /// <summary>Desliga o algoritmo de Nagle e o ACK atrasado na interface ativa: pacotes pequenos
    /// (comandos de jogo em TCP, como em MMOs e MOBAs) saem na hora em vez de esperar companhia.</summary>
    public Task<OpResult> DisableNagleAsync() => Task.Run(() =>
    {
        var nic = NetworkLatencyService.ActiveAdapter();
        if (nic is null) return new OpResult(false, "Lat_NoAdapter");
        string ifacePath = $@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{nic.Id}";
        if (GetDword(ifacePath, "TcpAckFrequency") == 1 && GetDword(ifacePath, "TCPNoDelay") == 1)
            return new OpResult(true, "Lat_AdvAlready");
        SetDword(ifacePath, "TcpAckFrequency", 1, "Lat_ToolNagle");
        SetDword(ifacePath, "TCPNoDelay", 1, "Lat_ToolNagle");
        return new OpResult(true, "Lat_DoneNagle", Args: new object?[] { nic.Name });
    });

    /// <summary>Remove o limite que o Windows impõe ao tráfego de rede enquanto toca áudio/vídeo
    /// (NetworkThrottlingIndex — por padrão 10 pacotes/ms).</summary>
    public Task<OpResult> RemoveNetworkThrottlingAsync() => Task.Run(() =>
    {
        int off = unchecked((int)0xFFFFFFFF);
        if (GetDword(SystemProfile, "NetworkThrottlingIndex") == off) return new OpResult(true, "Lat_AdvAlready");
        SetDword(SystemProfile, "NetworkThrottlingIndex", off, "Lat_ToolThrottling");
        return new OpResult(true, "Lat_DoneThrottling");
    });

    /// <summary>Desliga o Receive Segment Coalescing: a pilha deixa de segurar pacotes recebidos para
    /// juntá-los em um só (economiza CPU em downloads, mas atrasa cada pacote).</summary>
    public async Task<OpResult> DisableRscAsync()
    {
        // Get/Set-NetOffloadGlobalSetting devolvem "Enabled"/"Disabled" em qualquer idioma
        // (a saída do netsh vem traduzida).
        var read = await RunPowerShellAsync("(Get-NetOffloadGlobalSetting -ErrorAction Stop).ReceiveSegmentCoalescing");
        string old = read.output.Trim();
        if (old.Equals("Disabled", StringComparison.OrdinalIgnoreCase)) return new OpResult(true, "Lat_AdvAlready");
        if (!old.Equals("Enabled", StringComparison.OrdinalIgnoreCase)) return new OpResult(false, "Lat_OpFailed", old);
        var r = await RunPowerShellAsync("Set-NetOffloadGlobalSetting -ReceiveSegmentCoalescing Disabled -ErrorAction Stop; 'OK'");
        if (!r.output.Contains("OK")) return new OpResult(false, "Lat_OpFailed", r.output.Trim());
        if (!_log.GetAllActive().Any(c => c.ValueKind == "netoffload-rsc"))
            Record("netoffload-rsc", "global", "ReceiveSegmentCoalescing", "Enabled", "Disabled", "Lat_ToolRsc");
        return new OpResult(true, "Lat_DoneRsc");
    }

    // ===================== Placa de rede: energia e driver =====================

    private const string NetClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";

    /// <summary>Desmarca "O computador pode desligar este dispositivo para economizar energia" na
    /// placa ativa e na Wi-Fi (bits 0x18 de PnPCapabilities). É o motivo clássico de o Wi-Fi
    /// "sumir" após alguns minutos parado ou ao voltar da suspensão.</summary>
    public Task<OpResult> PreventAdapterPowerOffAsync() => Task.Run(() =>
    {
        var nics = new[] { NetworkLatencyService.ActiveAdapter(), NetworkLatencyService.WifiAdapter() }
            .Where(n => n is not null).DistinctBy(n => n!.Id).ToList();
        if (nics.Count == 0) return new OpResult(false, "Lat_NoAdapter");

        int changed = 0;
        foreach (var nic in nics)
        {
            string? path = FindAdapterClassKey(nic!.Id);
            if (path is null) continue;
            int old = GetDword(path, "PnPCapabilities") ?? 0;
            int value = old | 0x18;
            if (value == old) continue;
            SetDword(path, "PnPCapabilities", value, "Lat_ToolAdapterPower");
            changed++;
        }
        return new OpResult(true, changed > 0 ? "Lat_DoneAdapterPower" : "Lat_AdvAlready");
    });

    /// <summary>Delivery Optimization só por HTTP (sem enviar atualizações para outros PCs da
    /// internet/rede). O upload P2P em segundo plano é uma causa comum de ping alto "do nada".</summary>
    public Task<OpResult> DisableDeliveryOptimizationP2PAsync() => Task.Run(() =>
    {
        const string key = @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization";
        if (GetDword(key, "DODownloadMode") == 0) return new OpResult(true, "Lat_AdvAlready");
        SetDword(key, "DODownloadMode", 0, "Lat_ToolDeliveryOpt");
        return new OpResult(true, "Lat_DoneDeliveryOpt");
    });

    private static string? FindAdapterClassKey(string interfaceId)
    {
        try
        {
            using var cls = Registry.LocalMachine.OpenSubKey(NetClassKey);
            if (cls is null) return null;
            foreach (var sub in cls.GetSubKeyNames())
            {
                if (sub.Length != 4 || !sub.All(char.IsDigit)) continue;
                using var k = cls.OpenSubKey(sub);
                if (string.Equals(k?.GetValue("NetCfgInstanceId") as string, interfaceId, StringComparison.OrdinalIgnoreCase))
                    return $@"{NetClassKey}\{sub}";
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Ajusta propriedades avançadas do driver da placa (as mesmas do Gerenciador de Dispositivos).
    /// Os nomes mudam por fabricante e idioma, então a escolha é feita por palavra-chave do
    /// registro + nome exibido, e o valor-alvo é procurado entre os valores que o próprio driver
    /// aceita — nada é inventado. A placa reinicia uma vez no fim (2–5 s sem conexão).
    /// </summary>
    public async Task<OpResult> ApplyAdapterTweakAsync(AdapterTweak tweak)
    {
        bool wifiOnly = tweak is AdapterTweak.WifiRoaming or AdapterTweak.WifiPrefer5GHz;
        var nic = await Task.Run(() => wifiOnly ? NetworkLatencyService.WifiAdapter() : NetworkLatencyService.ActiveAdapter());
        if (nic is null) return new OpResult(false, wifiOnly ? "Lat_AdvNoWifi" : "Lat_NoAdapter");
        string name = nic.Name;

        var props = await ReadAdapterPropertiesAsync(name);
        if (props is null) return new OpResult(false, "Lat_OpFailed");
        var plan = PlanAdapterTweak(tweak, props);
        if (plan.Count == 0) return new OpResult(false, "Lat_AdvNotSupported", Args: new object?[] { name });
        var pending = plan.Where(p => p.Target != p.Prop.RegistryValue).ToList();
        if (pending.Count == 0) return new OpResult(true, "Lat_AdvAlready");

        var sb = new StringBuilder();
        foreach (var (prop, target) in pending)
            sb.Append($"try {{ Set-NetAdapterAdvancedProperty -Name {Ps(name)} -RegistryKeyword {Ps(prop.RegistryKeyword)} -RegistryValue {Ps(target)} -NoRestart -ErrorAction Stop; 'OK:{prop.RegistryKeyword}' }} catch {{ 'FAIL:{prop.RegistryKeyword}' }}\n");
        sb.Append($"Restart-NetAdapter -Name {Ps(name)} -Confirm:$false -ErrorAction SilentlyContinue\n");
        var r = await RunPowerShellAsync(sb.ToString());

        string titleKey = TweakTitleKey(tweak);
        int ok = 0;
        foreach (var (prop, target) in pending)
        {
            if (!r.output.Contains("OK:" + prop.RegistryKeyword)) continue;
            ok++;
            bool logged = _log.GetAllActive().Any(c => c.ValueKind == "netadv" && c.KeyPath == name && c.ValueName == prop.RegistryKeyword);
            if (!logged) Record("netadv", name, prop.RegistryKeyword, prop.RegistryValue, target, titleKey);
        }
        if (ok == 0) return new OpResult(false, "Lat_OpFailed", r.output.Trim());
        string changedNames = string.Join(", ", pending.Where(p => r.output.Contains("OK:" + p.Prop.RegistryKeyword)).Select(p => p.Prop.DisplayName));
        return new OpResult(true, "Lat_AdvDone", Args: new object?[] { changedNames, name });
    }

    private static string TweakTitleKey(AdapterTweak t) => t switch
    {
        AdapterTweak.WifiRoaming => "Lat_ToolWifiRoaming",
        AdapterTweak.WifiPrefer5GHz => "Lat_ToolWifiPrefer5",
        AdapterTweak.PowerSaving => "Lat_ToolDriverPower",
        AdapterTweak.LargeSendOffload => "Lat_ToolLso",
        AdapterTweak.FlowControl => "Lat_ToolFlowControl",
        _ => "Lat_ToolInterruptMod",
    };

    private static async Task<List<AdapterProperty>?> ReadAdapterPropertiesAsync(string adapterName)
    {
        string script =
            $"$p = Get-NetAdapterAdvancedProperty -Name {Ps(adapterName)} -ErrorAction Stop | Select-Object DisplayName, DisplayValue, RegistryKeyword, " +
            "@{n='RegistryValue';e={@($_.RegistryValue) -join ''}}, @{n='ValidDisplayJoined';e={@($_.ValidDisplayValues) -join [char]31}}, @{n='ValidRegistryJoined';e={@($_.ValidRegistryValues) -join [char]31}}\n" +
            "ConvertTo-Json -InputObject @($p) -Depth 3 -Compress";
        var r = await RunPowerShellAsync(script);
        int start = r.output.IndexOf('[');
        if (start < 0) return null;
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<AdapterProperty>>(r.output[start..],
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return null; }
    }

    private static readonly HashSet<string> PowerKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "*EEE", "AdvancedEEE", "EEELinkAdvertisement", "EnableGreenEthernet", "PowerSavingMode", "LowPowerEnable",
        "*PacketCoalescing", "ULPMode", "MIMOPowerSaveMode", "uAPSDSupport", "*DeviceSleepOnDisconnect",
        "AutoPowerSaveModeEnabled", "ReduceSpeedOnPowerDown", "SavePowerNowEnabled", "EnablePowerManagement",
    };

    private static readonly HashSet<string> LsoKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "*LsoV1IPv4", "*LsoV2IPv4", "*LsoV2IPv6",
    };

    private static readonly Regex PowerNameRegex = new(
        @"power ?sav|economia de energia|energy.?efficient|energia eficiente|green ?ethernet|ethernet verde|packet coalesc|mimo power|u-?apsd|ultra low power",
        RegexOptions.IgnoreCase);

    private static readonly Regex DisabledValueRegex = new(
        @"^\W*\d*\W*(disabled?|desabilitad[oa]|desativad[oa]|desactivad[oa]|deshabilitad[oa]|deaktiviert|off|no smps|nenhum|none)\W*$",
        RegexOptions.IgnoreCase);

    /// <summary>Escolhe quais propriedades mudar e para qual valor de registro (só valores aceitos pelo driver).</summary>
    internal static List<(AdapterProperty Prop, string Target)> PlanAdapterTweak(AdapterTweak tweak, IEnumerable<AdapterProperty> props)
    {
        var plan = new List<(AdapterProperty, string)>();
        foreach (var p in props)
        {
            var display = p.ValidDisplayValues ?? Array.Empty<string>();
            var registry = p.ValidRegistryValues ?? Array.Empty<string>();
            if (display.Length == 0 || display.Length != registry.Length) continue; // campo livre: não mexe
            string id = p.RegistryKeyword + " " + p.DisplayName;

            int pick = -1;
            switch (tweak)
            {
                case AdapterTweak.WifiRoaming:
                    if (!Regex.IsMatch(id, "roam", RegexOptions.IgnoreCase) || Regex.IsMatch(id, "band|prefer", RegexOptions.IgnoreCase)) continue;
                    pick = Array.FindIndex(display, v => Regex.IsMatch(v, @"lowest|mais baix|más baj|niedrigste|plus bas|m[ií]nim", RegexOptions.IgnoreCase));
                    if (pick < 0)
                        pick = Array.FindIndex(display, v => Regex.IsMatch(v, @"^\W*\d*\W*(low|baix[oa]|baj[oa])\W*$", RegexOptions.IgnoreCase));
                    break;
                case AdapterTweak.WifiPrefer5GHz:
                    if (!Regex.IsMatch(id, @"prefer", RegexOptions.IgnoreCase) || !Regex.IsMatch(id, "band", RegexOptions.IgnoreCase)) continue;
                    // "Miracast prefer band" (MediaTek) é o canal do espelhamento de tela, não do Wi-Fi.
                    if (Regex.IsMatch(id, @"miracast|wfd|direct|hotspot|softap|\bGO\b", RegexOptions.IgnoreCase)) continue;
                    pick = Array.FindIndex(display, v => Regex.IsMatch(v, @"(?<![\d,.])5\s*G", RegexOptions.IgnoreCase));
                    break;
                case AdapterTweak.PowerSaving:
                    if (Regex.IsMatch(id, "wake|wol", RegexOptions.IgnoreCase)) continue;
                    if (!PowerKeywords.Contains(p.RegistryKeyword) && !PowerNameRegex.IsMatch(p.DisplayName)) continue;
                    pick = Array.FindIndex(display, v => DisabledValueRegex.IsMatch(v));
                    break;
                case AdapterTweak.InterruptModeration:
                    if (!p.RegistryKeyword.Equals("*InterruptModeration", StringComparison.OrdinalIgnoreCase)) continue;
                    pick = Array.FindIndex(display, v => DisabledValueRegex.IsMatch(v));
                    break;
                case AdapterTweak.LargeSendOffload:
                case AdapterTweak.FlowControl:
                    // Palavras-chave padronizadas pela Microsoft (NDIS): 0 = desativado em todos os drivers.
                    bool wanted = tweak == AdapterTweak.FlowControl
                        ? p.RegistryKeyword.Equals("*FlowControl", StringComparison.OrdinalIgnoreCase)
                        : LsoKeywords.Contains(p.RegistryKeyword);
                    if (!wanted) continue;
                    pick = Array.FindIndex(display, v => DisabledValueRegex.IsMatch(v));
                    if (pick < 0) pick = Array.IndexOf(registry, "0");
                    break;
            }
            if (pick >= 0) plan.Add((p, registry[pick]));
        }
        return plan;
    }

    // ===================== Wi-Fi: busca em segundo plano, MTU, relatório =====================

    /// <summary>A busca automática de redes (WLAN AutoConfig) está ligada? null = sem Wi-Fi/ilegível.</summary>
    public async Task<bool?> ReadWifiBackgroundScanAsync()
    {
        var nic = await Task.Run(NetworkLatencyService.WifiAdapter);
        if (nic is null) return null;
        string output = (await RunAsync("netsh", "wlan show settings")).output;
        return ParseAutoConfig(output, nic.Name);
    }

    internal static bool? ParseAutoConfig(string output, string interfaceName)
    {
        foreach (var line in output.Split('\n'))
        {
            if (!line.Contains($"\"{interfaceName}\"")) continue;
            if (!line.Contains("configura", StringComparison.OrdinalIgnoreCase)) continue; // PT "configuração" / EN "configuration"
            if (Regex.IsMatch(line, "desabilitad|desativad|disabled", RegexOptions.IgnoreCase)) return false;
            if (Regex.IsMatch(line, "habilitad|ativad|enabled", RegexOptions.IgnoreCase)) return true;
        }
        return null;
    }

    /// <summary>
    /// Modo partida do Wi-Fi: desliga a busca automática de redes, que a cada ~1 minuto faz o
    /// adaptador sair do canal por alguns milissegundos e gera os picos de ping periódicos típicos
    /// do Wi-Fi. A conexão atual continua; o Windows só não troca nem reconecta sozinho. É de
    /// sessão: o Pulse1x religa ao desativar, ao fechar e ao abrir de novo.
    /// </summary>
    public async Task<OpResult> SetWifiBackgroundScanAsync(bool enabled)
    {
        var nic = await Task.Run(NetworkLatencyService.WifiAdapter);
        if (nic is null) return new OpResult(false, "Lat_AdvNoWifi");
        string name = nic.Name;
        var r = await RunAsync("netsh", $"wlan set autoconfig enabled={(enabled ? "yes" : "no")} interface=\"{name}\"");
        if (r.code != 0) return new OpResult(false, "Lat_OpFailed", r.output.Trim());

        var logged = _log.GetAllActive().Where(c => c.ValueKind == "netsh-autoconfig").ToList();
        if (enabled)
            foreach (var c in logged) _log.MarkReverted(c);
        else if (!logged.Any(c => c.KeyPath == name))
            Record("netsh-autoconfig", name, "AutoConfig", "yes", "no", "Lat_WifiGameMode");
        return new OpResult(true, enabled ? "Lat_DoneWifiGameOff" : "Lat_DoneWifiGameOn");
    }

    /// <summary>Religa a busca automática deixada desligada (fechamento do app ou sessão anterior
    /// que terminou sem desligar o modo). Síncrono para poder rodar no OnExit.</summary>
    public void RestoreSessionWifiSettings()
    {
        foreach (var c in _log.GetAllActive().Where(c => c.ValueKind == "netsh-autoconfig").ToList())
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo("netsh", $"wlan set autoconfig enabled=yes interface=\"{c.KeyPath}\"")
                { UseShellExecute = false, CreateNoWindow = true });
                p?.WaitForExit(5000);
            }
            catch { }
            _log.MarkReverted(c);
        }
    }

    /// <summary>MTU IPv4 atual de uma interface (netsh), ou 0 se não der para ler.</summary>
    public async Task<int> ReadMtuAsync(string interfaceName)
    {
        string output = (await RunAsync("netsh", "interface ipv4 show subinterfaces")).output;
        return ParseMtu(output, interfaceName);
    }

    internal static int ParseMtu(string output, string interfaceName)
    {
        foreach (var line in output.Split('\n'))
        {
            var m = Regex.Match(line, @"^\s*(\d+)\s+\d+\s+\d+\s+\d+\s+(.+?)\s*$");
            if (m.Success && m.Groups[2].Value.Equals(interfaceName, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(m.Groups[1].Value, out int mtu)) return mtu;
        }
        return 0;
    }

    /// <summary>Grava o MTU encontrado pelo teste no adaptador ativo (reversível).</summary>
    public async Task<OpResult> SetMtuAsync(int mtu)
    {
        if (mtu is < 576 or > 9000) return new OpResult(false, "Lat_OpFailed");
        var nic = await Task.Run(NetworkLatencyService.ActiveAdapter);
        if (nic is null) return new OpResult(false, "Lat_NoAdapter");
        string name = nic.Name;
        int old = await ReadMtuAsync(name);
        if (old == mtu) return new OpResult(true, "Lat_AdvAlready");
        var r = await RunAsync("netsh", $"interface ipv4 set subinterface \"{name}\" mtu={mtu} store=persistent");
        if (r.code != 0) return new OpResult(false, "Lat_OpFailed", r.output.Trim());
        bool logged = _log.GetAllActive().Any(c => c.ValueKind == "netsh-mtu" && c.KeyPath == name);
        if (!logged && old > 0) Record("netsh-mtu", name, "MTU", old.ToString(), mtu.ToString(), "Lat_MtuTitle");
        return new OpResult(true, "Lat_DoneMtu", Args: new object?[] { mtu, name });
    }

    /// <summary>Gera o relatório de Wi-Fi do Windows (quedas, desconexões e erros dos últimos 3
    /// dias) e abre no navegador.</summary>
    public async Task<OpResult> OpenWlanReportAsync()
    {
        var r = await RunAsync("netsh", "wlan show wlanreport");
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Microsoft", "Windows", "WlanReport", "wlan-report-latest.html");
        if (!File.Exists(path)) return new OpResult(false, "Lat_OpFailed", r.output.Trim());
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch { return new OpResult(false, "Lat_OpFailed"); }
        return new OpResult(true, "Lat_DoneWlanReport");
    }

    /// <summary>Lê se a economia de energia do Wi-Fi e a suspensão seletiva de USB estão ativas
    /// agora — usado pelo diagnóstico de latência de drivers para sugerir a causa mais provável.</summary>
    public async Task<(bool wifiPowerSavingOn, bool usbSuspendOn)> ReadPowerSavingStatesAsync()
    {
        int? wifi = await ReadPowerIndexAsync(WirelessSub, WirelessPowerSaving);
        int? usb = await ReadPowerIndexAsync(UsbSub, UsbSelectiveSuspend);
        return (wifi is not null && wifi != 0, usb is not null && usb != 0);
    }

    // ===================== Reversão =====================

    public bool HasChanges() => _log.GetAllActive().Count > 0;

    private const string ScopePrefix = "network:";
    private static readonly AsyncLocal<string?> _scope = new();

    /// <summary>Roda uma ferramenta marcando tudo o que ela gravar com <paramref name="scopeKey"/>
    /// (chave do título do card), para depois ser revertida sozinha.</summary>
    public async Task<OpResult> RunScopedAsync(string scopeKey, Func<Task<OpResult>> action)
    {
        _scope.Value = scopeKey; // AsyncLocal: só vale dentro deste fluxo assíncrono
        try { return await action(); }
        finally { _scope.Value = null; }
    }

    /// <summary>Alterações ainda ativas feitas pela ferramenta. Registros de versões anteriores
    /// (sem escopo) são reconhecidos pelo título gravado, em português ou inglês.</summary>
    public IReadOnlyList<OptimizationChange> ChangesFor(string scopeKey)
    {
        var titles = new[] { Localization.LocalizationStrings.Pt.GetValueOrDefault(scopeKey), Localization.LocalizationStrings.En.GetValueOrDefault(scopeKey) };
        return _log.GetAllActive().Where(c => c.OptimizationId == ScopePrefix + scopeKey
                                              || (c.OptimizationId == "network" && titles.Contains(c.OptimizationTitle)))
                                  .ToList();
    }

    public bool HasChangesFor(string scopeKey) => ChangesFor(scopeKey).Count > 0;

    /// <summary>Desfaz só o que uma ferramenta alterou (com uma reinicialização por placa no fim).</summary>
    public async Task<OpResult> RevertScopeAsync(string scopeKey)
    {
        var changes = ChangesFor(scopeKey);
        if (changes.Count == 0) return new OpResult(true, "Lat_NothingToRevert");
        var adapters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Do mais novo para o mais antigo (ordem do log): se a mesma chave mudou duas vezes, termina
        // no valor original. O .Reverse() que havia aqui invertia isso e parava no valor intermediário.
        foreach (var change in changes)
        {
            if (change.ValueKind == "netadv") adapters.Add(change.KeyPath);
            await RevertChangeAsync(change, restartAdapter: false);
        }
        foreach (var name in adapters)
            await RunPowerShellAsync($"Restart-NetAdapter -Name {Ps(name)} -Confirm:$false -ErrorAction SilentlyContinue");
        bool needsReboot = changes.Any(c => c.ValueKind == "reg-backup");
        return new OpResult(true, needsReboot ? "Lat_RevertedReboot" : "Lat_Reverted", Args: new object?[] { changes.Count });
    }
    public IReadOnlyList<OptimizationChange> Changes => _log.GetAllActive();

    /// <summary>"Desfazer Todas as Alterações": reverte cada mudança registrada e devolve os padrões
    /// de rede do Windows (Winsock, TCP/IP, DNS automático, Auto-Tuning normal).</summary>
    public async Task<OpResult> RestoreAllAsync()
    {
        // Propriedades do driver: todas sem reiniciar e uma reinicialização por placa no fim, em
        // vez de derrubar a conexão a cada valor revertido.
        var adaptersToRestart = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // GetAllActive já vem do mais novo para o mais antigo: a cópia do registro feita antes de um
        // reset é importada depois de desfeitos os ajustes seguintes.
        foreach (var change in _log.GetAllActive())
        {
            if (change.ValueKind == "netadv") adaptersToRestart.Add(change.KeyPath);
            await RevertChangeAsync(change, restartAdapter: false);
        }
        foreach (var name in adaptersToRestart)
            await RunPowerShellAsync($"Restart-NetAdapter -Name {Ps(name)} -Confirm:$false -ErrorAction SilentlyContinue");

        // Só o Auto-Tuning volta ao padrão como rede de segurança — ele é o mesmo em qualquer PC.
        // DNS em DHCP, "winsock reset" e "int ip reset" eram executados SEMPRE aqui e apagavam
        // configurações do próprio usuário (DNS manual, IP fixo) que o Pulse1x nunca alterou;
        // essas continuam disponíveis como ferramentas separadas.
        await RunAsync("netsh", "int tcp set global autotuninglevel=normal");
        return new OpResult(true, "Lat_DoneRestoreAll");
    }

    /// <summary>Desfaz uma única alteração registrada, conforme o tipo (tag em <c>ValueKind</c>).</summary>
    public async Task RevertChangeAsync(OptimizationChange change, bool restartAdapter = true)
    {
        switch (change.ValueKind)
        {
            case "netadv":
                if (change.OldValue is not null)
                    await RunPowerShellAsync(
                        $"Set-NetAdapterAdvancedProperty -Name {Ps(change.KeyPath)} -RegistryKeyword {Ps(change.ValueName)} " +
                        $"-RegistryValue {Ps(change.OldValue)}{(restartAdapter ? "" : " -NoRestart")} -ErrorAction SilentlyContinue");
                break;
            case "netsh-autoconfig":
                await RunAsync("netsh", $"wlan set autoconfig enabled=yes interface=\"{change.KeyPath}\"");
                break;
            case "reg-backup":
                // Importa a cópia feita antes do reset (vale por completo após reiniciar o PC).
                if (change.OldValue is not null && File.Exists(change.OldValue))
                    await RunAsync("reg", $"import \"{change.OldValue}\"");
                break;
            case "netoffload-rsc":
                if (change.OldValue is "Enabled" or "Disabled")
                    await RunPowerShellAsync($"Set-NetOffloadGlobalSetting -ReceiveSegmentCoalescing {change.OldValue} -ErrorAction SilentlyContinue");
                break;
            case "netsh-mtu":
                if (int.TryParse(change.OldValue, out int oldMtu))
                    await RunAsync("netsh", $"interface ipv4 set subinterface \"{change.KeyPath}\" mtu={oldMtu} store=persistent");
                break;
            case "DWord":
            case "String":
                RevertRegistry(change);
                break;
            case "powerplan":
                if (!string.IsNullOrEmpty(change.OldValue))
                    await RunAsync("powercfg", $"/setactive {change.OldValue}");
                break;
            case "powerindex":
                {
                    var parts = change.KeyPath.Split(' ');
                    if (parts.Length == 2 && change.OldValue is not null)
                    {
                        // "ac|dc" desde a 1.13.0; registros antigos guardam só o índice de tomada.
                        var old = change.OldValue.Split('|');
                        await RunAsync("powercfg", $"/setacvalueindex SCHEME_CURRENT {parts[0]} {parts[1]} {old[0]}");
                        if (old.Length == 2)
                            await RunAsync("powercfg", $"/setdcvalueindex SCHEME_CURRENT {parts[0]} {parts[1]} {old[1]}");
                        await RunAsync("powercfg", "/setactive SCHEME_CURRENT");
                    }
                }
                break;
            case "netsh-rss":
                await RunAsync("netsh", $"int tcp set global rss={change.OldValue}");
                break;
            case "netsh-autotuning":
                await RunAsync("netsh", $"int tcp set global autotuninglevel={change.OldValue}");
                break;
            case "netsh-dns":
                if (change.OldValue == "dhcp" || string.IsNullOrEmpty(change.OldValue))
                    await RunAsync("netsh", $"interface ip set dns name=\"{change.KeyPath}\" source=dhcp");
                else
                {
                    var servers = change.OldValue.Split(',');
                    await RunAsync("netsh", $"interface ip set dns name=\"{change.KeyPath}\" static {servers[0]} primary");
                    for (int i = 1; i < servers.Length; i++)
                        await RunAsync("netsh", $"interface ip add dns name=\"{change.KeyPath}\" {servers[i]} index={i + 1}");
                }
                break;
        }
        _log.MarkReverted(change);
    }

    // ===================== Helpers =====================

    private void RevertRegistry(OptimizationChange c)
    {
        try
        {
            var root = c.Hive == "HKLM" ? Registry.LocalMachine : Registry.CurrentUser;
            if (c.OldValue is null)
            {
                using var key = root.OpenSubKey(c.KeyPath, writable: true);
                if (key?.GetValue(c.ValueName) is not null)
                    key.DeleteValue(c.ValueName, throwOnMissingValue: false);
                return;
            }
            using var wk = root.CreateSubKey(c.KeyPath);
            bool isDword = c.ValueKind == "DWord" || HardwareOptimizationService.IsGamesProfileDword(c.KeyPath, c.ValueName);
            if (isDword && int.TryParse(c.OldValue, out int dword))
                wk.SetValue(c.ValueName, dword, RegistryValueKind.DWord);
            else
                wk.SetValue(c.ValueName, c.OldValue, RegistryValueKind.String);
        }
        catch { }
    }

    // Grava um DWORD em HKLM e registra a alteração (valor antigo) para reversão exata.
    private void SetDword(string subKey, string name, int value, string titleKey)
    {
        int? old = GetDword(subKey, name);
        if (old == value) return;
        // Valor gravado como texto por versões antigas: o número dele ainda é o "valor anterior".
        if (old is null && int.TryParse(GetString(subKey, name), out int fromText)) old = fromText;
        try
        {
            using var wk = Registry.LocalMachine.CreateSubKey(subKey);
            wk.SetValue(name, value, RegistryValueKind.DWord);
        }
        catch { return; }
        Record("registry", subKey, name, old?.ToString(), value.ToString(), titleKey, "DWord", "HKLM");
    }

    private void SetString(string subKey, string name, string value, string titleKey)
    {
        string? old = GetString(subKey, name);
        if (old == value) return;
        try
        {
            using var wk = Registry.LocalMachine.CreateSubKey(subKey);
            wk.SetValue(name, value, RegistryValueKind.String);
        }
        catch { return; }
        Record("registry", subKey, name, old, value, titleKey, "String", "HKLM");
    }

    private static int? GetDword(string subKey, string name)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(subKey);
            return key?.GetValue(name) is int i ? i : (int?)null;
        }
        catch { return null; }
    }

    // DNS IPv4 configurado manualmente na interface ("" quando vem do DHCP), no formato "a,b".
    private static string ReadStaticDns(string interfaceId)
    {
        string? raw = GetString($@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{interfaceId}", "NameServer");
        if (string.IsNullOrWhiteSpace(raw)) return "";
        return string.Join(",", raw.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string? GetString(string subKey, string name)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(subKey);
            return key?.GetValue(name) as string;
        }
        catch { return null; }
    }

    // Registra uma alteração no log. valueKind funciona como "tag" que diz à reversão como desfazer.
    private void Record(string tag, string keyPath, string valueName, string? oldValue, string? newValue,
        string titleKey, string? realKind = null, string hive = "")
    {
        // O escopo é a ferramenta que o usuário clicou (um perfil grava RSS, Auto-Tuning etc. com os
        // títulos deles, mas tudo fica marcado como do perfil, para o "Reverter" do perfil desfazer tudo).
        string scope = _scope.Value ?? titleKey;
        _log.Record(new OptimizationChange
        {
            OptimizationId = ScopePrefix + scope,
            OptimizationTitle = Localization.Loc.S(titleKey),
            Kind = ChangeKind.Registry,
            Hive = hive,
            KeyPath = keyPath,
            ValueName = valueName,
            ValueKind = realKind ?? tag, // registry usa "DWord"/"String"; netsh/powercfg usam a tag
            OldValue = oldValue,
            NewValue = newValue,
        });
    }

    // Zera um índice do plano de energia na tomada E na bateria. Antes só o índice de tomada (AC)
    // era gravado, e num notebook na bateria o ajuste simplesmente não valia. O valor anterior é
    // guardado como "ac|dc" para o desfazer restaurar cada um.
    private async Task DisablePowerIndexAsync(string sub, string setting, string valueName, string titleKey)
    {
        int? oldAc = await ReadPowerIndexAsync(sub, setting);
        int? oldDc = await ReadPowerIndexAsync(sub, setting, dc: true);
        await RunAsync("powercfg", $"/setacvalueindex SCHEME_CURRENT {sub} {setting} 0");
        await RunAsync("powercfg", $"/setdcvalueindex SCHEME_CURRENT {sub} {setting} 0");
        await RunAsync("powercfg", "/setactive SCHEME_CURRENT");
        if ((oldAc ?? 0) != 0 || (oldDc ?? 0) != 0)
            Record("powerindex", $"{sub} {setting}", valueName, $"{oldAc ?? 0}|{oldDc ?? 0}", "0|0", titleKey);
    }

    private async Task<int?> ReadPowerIndexAsync(string sub, string setting, bool dc = false)
    {
        var r = await RunAsync("powercfg", $"/query SCHEME_CURRENT {sub} {setting}");
        // Linha do índice de CA (tomada): "AC Power Setting Index" (EN) ou "...Correntes
        // Alternadas..." (PT); de CC (bateria): "DC Power Setting Index" ou "...Contínuas...".
        foreach (var line in r.output.Split('\n'))
        {
            bool match = dc
                ? line.Contains("DC Power Setting Index", StringComparison.OrdinalIgnoreCase)
                  || line.Contains("Contínuas", StringComparison.OrdinalIgnoreCase)
                  || line.Contains("Continuas", StringComparison.OrdinalIgnoreCase)
                : line.Contains("AC Power Setting Index", StringComparison.OrdinalIgnoreCase)
                  || line.Contains("Alternadas", StringComparison.OrdinalIgnoreCase);
            if (!match) continue;
            var m = Regex.Match(line, @"0x([0-9a-fA-F]+)");
            if (m.Success && int.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.HexNumber, null, out int v))
                return v;
        }
        return null;
    }

    private async Task<(string rss, string autotune)> ReadTcpAsync()
    {
        string o = (await RunAsync("netsh", "int tcp show global")).output;
        string rss = ValueOf(o, "Receive-Side Scaling State", "Dimensionamento") ?? "—";
        string at = ValueOf(o, "Receive Window Auto-Tuning Level", "Ajuste Automático", "Ajuste Automatico") ?? "—";
        return (rss.Trim(), at.Trim());
    }

    private static string ParseAutoTuning(string raw)
    {
        // Normaliza o nível para o valor aceito pelo netsh (normal/disabled/highlyrestricted/...).
        string lower = raw.ToLowerInvariant();
        foreach (var level in new[] { "disabled", "highlyrestricted", "restricted", "normal", "experimental" })
            if (lower.Contains(level)) return level;
        return "—";
    }

    private static string? ValueOf(string output, params string[] labels)
    {
        foreach (var line in output.Split('\n'))
        {
            int c = line.IndexOf(':');
            if (c <= 0) continue;
            string label = line[..c].Trim();
            if (labels.Any(l => label.Contains(l, StringComparison.OrdinalIgnoreCase)))
                return line[(c + 1)..].Trim();
        }
        return null;
    }

    private static async Task<(int code, string output)> RunAsync(string exe, string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                // A saída vem na página de código do console (CP850 em português), não em UTF-8.
                StandardOutputEncoding = ConsoleEncoding.Oem,
                StandardErrorEncoding = ConsoleEncoding.Oem,
            };
            using var proc = Process.Start(psi)!;
            // As duas saídas juntas: ler stdout até o fim antes do stderr travava o processo
            // quando ele enchia o buffer de erro (ex.: netsh com muitas mensagens).
            var stdout = proc.StandardOutput.ReadToEndAsync();
            var stderr = proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();
            return (proc.ExitCode, await stdout + await stderr);
        }
        catch (Exception ex) { return (1, ex.Message); }
    }

    /// <summary>Roda um script PowerShell passado em Base64 (sem problemas de aspas) com saída em
    /// UTF-8 — nomes de propriedades do driver em português chegam intactos.</summary>
    private static async Task<(int code, string output)> RunPowerShellAsync(string script)
    {
        string full = "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; $ProgressPreference = 'SilentlyContinue'\n" + script;
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(full));
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            using var proc = Process.Start(psi)!;
            var stdout = proc.StandardOutput.ReadToEndAsync();
            var stderr = proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();
            return (proc.ExitCode, await stdout + await stderr);
        }
        catch (Exception ex) { return (1, ex.Message); }
    }

    /// <summary>Literal PowerShell entre aspas simples.</summary>
    private static string Ps(string value) => "'" + value.Replace("'", "''") + "'";
}
