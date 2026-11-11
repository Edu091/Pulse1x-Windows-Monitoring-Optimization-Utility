using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace Pulse1x.App.Services;

// ===================== Modelos de leitura (somente leitura, imutáveis) =====================

/// <summary>Faixa de qualidade de uma métrica — vira selo colorido na interface (🟢🟡🟠🔴).</summary>
public enum QualityLevel { Excellent, Good, Fair, Poor }

/// <summary>Informações do enlace Wi-Fi atuais, extraídas de <c>netsh wlan show interfaces</c>.</summary>
public record WifiInfo(
    bool Connected,
    string Ssid,
    int SignalPercent,
    int SignalDbm,
    int Channel,
    string Band,
    double LinkMbps,
    string RadioType,
    string Bssid = "",
    string InterfaceName = "",
    string InterfaceGuid = "",
    bool LocationBlocked = false);

/// <summary>Dados básicos do adaptador ativo (IP local, DNS, nome) — de <see cref="NetworkInterface"/>.</summary>
public record NetworkBasics(string LocalIp, string Dns, string AdapterName, bool IsWifi, string Gateway, long LinkBps = 0);

/// <summary>Uma rede Wi-Fi vizinha (um BSSID) vista na última varredura.</summary>
public record WifiNetwork(string Ssid, string Bssid, int SignalPercent, int Channel, string Band, string Radio,
    int? Stations, int? UtilizationPercent);

/// <summary>Resultado do teste de bufferbloat: latência ociosa vs. sob carga de download/upload.</summary>
public record BufferbloatResult(double IdleMs, double DownloadLoadedMs, double UploadLoadedMs,
    double DownloadMbps, double UploadMbps, string Grade, QualityLevel Level, bool Success)
{
    public double WorstIncreaseMs => Math.Max(0, Math.Max(DownloadLoadedMs, UploadLoadedMs) - IdleMs);
}

/// <summary>Tempo de resposta de um servidor DNS (mediana de várias consultas).</summary>
public record DnsBenchmarkResult(string Name, string Primary, string Secondary, double MedianMs, int Answered, int Sent, bool IsCurrent = false)
{
    public bool Success => Answered > 0;
}

/// <summary>Resultado da busca de MTU: maior pacote que passa sem fragmentar até a internet.</summary>
public record MtuResult(int BestMtu, int CurrentMtu, bool Success);

/// <summary>Resultado agregado de uma rajada de pings: latência média, jitter e perda de pacotes.</summary>
public record PingStats(double AvgMs, double JitterMs, double LossPercent, bool Success);

/// <summary>Resultado de um teste de velocidade (download/upload em Mbps).</summary>
public record SpeedResult(double DownloadMbps, double UploadMbps);

/// <summary>Latência de um servidor de teste conhecido (Google, Cloudflare, Riot, etc.).</summary>
public record TestServerResult(string Name, string Host, double AvgMs, double JitterMs, double LossPercent, bool Reachable);

/// <summary>Estado das otimizações de TCP relevantes para jogos (RSS e Auto-Tuning).</summary>
public record TcpGlobalSettings(string RssState, string AutoTuningLevel);

/// <summary>Análise de congestionamento de canais Wi-Fi nas redes vizinhas.</summary>
public record ChannelAdvice(int CurrentChannel, int SuggestedChannel24, int SuggestedChannel5, int NeighborsOnCurrent, bool Congested);

/// <summary>Informações do driver do adaptador Wi-Fi (para detectar driver antigo).</summary>
public record WifiDriverInfo(string Name, string Version, DateTime? Date, bool LooksOutdated);

/// <summary>Nota geral da conexão (0–100) com uma chave de classificação localizável.</summary>
public record ConnectionScore(int Score, QualityLevel Level);

/// <summary>Uma observação do diagnóstico inteligente, em linguagem simples + chave de sugestão.</summary>
public record DiagnosticFinding(string MessageKey, QualityLevel Severity);

/// <summary>Relatório completo do botão "Analisar Minha Conexão".</summary>
public record DiagnosticReport(
    PingStats Ping,
    SpeedResult Speed,
    WifiInfo Wifi,
    NetworkBasics Basics,
    TcpGlobalSettings Tcp,
    ChannelAdvice? Channels,
    WifiDriverInfo? Driver,
    ConnectionScore Score,
    IReadOnlyList<string> SummaryKeys,
    PingStats? RouterPing = null);

/// <summary>Resultado da verificação simplificada de latência de drivers (estilo LatencyMon).</summary>
public record DriverLatencyResult(double MaxDeviationMs, double AvgDeviationMs, QualityLevel Level);

/// <summary>
/// Central de leitura e diagnóstico de rede/Wi-Fi (somente leitura — não altera o sistema).
/// Coleta ping/jitter/perda via <see cref="Ping"/>, velocidade do link e sinal via
/// <c>netsh wlan</c>, IP/DNS via <see cref="NetworkInterface"/>, e teste de velocidade real via
/// HTTP. Todas as métricas alimentam o painel em tempo real, os servidores de teste e o
/// diagnóstico inteligente da categoria Latência. As alterações ficam em <see cref="NetworkOptimizationService"/>.
/// </summary>
public class NetworkLatencyService
{
    // HttpClient compartilhado (boa prática: um por aplicação) usado no teste de velocidade.
    // Os testes limitam a própria duração; o timeout é só uma rede de segurança.
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    /// <summary>Alvo de "ping da internet": o anycast da Cloudflare responde ICMP e fica perto em
    /// praticamente qualquer provedor, então mede a rota real até fora da sua casa.</summary>
    public const string InternetPingTarget = "1.1.1.1";

    // Hosts dos servidores de teste de latência pedidos (jogos e nuvem).
    public static readonly (string Name, string Host)[] TestServers =
    {
        ("Google", "8.8.8.8"),
        ("Cloudflare", "1.1.1.1"),
        ("Riot Games", "riotgames.com"),
        ("Valve (Steam)", "steamcommunity.com"),
        ("Microsoft", "microsoft.com"),
        ("AWS", "amazon.com"),
    };

    // ===================== Ping / Jitter / Perda =====================

    /// <summary>Envia <paramref name="count"/> pings e calcula latência média, jitter (variação média
    /// entre amostras consecutivas) e perda de pacotes. É a base do indicador em tempo real.</summary>
    public async Task<PingStats> PingAsync(string host, int count = 10, int timeoutMs = 1000, CancellationToken ct = default)
    {
        var times = new List<long>();
        int lost = 0;
        using var ping = new Ping();

        for (int i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var reply = await ping.SendPingAsync(host, timeoutMs);
                if (reply.Status == IPStatus.Success) times.Add(reply.RoundtripTime);
                else lost++;
            }
            catch { lost++; }
        }

        if (times.Count == 0) return new PingStats(0, 0, 100, false);

        double avg = times.Average();
        // Jitter = média das diferenças absolutas entre pings consecutivos (padrão RFC 3550 simplificado).
        double jitter = 0;
        for (int i = 1; i < times.Count; i++) jitter += Math.Abs(times[i] - times[i - 1]);
        jitter = times.Count > 1 ? jitter / (times.Count - 1) : 0;

        double loss = (double)lost / count * 100;
        return new PingStats(Math.Round(avg, 1), Math.Round(jitter, 1), Math.Round(loss, 1), true);
    }

    /// <summary>Mede latência por conexão TCP (handshake) — funciona mesmo quando o host bloqueia
    /// ICMP (caso de microsoft.com, amazon.com e muitos servidores de jogo/CDN).</summary>
    public async Task<PingStats> TcpPingAsync(string host, int port = 443, int count = 5, int timeoutMs = 1500, CancellationToken ct = default)
    {
        var times = new List<double>();
        int lost = 0;

        // Resolve o nome UMA vez, fora do cronômetro. Antes, ConnectAsync(host) resolvia o DNS a
        // cada tentativa dentro da medição e a primeira amostra somava 20–100 ms de consulta.
        System.Net.IPAddress[] addresses;
        try { addresses = await System.Net.Dns.GetHostAddressesAsync(host, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return new PingStats(0, 0, 100, false); }
        if (addresses.Length == 0) return new PingStats(0, 0, 100, false);

        for (int i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var client = new TcpClient(addresses[0].AddressFamily);
                var sw = Stopwatch.StartNew();
                var connect = client.ConnectAsync(addresses[0], port);
                var finished = await Task.WhenAny(connect, Task.Delay(timeoutMs));
                sw.Stop();
                if (finished == connect && client.Connected) times.Add(sw.Elapsed.TotalMilliseconds);
                else
                {
                    lost++;
                    // Em timeout, o client é descartado e a conexão pendente falha depois: observamos
                    // a exceção para não vazar uma "unobserved task exception".
                    _ = connect.ContinueWith(static t => { _ = t.Exception; }, TaskScheduler.Default);
                }
            }
            catch { lost++; }
        }

        if (times.Count == 0) return new PingStats(0, 0, 100, false);

        double avg = times.Average();
        double jitter = 0;
        for (int i = 1; i < times.Count; i++) jitter += Math.Abs(times[i] - times[i - 1]);
        jitter = times.Count > 1 ? jitter / (times.Count - 1) : 0;
        return new PingStats(Math.Round(avg, 1), Math.Round(jitter, 1), Math.Round((double)lost / count * 100, 1), true);
    }

    /// <summary>Mede a latência de um servidor de teste: tenta ICMP e, se o host não responde
    /// (bloqueio comum), cai para a latência de conexão TCP na porta 443 — assim o resultado nunca
    /// fica "inalcançável" só porque o servidor ignora ping.</summary>
    public async Task<PingStats> MeasureTestServerAsync(string host, CancellationToken ct = default)
    {
        var icmp = await PingAsync(host, count: 6, timeoutMs: 1500, ct);
        if (icmp.Success && icmp.LossPercent < 100) return icmp;
        return await TcpPingAsync(host, 443, count: 5, timeoutMs: 1500, ct);
    }

    /// <summary>Um único ping rápido — usado no laço de tempo real do painel para não bloquear.</summary>
    public async Task<long?> QuickPingAsync(string host, int timeoutMs = 1000)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, timeoutMs);
            return reply.Status == IPStatus.Success ? reply.RoundtripTime : null;
        }
        catch { return null; }
    }

    // ===================== Wi-Fi (netsh) =====================

    /// <summary>Lê o estado do enlace Wi-Fi atual (SSID, sinal, canal, banda, taxa do link).
    /// Os rótulos do <c>netsh</c> variam por idioma do Windows, então casamos por fragmentos PT+EN.</summary>
    public async Task<WifiInfo> ReadWifiAsync()
    {
        string output = (await RunAsync("netsh", "wlan show interfaces")).output;
        return ParseWifiInterfaces(output);
    }

    /// <summary>Interpreta a saída de <c>netsh wlan show interfaces</c> (PT ou EN).</summary>
    internal static WifiInfo ParseWifiInterfaces(string output)
    {
        // Windows 11 24H2+: sem permissão de localização o netsh recusa os dados do Wi-Fi e só
        // imprime um aviso. Antes isso virava "—" em tudo, sem explicar o porquê.
        bool locationBlocked = IsLocationBlocked(output);

        string ssid = ExtractValue(output, "SSID") ?? "";
        string bssid = ExtractValue(output, "AP BSSID", "BSSID") ?? "";
        string ifName = ExtractValue(output, "Nome", "Name") ?? "";
        string ifGuid = ExtractValue(output, "GUID") ?? "";
        // BSSID também contém "SSID"; se o SSID veio vazio mas há BSSID, seguimos mesmo assim.
        int signal = ParseFirstInt(ExtractValue(output, "Signal", "Sinal")) ?? 0;
        int channel = ParseFirstInt(ExtractValue(output, "Channel", "Canal")) ?? 0;
        string radio = ExtractValue(output, "Radio type", "Tipo de rádio", "Tipo de radio") ?? "";
        string band = ExtractValue(output, "Band", "Banda") ?? InferBand(channel);
        // Taxa de recepção é a velocidade real de download do link no momento.
        double rx = ParseFirstDouble(ExtractValue(output, "Receive rate", "Taxa de recepção", "Taxa de recepcao")) ?? 0;
        double tx = ParseFirstDouble(ExtractValue(output, "Transmit rate", "Taxa de transmissão", "Taxa de transmissao")) ?? 0;
        double link = Math.Max(rx, tx);

        bool connected = signal > 0 || !string.IsNullOrEmpty(ssid);
        // O netsh já traz o RSSI exato (em dBm) na maioria das versões; usamos ele quando existe.
        // Caso contrário, aproximamos do sinal %: 100% ≈ -50 dBm, 0% ≈ -100 dBm.
        int? rssi = ParseFirstInt(ExtractValue(output, "Rssi", "RSSI"));
        int dbm = connected ? (rssi ?? (signal / 2) - 100) : 0;

        return new WifiInfo(connected, ssid, signal, dbm, channel, band, link, radio, bssid, ifName, ifGuid, locationBlocked);
    }

    internal static bool IsLocationBlocked(string netshOutput) =>
        netshOutput.Contains("location permission", StringComparison.OrdinalIgnoreCase)
        || netshOutput.Contains("permissão de localização", StringComparison.OrdinalIgnoreCase)
        || netshOutput.Contains("permissao de localizacao", StringComparison.OrdinalIgnoreCase)
        || netshOutput.Contains("ms-settings:privacy-location", StringComparison.OrdinalIgnoreCase);

    private static string InferBand(int channel) => channel switch
    {
        >= 1 and <= 14 => "2,4 GHz",
        >= 32 and <= 196 => "5 GHz",
        > 196 => "6 GHz",
        _ => "—",
    };

    // ===================== IP / DNS / adaptador =====================

    /// <summary>Lê IP local, gateway, servidores DNS e o nome do adaptador ativo (com gateway IPv4).</summary>
    public NetworkBasics ReadBasics()
    {
        var nic = ActiveAdapter();
        if (nic is null) return new NetworkBasics("—", "—", "—", false, "—");

        var props = nic.GetIPProperties();
        var ipv4 = props.UnicastAddresses.FirstOrDefault(u => u.Address.AddressFamily == AddressFamily.InterNetwork);
        var gw = props.GatewayAddresses.FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork);
        var dns = props.DnsAddresses.Where(d => d.AddressFamily == AddressFamily.InterNetwork).Select(d => d.ToString());

        bool isWifi = nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
        string dnsText = dns.Any() ? string.Join(", ", dns) : "—";
        long speed = 0;
        try { speed = nic.Speed; } catch { }
        return new NetworkBasics(ipv4?.Address.ToString() ?? "—", dnsText, nic.Name, isWifi, gw?.Address.ToString() ?? "—", speed);
    }

    [System.Runtime.InteropServices.DllImport("iphlpapi.dll")]
    private static extern int GetBestInterface(uint destAddr, out uint bestIfIndex);

    /// <summary>O adaptador que o Windows realmente usa para chegar à internet. Antes era "o
    /// primeiro com gateway", e com VPN, Hyper-V ou um cabo desconectado-mas-configurado a ordem
    /// do sistema podia apontar a placa errada (IP, DNS e otimizações no adaptador errado).</summary>
    public static NetworkInterface? ActiveAdapter()
    {
        var candidates = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                        && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                        && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
            .Where(n => n.GetIPProperties().GatewayAddresses
                .Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork))
            .ToList();

        try
        {
            // 1.1.1.1 em ordem de rede (big-endian) lido como uint little-endian.
            uint dest = BitConverter.ToUInt32(new byte[] { 1, 1, 1, 1 }, 0);
            if (GetBestInterface(dest, out uint index) == 0)
            {
                var best = candidates.FirstOrDefault(n =>
                {
                    try { return n.GetIPProperties().GetIPv4Properties()?.Index == index; }
                    catch { return false; }
                });
                if (best is not null) return best;
            }
        }
        catch { /* sem iphlpapi: cai na heurística abaixo */ }

        // Sem rota conhecida: prefere placas físicas (cabo, depois Wi-Fi) às virtuais.
        return candidates
            .OrderBy(n => IsLikelyVirtual(n) ? 1 : 0)
            .ThenBy(n => n.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? 0 : 1)
            .FirstOrDefault();
    }

    /// <summary>O adaptador Wi-Fi físico (mesmo que a conexão principal seja por cabo).</summary>
    public static NetworkInterface? WifiAdapter()
    {
        var active = ActiveAdapter();
        if (active is { NetworkInterfaceType: NetworkInterfaceType.Wireless80211 }) return active;
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 && !IsLikelyVirtual(n))
            .OrderBy(n => n.OperationalStatus == OperationalStatus.Up ? 0 : 1)
            .FirstOrDefault();
    }

    private static bool IsLikelyVirtual(NetworkInterface n)
    {
        string d = n.Description;
        return d.Contains("Virtual", StringComparison.OrdinalIgnoreCase)
               || d.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase)
               || d.Contains("VPN", StringComparison.OrdinalIgnoreCase)
               || d.Contains("TAP-", StringComparison.OrdinalIgnoreCase)
               || d.Contains("WireGuard", StringComparison.OrdinalIgnoreCase)
               || d.Contains("Wi-Fi Direct", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>O alvo de ping preferido: o gateway (mede a latência até o roteador) ou 1.1.1.1.</summary>
    public string PreferredPingTarget()
    {
        var b = ReadBasics();
        return b.Gateway != "—" ? b.Gateway : "1.1.1.1";
    }

    // ===================== Teste de velocidade (HTTP real) =====================

    /// <summary>Mede download e upload reais na Cloudflare, cada um por alguns segundos, e converte
    /// para Mbps. Consome banda de internet de propósito — é o que o usuário pede ao tocar em "Analisar".</summary>
    public async Task<SpeedResult> RunSpeedTestAsync(CancellationToken ct = default)
    {
        double down = await MeasureDownloadAsync(TimeSpan.FromSeconds(8), ct);
        double up = await MeasureUploadAsync(TimeSpan.FromSeconds(6), ct);
        return new SpeedResult(Math.Round(down, 1), Math.Round(up, 1));
    }

    // Antes: 15 MB fixos com timeout de 30 s. Abaixo de ~4 Mbps o download nunca terminava a tempo
    // e o diagnóstico mostrava "0 Mbps". Agora lê pelo tempo dado e mede o que chegou.
    private static async Task<double> MeasureDownloadAsync(TimeSpan duration, CancellationToken ct)
    {
        using var timeLimit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeLimit.CancelAfter(duration + TimeSpan.FromSeconds(5)); // cabeçalhos lentos não prendem o teste
        long total = 0;
        var buffer = new byte[81920];
        Stopwatch? sw = null;
        try
        {
            // A Cloudflare recusa (403) pedidos acima de ~50 MB; em vez de um arquivo gigante,
            // encadeia blocos de 25 MB até o tempo acabar — em conexões de 1 Gbps um bloco só
            // terminaria em 0,2 s e o teste de bufferbloat ficaria sem carga.
            while (sw is null || sw.Elapsed < duration)
            {
                using var resp = await Http.GetAsync("https://speed.cloudflare.com/__down?bytes=25000000",
                    HttpCompletionOption.ResponseHeadersRead, timeLimit.Token);
                resp.EnsureSuccessStatusCode();
                await using var stream = await resp.Content.ReadAsStreamAsync(timeLimit.Token);
                sw ??= Stopwatch.StartNew(); // conta só a transferência, não o primeiro handshake TLS
                while (sw.Elapsed < duration)
                {
                    int read = await stream.ReadAsync(buffer, timeLimit.Token);
                    if (read == 0) break;
                    total += read;
                }
            }
        }
        // Sair da página cancela o teste: o cancelamento sobe, não vira "0 Mbps".
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { /* limite de tempo ou queda: mede o que chegou */ }
        double seconds = Math.Max(sw?.Elapsed.TotalSeconds ?? 0, 0.001);
        return total * 8 / seconds / 1_000_000; // bits -> Mbps
    }

    private static async Task<double> MeasureUploadAsync(TimeSpan duration, CancellationToken ct)
    {
        var content = new TimedUploadContent(duration, ct);
        try
        {
            using var timeLimit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeLimit.CancelAfter(duration + TimeSpan.FromSeconds(10));
            using var resp = await Http.PostAsync("https://speed.cloudflare.com/__up", content, timeLimit.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { /* conta os bytes que saíram mesmo assim */ }
        finally { content.Dispose(); }
        double seconds = Math.Max(content.Elapsed.TotalSeconds, 0.001);
        return content.BytesSent * 8 / seconds / 1_000_000;
    }

    /// <summary>Corpo de upload que gera bytes até o tempo acabar (chunked), em vez de um tamanho
    /// fixo que estoura o timeout em conexões lentas.</summary>
    private sealed class TimedUploadContent : HttpContent
    {
        private readonly TimeSpan _duration;
        private readonly CancellationToken _ct;
        private readonly Stopwatch _sw = new();
        public long BytesSent { get; private set; }
        public TimeSpan Elapsed => _sw.Elapsed;

        public TimedUploadContent(TimeSpan duration, CancellationToken ct)
        {
            _duration = duration;
            _ct = ct;
            Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        }

        protected override async Task SerializeToStreamAsync(System.IO.Stream stream, System.Net.TransportContext? context)
        {
            var chunk = new byte[65536];
            Random.Shared.NextBytes(chunk); // dados incompressíveis
            _sw.Start();
            while (_sw.Elapsed < _duration && !_ct.IsCancellationRequested)
            {
                await stream.WriteAsync(chunk, _ct);
                BytesSent += chunk.Length;
            }
            _sw.Stop();
        }

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }
    }

    // ===================== Bufferbloat (latência sob carga) =====================

    /// <summary>
    /// Mede quanto o ping sobe enquanto a conexão está cheia — o "lag quando alguém assiste
    /// vídeo/baixa algo" que o ping ocioso não mostra. Pinga 1.1.1.1 parado, depois durante um
    /// download e durante um upload, e dá uma nota de A+ a F pelo maior aumento (mesma escala
    /// usada pelos testes de bufferbloat conhecidos).
    /// </summary>
    public async Task<BufferbloatResult> RunBufferbloatTestAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        progress?.Report("idle");
        var idle = await PingSeriesAsync(InternetPingTarget, TimeSpan.FromSeconds(3), ct);
        if (idle.Count == 0)
            return new BufferbloatResult(0, 0, 0, 0, 0, "—", QualityLevel.Poor, false);

        progress?.Report("download");
        var (downPings, downMbps) = await PingDuringAsync(t => MeasureDownloadAsync(TimeSpan.FromSeconds(7), t), ct);
        progress?.Report("upload");
        var (upPings, upMbps) = await PingDuringAsync(t => MeasureUploadAsync(TimeSpan.FromSeconds(6), t), ct);

        double idleMs = Median(idle);
        // Sem resposta nenhuma sob carga = fila tão cheia que o ping estourou (pior caso).
        double downMs = downPings.Count > 0 ? Percentile(downPings, 0.9) : 1000;
        double upMs = upPings.Count > 0 ? Percentile(upPings, 0.9) : 1000;
        var (grade, level) = BufferbloatGrade(Math.Max(downMs, upMs) - idleMs);
        return new BufferbloatResult(Math.Round(idleMs, 1), Math.Round(downMs, 1), Math.Round(upMs, 1),
            Math.Round(downMbps, 1), Math.Round(upMbps, 1), grade, level, true);
    }

    /// <summary>Nota pelo aumento de latência sob carga (ms).</summary>
    internal static (string Grade, QualityLevel Level) BufferbloatGrade(double increaseMs) => increaseMs switch
    {
        < 5 => ("A+", QualityLevel.Excellent),
        < 30 => ("A", QualityLevel.Excellent),
        < 60 => ("B", QualityLevel.Good),
        < 200 => ("C", QualityLevel.Fair),
        < 400 => ("D", QualityLevel.Poor),
        _ => ("F", QualityLevel.Poor),
    };

    private async Task<(List<double> pings, double mbps)> PingDuringAsync(Func<CancellationToken, Task<double>> load, CancellationToken ct)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var loadTask = load(ct);
        // Espera a carga "encher a fila" antes de medir.
        try { await Task.Delay(1000, ct); }
        catch (OperationCanceledException)
        {
            _ = loadTask.ContinueWith(static t => { _ = t.Exception; }, TaskScheduler.Default);
            throw;
        }
        var pingTask = PingSeriesAsync(InternetPingTarget, TimeSpan.FromSeconds(30), stop.Token);
        double mbps = await loadTask;
        stop.Cancel();
        var pings = await pingTask;
        ct.ThrowIfCancellationRequested();
        return (pings, mbps);
    }

    // Pinga a cada ~200 ms até o tempo acabar ou o token ser cancelado (cancelamento encerra a série, sem exceção).
    private static async Task<List<double>> PingSeriesAsync(string host, TimeSpan duration, CancellationToken ct)
    {
        var times = new List<double>();
        using var ping = new Ping();
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < duration && !ct.IsCancellationRequested)
        {
            try
            {
                var reply = await ping.SendPingAsync(host, 1500);
                if (reply.Status == IPStatus.Success) times.Add(reply.RoundtripTime);
            }
            catch { }
            try { await Task.Delay(200, ct); } catch (OperationCanceledException) { break; }
        }
        return times;
    }

    internal static double Median(IReadOnlyList<double> values) => Percentile(values, 0.5);

    internal static double Percentile(IReadOnlyList<double> values, double p)
    {
        if (values.Count == 0) return 0;
        var sorted = values.OrderBy(v => v).ToArray();
        double rank = p * (sorted.Length - 1);
        int lo = (int)Math.Floor(rank), hi = (int)Math.Ceiling(rank);
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (rank - lo);
    }

    // ===================== Benchmark de DNS =====================

    /// <summary>Servidores DNS públicos comparados no benchmark (e oferecidos para troca).</summary>
    public static readonly (string Name, string Primary, string Secondary)[] PublicDnsServers =
    {
        ("Cloudflare", "1.1.1.1", "1.0.0.1"),
        ("Google", "8.8.8.8", "8.8.4.4"),
        ("Quad9", "9.9.9.9", "149.112.112.112"),
        ("OpenDNS", "208.67.222.222", "208.67.220.220"),
        ("AdGuard", "94.140.14.14", "94.140.15.15"),
    };

    private static readonly string[] DnsTestDomains =
        { "google.com", "youtube.com", "cloudflare.com", "microsoft.com", "amazon.com", "steampowered.com", "riotgames.com", "discord.com" };

    /// <summary>Mede o tempo de resposta (mediana) de cada servidor DNS com consultas UDP diretas,
    /// sem passar pelo cache do Windows. <paramref name="currentDns"/> entra como "Atual".</summary>
    public async Task<List<DnsBenchmarkResult>> BenchmarkDnsAsync(IEnumerable<string> currentDns, string currentLabel, CancellationToken ct = default)
    {
        var targets = new List<(string Name, string Primary, string Secondary, bool IsCurrent)>();
        var current = currentDns.Where(ip => System.Net.IPAddress.TryParse(ip, out var a)
                                             && a.AddressFamily == AddressFamily.InterNetwork).Distinct().ToList();
        string firstCurrent = current.FirstOrDefault() ?? "";
        // DNS atual já é um dos públicos: marca aquele como "atual" em vez de sumir da comparação.
        bool currentIsPublic = PublicDnsServers.Any(s => s.Primary == firstCurrent || s.Secondary == firstCurrent);
        if (current.Count > 0 && !currentIsPublic)
            targets.Add((currentLabel, current[0], current.Count > 1 ? current[1] : "", true));
        targets.AddRange(PublicDnsServers.Select(s => (s.Name, s.Primary, s.Secondary, s.Primary == firstCurrent || s.Secondary == firstCurrent)));

        var tasks = targets.Select(async t =>
        {
            var times = new List<double>();
            int sent = 0;
            foreach (var domain in DnsTestDomains)
            {
                ct.ThrowIfCancellationRequested();
                sent++;
                double? ms = await QueryDnsAsync(t.Primary, domain, 1500, ct);
                if (ms is { } v) times.Add(v);
            }
            return new DnsBenchmarkResult(t.Name, t.Primary, t.Secondary,
                times.Count > 0 ? Math.Round(Median(times), 1) : 0, times.Count, sent, t.IsCurrent);
        });
        var results = (await Task.WhenAll(tasks)).ToList();
        return results.OrderBy(r => r.Success ? 0 : 1).ThenBy(r => r.MedianMs).ToList();
    }

    private static async Task<double?> QueryDnsAsync(string server, string domain, int timeoutMs, CancellationToken ct)
    {
        try
        {
            using var udp = new UdpClient(AddressFamily.InterNetwork);
            udp.Connect(System.Net.IPAddress.Parse(server), 53);
            ushort id = (ushort)Random.Shared.Next(1, 65535);
            byte[] query = BuildDnsQuery(id, domain);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(timeoutMs);
            var sw = Stopwatch.StartNew();
            await udp.SendAsync(query, timeout.Token);
            while (true)
            {
                var resp = await udp.ReceiveAsync(timeout.Token);
                if (IsDnsResponseFor(resp.Buffer, id)) return sw.Elapsed.TotalMilliseconds;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    /// <summary>Monta uma consulta DNS tipo A (RFC 1035) com recursão pedida.</summary>
    internal static byte[] BuildDnsQuery(ushort id, string domain)
    {
        var bytes = new List<byte>
        {
            (byte)(id >> 8), (byte)id,
            0x01, 0x00, // RD
            0x00, 0x01, // QDCOUNT
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        };
        foreach (var label in domain.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var l = Encoding.ASCII.GetBytes(label);
            bytes.Add((byte)l.Length);
            bytes.AddRange(l);
        }
        bytes.Add(0);
        bytes.AddRange(new byte[] { 0x00, 0x01, 0x00, 0x01 }); // QTYPE A, QCLASS IN
        return bytes.ToArray();
    }

    internal static bool IsDnsResponseFor(byte[] buffer, ushort id) =>
        buffer.Length >= 12 && buffer[0] == (byte)(id >> 8) && buffer[1] == (byte)id && (buffer[2] & 0x80) != 0;

    // ===================== MTU =====================

    /// <summary>Descobre o maior pacote que chega a 1.1.1.1 sem fragmentar (ping com "não
    /// fragmentar" + busca binária). MTU = carga ICMP + 28 bytes de cabeçalho IP/ICMP.</summary>
    public async Task<MtuResult> FindMtuAsync(int currentMtu, CancellationToken ct = default)
    {
        const int min = 1172, max = 1472; // MTU 1200..1500
        if (!await FitsAsync(min, ct)) return new MtuResult(0, currentMtu, false);
        int lo = min, hi = max;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (await FitsAsync(mid, ct)) lo = mid; else hi = mid - 1;
        }
        return new MtuResult(lo + 28, currentMtu, true);
    }

    private static async Task<bool> FitsAsync(int payload, CancellationToken ct)
    {
        using var ping = new Ping();
        var options = new PingOptions(64, dontFragment: true);
        var buffer = new byte[payload];
        // Duas tentativas: uma perda comum não pode ser confundida com "pacote grande demais".
        for (int i = 0; i < 2; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var reply = await ping.SendPingAsync(InternetPingTarget, 1200, buffer, options);
                if (reply.Status == IPStatus.Success) return true;
                if (reply.Status == IPStatus.PacketTooBig) return false;
            }
            catch { }
        }
        return false;
    }

    // ===================== Servidores de teste =====================

    /// <summary>Mede a latência para cada servidor de teste conhecido (jogos e nuvem).</summary>
    public async Task<List<TestServerResult>> TestServersAsync()
    {
        var results = new List<TestServerResult>();
        foreach (var (name, host) in TestServers)
        {
            var stats = await MeasureTestServerAsync(host);
            results.Add(new TestServerResult(name, host, stats.AvgMs, stats.JitterMs, stats.LossPercent, stats.Success));
        }
        return results;
    }

    // ===================== TCP global (RSS / Auto-Tuning) =====================

    /// <summary>Lê o estado de Receive Side Scaling e Receive Window Auto-Tuning (de <c>netsh int tcp show global</c>).</summary>
    public async Task<TcpGlobalSettings> ReadTcpSettingsAsync()
    {
        string output = (await RunAsync("netsh", "int tcp show global")).output;
        string rss = ExtractValue(output, "Receive-Side Scaling State", "Estado de Dimensionamento") ?? "—";
        string autotune = ExtractValue(output, "Receive Window Auto-Tuning Level", "Nível de Ajuste Automático", "Nivel de Ajuste Automatico") ?? "—";
        return new TcpGlobalSettings(rss.Trim(), autotune.Trim());
    }

    // ===================== Análise de canais Wi-Fi =====================

    /// <summary>Conta as redes vizinhas por canal (de <c>netsh wlan show networks mode=bssid</c>) e sugere
    /// o canal menos congestionado em 2,4 GHz e 5 GHz. Ajuda a evitar interferência.</summary>
    public async Task<ChannelAdvice?> AnalyzeChannelsAsync(int currentChannel, string currentBssid = "")
    {
        string output = (await RunAsync("netsh", "wlan show networks mode=bssid")).output;
        return BuildChannelAdvice(ParseWifiNetworks(output), currentChannel, currentBssid);
    }

    /// <summary>
    /// Avalia o congestionamento: em 2,4 GHz canais a menos de 5 de distância se sobrepõem (1, 6 e
    /// 11 são os únicos independentes); em 5 GHz só o mesmo canal disputa o ar. Redes fortes pesam
    /// mais que redes quase inaudíveis, e o próprio roteador (mesmo BSSID/SSID) não conta como vizinho.
    /// </summary>
    internal static ChannelAdvice? BuildChannelAdvice(IReadOnlyList<WifiNetwork> networks, int currentChannel, string currentBssid)
    {
        if (networks.Count == 0) return null;
        var neighbors = networks.Where(n => !SameAccessPoint(n.Bssid, currentBssid)).ToList();

        double Load(int channel) => neighbors.Where(n => Interferes(n.Channel, channel))
            .Sum(n => n.SignalPercent >= 60 ? 1.0 : n.SignalPercent >= 30 ? 0.6 : 0.25);

        int best24 = new[] { 1, 6, 11 }.OrderBy(Load).ThenBy(c => c).First();
        // Canais de 5 GHz sem DFS (o roteador não precisa sair do canal ao detectar radar).
        int best5 = new[] { 36, 40, 44, 48, 149, 153, 157, 161 }.OrderBy(Load).ThenBy(c => c).First();
        int neighborsOnCurrent = neighbors.Count(n => currentChannel > 0 && Interferes(n.Channel, currentChannel));
        bool congested = currentChannel > 0 && Load(currentChannel) >= 3;
        return new ChannelAdvice(currentChannel, best24, best5, neighborsOnCurrent, congested);
    }

    internal static bool Interferes(int a, int b)
    {
        if (a <= 0 || b <= 0) return false;
        bool a24 = a <= 14, b24 = b <= 14;
        if (a24 != b24) return false;
        return a24 ? Math.Abs(a - b) < 5 : a == b;
    }

    /// <summary>
    /// Os BSSIDs do mesmo roteador (2,4/5 GHz, rede de convidados, rede oculta de mesh) são o MAC
    /// base com variações: o fim muda (…:14 / …:15) e as redes virtuais ligam o bit "administrado
    /// localmente" e mexem num byte do meio (e8:81:75:14:4b:15 → ea:81:75:44:4b:15). Essas redes
    /// usam o mesmo rádio, então não disputam o canal com você.
    /// </summary>
    internal static bool SameAccessPoint(string a, string b)
    {
        static byte[]? Parse(string s)
        {
            var parts = s.Trim().Split(':', '-');
            if (parts.Length != 6) return null;
            var bytes = new byte[6];
            for (int i = 0; i < 6; i++)
                if (!byte.TryParse(parts[i], System.Globalization.NumberStyles.HexNumber, null, out bytes[i])) return null;
            return bytes;
        }

        var x = Parse(a);
        var y = Parse(b);
        if (x is null || y is null) return false;
        if (x.Take(5).SequenceEqual(y.Take(5))) return true;
        return (x[0] | 0x02) == (y[0] | 0x02) && x[1] == y[1] && x[2] == y[2] && x[4] == y[4]
               && Math.Abs(x[5] - y[5]) <= 0x0F;
    }

    /// <summary>Pede ao Windows uma varredura nova (WlanScan) e lê as redes encontradas.</summary>
    public async Task<(List<WifiNetwork> Networks, bool LocationBlocked)> ScanWifiNetworksAsync(string interfaceGuid, CancellationToken ct = default)
    {
        if (Guid.TryParse(interfaceGuid, out var guid))
        {
            if (await Task.Run(() => WlanNative.RequestScan(guid)))
                await Task.Delay(4000, ct); // a varredura leva ~2–4 s
        }
        string output = (await RunAsync("netsh", "wlan show networks mode=bssid")).output;
        return (ParseWifiNetworks(output), IsLocationBlocked(output));
    }

    /// <summary>Interpreta <c>netsh wlan show networks mode=bssid</c> (PT ou EN), um item por BSSID.</summary>
    internal static List<WifiNetwork> ParseWifiNetworks(string output)
    {
        var list = new List<WifiNetwork>();
        string ssid = "";
        string? bssid = null;
        int signal = 0, channel = 0;
        string band = "", radio = "";
        int? stations = null, util = null;

        void Flush()
        {
            if (bssid is null) return;
            list.Add(new WifiNetwork(ssid, bssid, signal, channel, band.Length > 0 ? band : InferBand(channel), radio, stations, util));
            bssid = null;
            signal = channel = 0;
            band = radio = "";
            stations = util = null;
        }

        foreach (var raw in output.Split('\n'))
        {
            int colon = raw.IndexOf(':');
            if (colon <= 0) continue;
            string label = raw[..colon].Trim();
            string value = raw[(colon + 1)..].Trim();

            if (Regex.IsMatch(label, @"^SSID \d+$", RegexOptions.IgnoreCase))
            {
                Flush();
                ssid = value;
            }
            else if (Regex.IsMatch(label, @"^BSSID \d+$", RegexOptions.IgnoreCase))
            {
                Flush();
                // O MAC também tem ":", então o valor é tudo depois do primeiro ":" do rótulo.
                bssid = value;
            }
            else if (bssid is null) continue;
            else if (label.StartsWith("Sinal", StringComparison.OrdinalIgnoreCase) || label.StartsWith("Signal", StringComparison.OrdinalIgnoreCase))
                signal = ParseFirstInt(value) ?? 0;
            // Rótulo exato: "Utilização do canal" / "Channel Utilization" não podem virar canal.
            else if (label.Equals("Canal", StringComparison.OrdinalIgnoreCase) || label.Equals("Channel", StringComparison.OrdinalIgnoreCase))
                channel = ParseFirstInt(value) ?? 0;
            else if (label.Equals("Banda", StringComparison.OrdinalIgnoreCase) || label.Equals("Band", StringComparison.OrdinalIgnoreCase))
                band = value;
            else if ((label.StartsWith("Tipo de r", StringComparison.OrdinalIgnoreCase) && !label.StartsWith("Tipo de rede", StringComparison.OrdinalIgnoreCase))
                     || label.StartsWith("Radio type", StringComparison.OrdinalIgnoreCase))
                radio = value;
            else if (label.StartsWith("Esta", StringComparison.OrdinalIgnoreCase) || label.StartsWith("Connected Stations", StringComparison.OrdinalIgnoreCase))
                stations = ParseFirstInt(value);
            else if (label.StartsWith("Utiliza", StringComparison.OrdinalIgnoreCase) || label.StartsWith("Channel Utilization", StringComparison.OrdinalIgnoreCase))
            {
                // "0 (0 %)": o percentual fica entre parênteses.
                var m = Regex.Match(value, @"\((\d+)\s*%\)");
                util = m.Success ? int.Parse(m.Groups[1].Value) : null;
            }
        }
        Flush();
        return list;
    }

    // ===================== Driver do Wi-Fi =====================

    /// <summary>Consulta a data/versão do driver do adaptador Wi-Fi (WMI) e sinaliza se parece antigo
    /// (mais de ~2 anos), o que costuma causar quedas e latência alta.</summary>
    public Task<WifiDriverInfo?> ReadWifiDriverAsync() => Task.Run(() =>
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceName, DriverVersion, DriverDate FROM Win32_PnPSignedDriver WHERE DeviceClass = 'NET'");
            foreach (ManagementObject mo in searcher.Get())
            {
                string name = mo["DeviceName"] as string ?? "";
                if (!name.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase)
                    && !name.Contains("Wireless", StringComparison.OrdinalIgnoreCase)
                    && !name.Contains("802.11", StringComparison.OrdinalIgnoreCase)) continue;

                string version = mo["DriverVersion"] as string ?? "—";
                DateTime? date = null;
                if (mo["DriverDate"] is string raw && raw.Length >= 8)
                {
                    // Formato WMI CIM_DATETIME: yyyyMMdd...
                    if (DateTime.TryParseExact(raw.Substring(0, 8), "yyyyMMdd",
                        null, System.Globalization.DateTimeStyles.None, out var d)) date = d;
                }

                bool outdated = date is { } dt && (DateTime.Now - dt).TotalDays > 730;
                return (WifiDriverInfo?)new WifiDriverInfo(name, version, date, outdated);
            }
        }
        catch { }
        return null;
    });

    // ===================== Nota geral da conexão =====================

    /// <summary>Calcula a nota 0–100 ponderando latência, jitter, perda e sinal. Penaliza mais o que
    /// mais atrapalha jogos online: perda de pacotes e jitter.</summary>
    public ConnectionScore ComputeScore(PingStats ping, int signalPercent, bool isWifi)
    {
        if (!ping.Success) return new ConnectionScore(0, QualityLevel.Poor);

        double score = 100;
        // Latência: ideal < 20 ms; perde ~1 ponto a cada 3 ms acima disso.
        if (ping.AvgMs > 20) score -= Math.Min(35, (ping.AvgMs - 20) / 3.0);
        // Jitter: ideal < 5 ms; muito penalizado (instabilidade arruína jogos).
        if (ping.JitterMs > 5) score -= Math.Min(25, (ping.JitterMs - 5) * 2.0);
        // Perda de pacotes: cada 1% custa 6 pontos.
        score -= Math.Min(40, ping.LossPercent * 6.0);
        // Sinal Wi-Fi fraco: abaixo de 60% começa a pesar.
        if (isWifi && signalPercent > 0 && signalPercent < 60) score -= (60 - signalPercent) / 3.0;

        int finalScore = (int)Math.Round(Math.Clamp(score, 0, 100));
        return new ConnectionScore(finalScore, LevelFromScore(finalScore));
    }

    private static QualityLevel LevelFromScore(int s) => s switch
    {
        >= 85 => QualityLevel.Excellent,
        >= 65 => QualityLevel.Good,
        >= 40 => QualityLevel.Fair,
        _ => QualityLevel.Poor,
    };

    // Faixas de qualidade por métrica (usadas para colorir os indicadores do painel).
    public static QualityLevel PingLevel(double ms) => ms switch { < 20 => QualityLevel.Excellent, < 50 => QualityLevel.Good, < 100 => QualityLevel.Fair, _ => QualityLevel.Poor };
    public static QualityLevel JitterLevel(double ms) => ms switch { < 5 => QualityLevel.Excellent, < 15 => QualityLevel.Good, < 30 => QualityLevel.Fair, _ => QualityLevel.Poor };
    public static QualityLevel LossLevel(double pct) => pct switch { <= 0 => QualityLevel.Excellent, < 1 => QualityLevel.Good, < 5 => QualityLevel.Fair, _ => QualityLevel.Poor };
    public static QualityLevel SignalLevel(int pct) => pct switch { >= 75 => QualityLevel.Excellent, >= 55 => QualityLevel.Good, >= 35 => QualityLevel.Fair, _ => QualityLevel.Poor };

    // ===================== Diagnóstico inteligente =====================

    /// <summary>Roda a bateria completa de testes e devolve um relatório com a nota e as observações
    /// em linguagem simples (chaves de localização). É o miolo do botão "Analisar Minha Conexão".</summary>
    public async Task<DiagnosticReport> AnalyzeAsync(CancellationToken ct = default)
    {
        // GetAllNetworkInterfaces é lento com VPN/Hyper-V: nunca na thread de UI.
        var basics = await Task.Run(ReadBasics, ct);
        var wifi = await ReadWifiAsync();
        // Roteador e internet medidos juntos: separa "problema no Wi-Fi/rede de casa" de "problema no provedor".
        async Task<PingStats?> RouterAsync() =>
            basics.Gateway != "—" ? await PingAsync(basics.Gateway, count: 12, timeoutMs: 1000, ct) : null;
        var routerTask = RouterAsync();
        var internetTask = MeasureInternetAsync(count: 12, ct);
        await Task.WhenAll((Task)routerTask, internetTask);
        PingStats? router = await routerTask;
        var ping = await internetTask;
        var speed = await RunSpeedTestAsync(ct);
        ct.ThrowIfCancellationRequested();
        var tcp = await ReadTcpSettingsAsync();
        var channels = wifi.Connected && wifi.Channel > 0 ? await AnalyzeChannelsAsync(wifi.Channel, wifi.Bssid) : null;
        var driver = basics.IsWifi ? await ReadWifiDriverAsync() : null;
        var score = ComputeScore(ping, wifi.SignalPercent, basics.IsWifi);

        var summary = new List<string>();
        if (basics.IsWifi && wifi.LocationBlocked) summary.Add("Lat_FindLocationOff");
        if (router is { Success: true } r && ping.Success)
        {
            bool localBad = r.LossPercent > 0 || r.JitterMs >= 10 || r.AvgMs >= 15;
            bool internetBad = ping.LossPercent > 0 || ping.JitterMs >= 15 || ping.AvgMs >= 80;
            if (localBad) summary.Add(basics.IsWifi ? "Lat_FindLocalWifi" : "Lat_FindLocalLan");
            else if (internetBad) summary.Add("Lat_FindIsp");
        }
        else if (router is { Success: true } && !ping.Success) summary.Add("Lat_FindNoInternet");
        if (ping.LossPercent >= 5) summary.Add("Lat_FindHighLoss");
        if (ping.JitterMs >= 30) summary.Add("Lat_FindHighJitter");
        if (ping.AvgMs >= 100) summary.Add("Lat_FindHighPing");
        if (basics.IsWifi && wifi.SignalPercent is > 0 and < 50) summary.Add("Lat_FindWeakSignal");
        if (channels is { Congested: true }) summary.Add("Lat_FindCongestedChannel");
        // O netsh reporta a banda no formato do idioma do Windows ("2,4 GHz" em PT, "2.4 GHz" em EN).
        if (basics.IsWifi && (wifi.Band.Contains("2,4") || wifi.Band.Contains("2.4"))) summary.Add("Lat_FindUse5Ghz");
        if (driver is { LooksOutdated: true }) summary.Add("Lat_FindOldDriver");
        if (!tcp.AutoTuningLevel.Contains("normal", StringComparison.OrdinalIgnoreCase)
            && tcp.AutoTuningLevel != "—") summary.Add("Lat_FindAutoTuning");
        if (summary.Count == 0) summary.Add("Lat_FindAllGood");

        return new DiagnosticReport(ping, speed, wifi, basics, tcp, channels, driver, score, summary, router);
    }

    /// <summary>Latência até a internet (1.1.1.1). Se o ICMP estiver bloqueado na rede, mede pelo
    /// handshake TCP na porta 443, para não acusar "sem internet" à toa.</summary>
    public async Task<PingStats> MeasureInternetAsync(int count = 8, CancellationToken ct = default)
    {
        var icmp = await PingAsync(InternetPingTarget, count, timeoutMs: 1000, ct);
        if (icmp.Success) return icmp;
        return await TcpPingAsync(InternetPingTarget, 443, count: Math.Min(count, 5), timeoutMs: 1500, ct);
    }

    // ===================== Latência de drivers (estilo LatencyMon) =====================

    /// <summary>
    /// Verificação simplificada de latência de drivers, inspirada no LatencyMon: coloca uma thread em
    /// prioridade máxima dormindo em ciclos curtos e cronometrados, e mede o desvio entre o tempo
    /// pedido e o tempo real decorrido. Quando um driver monopoliza a CPU numa ISR/DPC, o agendador
    /// do Windows atrasa todas as outras threads — inclusive esta — então o desvio aqui captura o
    /// mesmo sintoma (stuttering de áudio/vídeo, input lag) sem precisar de um driver de kernel/ETW.
    /// Não é uma medição exata de tempo de execução de DPC, apenas um indicador prático equivalente.
    /// </summary>
    [System.Runtime.InteropServices.DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint period);

    [System.Runtime.InteropServices.DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint period);

    public Task<DriverLatencyResult> CheckDriverLatencyAsync(int durationMs = 4000)
    {
        var tcs = new TaskCompletionSource<DriverLatencyResult>();
        // Thread dedicada (não do pool) em prioridade alta: o atraso medido é o do sistema, não a
        // fila do ThreadPool.
        var thread = new Thread(() =>
        {
            try { tcs.SetResult(MeasureDriverLatency(durationMs)); }
            catch (Exception ex) { tcs.SetException(ex); }
        })
        { IsBackground = true, Priority = ThreadPriority.Highest, Name = "Pulse1x DPC probe" };
        thread.Start();
        return tcs.Task;
    }

    private static DriverLatencyResult MeasureDriverLatency(int durationMs)
    {
        // Sem pedir 1 ms ao timer do Windows, Sleep(1) dorme o tique padrão de ~15,6 ms e TODO PC
        // saía "latência elevada" (15 ms). Pedimos 1 ms só durante a medição.
        timeBeginPeriod(1);
        var samples = new List<double>(durationMs);
        try
        {
            Thread.Sleep(50); // deixa a nova resolução valer
            var total = Stopwatch.StartNew();
            var sw = new Stopwatch();
            while (total.ElapsedMilliseconds < durationMs)
            {
                sw.Restart();
                Thread.Sleep(1);
                samples.Add(sw.Elapsed.TotalMilliseconds);
            }
        }
        finally { timeEndPeriod(1); }

        return SummarizeDriverSamples(samples);
    }

    /// <summary>
    /// Desvio = quanto cada Sleep(1) passou do tempo típico do próprio PC (mediana). Comparar com
    /// "1 ms" exato acusava o arredondamento normal do timer como atraso de driver. O pico usa o
    /// percentil 99,9 para que um único soluço do próprio processo não decida o resultado sozinho.
    /// </summary>
    internal static DriverLatencyResult SummarizeDriverSamples(IReadOnlyList<double> samples)
    {
        if (samples.Count < 10) return new DriverLatencyResult(0, 0, QualityLevel.Excellent);
        double baseline = Median(samples);
        var deviations = samples.Select(s => Math.Max(0, s - baseline)).ToList();
        double peak = Percentile(deviations, 0.999);
        double avg = deviations.Average();
        return new DriverLatencyResult(Math.Round(peak, 2), Math.Round(avg, 3), DriverLatencyLevel(peak));
    }

    // Faixas calibradas como o LatencyMon: abaixo de 1ms é excelente, 8ms+ já causa engasgos perceptíveis.
    public static QualityLevel DriverLatencyLevel(double maxDeviationMs) => maxDeviationMs switch
    {
        < 1 => QualityLevel.Excellent,
        < 2 => QualityLevel.Good,
        < 8 => QualityLevel.Fair,
        _ => QualityLevel.Poor,
    };

    // ===================== Helpers de parsing do netsh =====================

    // Extrai o valor de uma linha "Rótulo : valor" do texto do netsh, casando o rótulo contra
    // qualquer um dos fragmentos informados (PT e EN) — os rótulos variam pelo idioma do Windows.
    private static string? ExtractValue(string output, params string[] labelFragments)
    {
        foreach (var line in output.Split('\n'))
        {
            int colon = line.IndexOf(':');
            if (colon <= 0) continue;
            string label = line[..colon].Trim();

            // SSID exato (evita casar BSSID, que termina com a mesma sigla).
            foreach (var frag in labelFragments)
            {
                bool match = frag == "SSID"
                    ? label.Equals("SSID", StringComparison.OrdinalIgnoreCase)
                    : label.StartsWith(frag, StringComparison.OrdinalIgnoreCase);
                if (match)
                {
                    string value = line[(colon + 1)..].Trim();
                    if (value.Length > 0) return value;
                }
            }
        }
        return null;
    }

    private static int? ParseFirstInt(string? text)
    {
        if (text is null) return null;
        var m = Regex.Match(text, @"-?\d+");
        return m.Success && int.TryParse(m.Value, out int v) ? v : null;
    }

    private static double? ParseFirstDouble(string? text)
    {
        if (text is null) return null;
        var m = Regex.Match(text, @"\d+(?:[.,]\d+)?");
        return m.Success && double.TryParse(m.Value.Replace(',', '.'),
            System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : null;
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
                // netsh/ping escrevem na página de código do console (CP850 em português). Lido
                // como UTF-8, "Taxa de recepção" e "Nível de Ajuste" viravam lixo e nunca batiam.
                StandardOutputEncoding = ConsoleEncoding.Oem,
                StandardErrorEncoding = ConsoleEncoding.Oem,
            };
            using var proc = Process.Start(psi)!;
            // Lê as duas saídas juntas: stderr redirecionado e não lido pode travar o processo.
            var stdout = proc.StandardOutput.ReadToEndAsync();
            var stderr = proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();
            return (proc.ExitCode, await stdout + await stderr);
        }
        catch (Exception ex) { return (1, ex.Message); }
    }
}

/// <summary>Chamada mínima à Native Wifi API só para pedir uma varredura nova. O netsh lista o
/// resultado da última varredura do Windows, que pode ter minutos.</summary>
internal static class WlanNative
{
    [System.Runtime.InteropServices.DllImport("wlanapi.dll")]
    private static extern uint WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiatedVersion, out IntPtr clientHandle);

    [System.Runtime.InteropServices.DllImport("wlanapi.dll")]
    private static extern uint WlanScan(IntPtr clientHandle, ref Guid interfaceGuid, IntPtr dot11Ssid, IntPtr ieData, IntPtr reserved);

    [System.Runtime.InteropServices.DllImport("wlanapi.dll")]
    private static extern uint WlanCloseHandle(IntPtr clientHandle, IntPtr reserved);

    public static bool RequestScan(Guid interfaceGuid)
    {
        try
        {
            if (WlanOpenHandle(2, IntPtr.Zero, out _, out var handle) != 0) return false;
            try { return WlanScan(handle, ref interfaceGuid, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) == 0; }
            finally { WlanCloseHandle(handle, IntPtr.Zero); }
        }
        catch { return false; } // sem serviço WLAN (PC só com cabo)
    }
}
