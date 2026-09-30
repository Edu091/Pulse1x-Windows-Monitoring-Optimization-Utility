using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Pulse1x.App.Services;

/// <summary>Provedor de DNS escolhível na categoria Latência.</summary>
public enum DnsProvider { Automatic, Google, Cloudflare }

/// <summary>Resultado de uma ação de otimização: sucesso + chave de mensagem + detalhe opcional.</summary>
public record OpResult(bool Success, string MessageKey, string? Detail = null);

/// <summary>
/// Aplica e desfaz otimizações de rede da categoria Latência. Toda alteração persistente é
/// registrada em um <see cref="OptimizationChangeLog"/> próprio (network-changes.json) para que
/// "Desfazer Todas as Alterações" restaure o estado original. As ações pontuais (flush DNS, reset
/// Winsock/TCP, reiniciar adaptador/serviços) não mudam configuração persistente — apenas executam
/// um comando do Windows. Nenhum tweak perigoso/irreversível é usado.
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

    /// <summary>Restaura o catálogo Winsock ao padrão (corrige pilha de sockets corrompida). Requer reinício.</summary>
    public async Task<OpResult> ResetWinsockAsync()
    {
        var r = await RunAsync("netsh", "winsock reset");
        return new OpResult(r.code == 0, r.code == 0 ? "Lat_DoneResetWinsock" : "Lat_OpFailed", r.output.Trim());
    }

    /// <summary>Reseta a pilha TCP/IP ao padrão. Requer reinício para efeito completo.</summary>
    public async Task<OpResult> ResetTcpIpAsync()
    {
        var r = await RunAsync("netsh", "int ip reset");
        return new OpResult(r.code == 0, r.code == 0 ? "Lat_DoneResetTcp" : "Lat_OpFailed", r.output.Trim());
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
            return new OpResult(rr.code == 0, rr.code == 0 ? "Lat_DoneDnsAuto" : "Lat_OpFailed", rr.output.Trim());
        }

        var (primary, secondary) = provider == DnsProvider.Google
            ? ("8.8.8.8", "8.8.4.4")
            : ("1.1.1.1", "1.0.0.1");

        var r1 = await RunAsync("netsh", $"interface ip set dns name=\"{name}\" static {primary} primary");
        await RunAsync("netsh", $"interface ip add dns name=\"{name}\" {secondary} index=2");
        if (r1.code != 0) return new OpResult(false, "Lat_OpFailed", r1.output.Trim());

        if (!alreadyRecorded)
            Record("netsh-dns", name, "Dns", string.IsNullOrEmpty(oldDns) ? "dhcp" : oldDns, $"{primary},{secondary}", "Lat_ToolDns");
        return new OpResult(true, provider == DnsProvider.Google ? "Lat_DoneDnsGoogle" : "Lat_DoneDnsCloudflare");
    }

    /// <summary>Perfil Competitivo: foca latência mínima — desativa o algoritmo de Nagle no adaptador,
    /// remove o limite de throttling de rede e maximiza a responsividade do agendador multimídia.</summary>
    public async Task<OpResult> ApplyCompetitiveProfileAsync()
    {
        // Responsividade do sistema e throttling de rede (afetam latência em jogos/streaming).
        SetDword(SystemProfile, "SystemResponsiveness", 0, "Lat_ToolCompetitive");
        SetDword(SystemProfile, "NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF), "Lat_ToolCompetitive");
        // DWORD, como no perfil original do Windows (texto aqui era ignorado pelo MMCSS e, ao
        // desfazer, apagava os valores de fábrica).
        SetDword(GamesProfile, "GPU Priority", 8, "Lat_ToolCompetitive");
        SetDword(GamesProfile, "Priority", 6, "Lat_ToolCompetitive");
        SetString(GamesProfile, "Scheduling Category", "High", "Lat_ToolCompetitive");

        // Desativa o algoritmo de Nagle na interface ativa (junta pacotes pequenos e adiciona atraso).
        var nic = NetworkLatencyService.ActiveAdapter();
        if (nic is not null)
        {
            string ifacePath = $@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{nic.Id}";
            SetDword(ifacePath, "TcpAckFrequency", 1, "Lat_ToolCompetitive");
            SetDword(ifacePath, "TCPNoDelay", 1, "Lat_ToolCompetitive");
        }

        await EnableRssAsync();
        await SetAutoTuningNormalAsync();
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
        return new OpResult(true, "Lat_DoneStability");
    }

    /// <summary>Aplica o conjunto seguro recomendado de uma vez ("Aplicar Todas as Otimizações").</summary>
    public async Task<OpResult> ApplyAllAsync()
    {
        await FlushDnsAsync();
        await SetAutoTuningNormalAsync();
        await EnableRssAsync();
        await DisableWifiPowerSavingAsync();
        await DisableSelectiveSuspendAsync();
        return new OpResult(true, "Lat_DoneApplyAll");
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
    public IReadOnlyList<OptimizationChange> Changes => _log.GetAllActive();

    /// <summary>"Desfazer Todas as Alterações": reverte cada mudança registrada e devolve os padrões
    /// de rede do Windows (Winsock, TCP/IP, DNS automático, Auto-Tuning normal).</summary>
    public async Task<OpResult> RestoreAllAsync()
    {
        foreach (var change in _log.GetAllActive())
            await RevertChangeAsync(change);

        // Só o Auto-Tuning volta ao padrão como rede de segurança — ele é o mesmo em qualquer PC.
        // DNS em DHCP, "winsock reset" e "int ip reset" eram executados SEMPRE aqui e apagavam
        // configurações do próprio usuário (DNS manual, IP fixo) que o Pulse1x nunca alterou;
        // essas continuam disponíveis como ferramentas separadas.
        await RunAsync("netsh", "int tcp set global autotuninglevel=normal");
        return new OpResult(true, "Lat_DoneRestoreAll");
    }

    /// <summary>Desfaz uma única alteração registrada, conforme o tipo (tag em <c>ValueKind</c>).</summary>
    public async Task RevertChangeAsync(OptimizationChange change)
    {
        switch (change.ValueKind)
        {
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
        _log.Record(new OptimizationChange
        {
            OptimizationId = "network",
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
            string output = await proc.StandardOutput.ReadToEndAsync();
            output += await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();
            return (proc.ExitCode, output);
        }
        catch (Exception ex) { return (1, ex.Message); }
    }
}
