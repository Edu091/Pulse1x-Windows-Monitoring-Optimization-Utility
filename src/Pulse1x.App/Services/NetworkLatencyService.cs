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
    string RadioType);

/// <summary>Dados básicos do adaptador ativo (IP local, DNS, nome) — de <see cref="NetworkInterface"/>.</summary>
public record NetworkBasics(string LocalIp, string Dns, string AdapterName, bool IsWifi, string Gateway);

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
    IReadOnlyList<string> SummaryKeys);

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
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

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
    public async Task<PingStats> PingAsync(string host, int count = 10, int timeoutMs = 1000)
    {
        var times = new List<long>();
        int lost = 0;
        using var ping = new Ping();

        for (int i = 0; i < count; i++)
        {
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
    public async Task<PingStats> TcpPingAsync(string host, int port = 443, int count = 5, int timeoutMs = 1500)
    {
        var times = new List<double>();
        int lost = 0;

        for (int i = 0; i < count; i++)
        {
            try
            {
                using var client = new TcpClient();
                var sw = Stopwatch.StartNew();
                var connect = client.ConnectAsync(host, port);
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
    public async Task<PingStats> MeasureTestServerAsync(string host)
    {
        var icmp = await PingAsync(host, count: 6, timeoutMs: 1500);
        if (icmp.Success && icmp.LossPercent < 100) return icmp;
        return await TcpPingAsync(host, 443, count: 5, timeoutMs: 1500);
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

        string ssid = ExtractValue(output, "SSID") ?? "";
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

        return new WifiInfo(connected, ssid, signal, dbm, channel, band, link, radio);
    }

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
        return new NetworkBasics(ipv4?.Address.ToString() ?? "—", dnsText, nic.Name, isWifi, gw?.Address.ToString() ?? "—");
    }

    /// <summary>O primeiro adaptador ativo, não-loopback, com gateway IPv4 (a conexão real em uso).</summary>
    public static NetworkInterface? ActiveAdapter()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                        && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                        && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
            .FirstOrDefault(n => n.GetIPProperties().GatewayAddresses
                .Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork));
    }

    /// <summary>O alvo de ping preferido: o gateway (mede a latência até o roteador) ou 1.1.1.1.</summary>
    public string PreferredPingTarget()
    {
        var b = ReadBasics();
        return b.Gateway != "—" ? b.Gateway : "1.1.1.1";
    }

    // ===================== Teste de velocidade (HTTP real) =====================

    /// <summary>Mede download e upload reais. Baixa ~15 MB da Cloudflare e envia ~5 MB; converte para Mbps.
    /// Consome banda de internet de propósito — é o que o usuário pede ao tocar em "Analisar".</summary>
    public async Task<SpeedResult> RunSpeedTestAsync()
    {
        double down = await MeasureDownloadAsync(15_000_000);
        double up = await MeasureUploadAsync(5_000_000);
        return new SpeedResult(Math.Round(down, 1), Math.Round(up, 1));
    }

    private static async Task<double> MeasureDownloadAsync(int bytes)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            using var resp = await Http.GetAsync($"https://speed.cloudflare.com/__down?bytes={bytes}",
                HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();
            var data = await resp.Content.ReadAsByteArrayAsync();
            sw.Stop();
            double seconds = Math.Max(sw.Elapsed.TotalSeconds, 0.001);
            return data.Length * 8 / seconds / 1_000_000; // bits -> Mbps
        }
        catch { return 0; }
    }

    private static async Task<double> MeasureUploadAsync(int bytes)
    {
        try
        {
            var payload = new byte[bytes];
            var sw = Stopwatch.StartNew();
            using var content = new ByteArrayContent(payload);
            using var resp = await Http.PostAsync("https://speed.cloudflare.com/__up", content);
            sw.Stop();
            double seconds = Math.Max(sw.Elapsed.TotalSeconds, 0.001);
            return bytes * 8 / seconds / 1_000_000;
        }
        catch { return 0; }
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
    public async Task<ChannelAdvice?> AnalyzeChannelsAsync(int currentChannel)
    {
        string output = (await RunAsync("netsh", "wlan show networks mode=bssid")).output;
        var counts = new Dictionary<int, int>();

        // Conta uma ocorrência por BSSID, casando só o rótulo "Canal"/"Channel" no INÍCIO da linha —
        // sem isso, "Utilização do canal: 0" seria contado como um falso canal 0 (regex sem âncora).
        foreach (Match m in Regex.Matches(output, @"^[ \t]*(?:Channel|Canal)\s*:\s*(\d+)",
            RegexOptions.IgnoreCase | RegexOptions.Multiline))
        {
            if (int.TryParse(m.Groups[1].Value, out int ch))
                counts[ch] = counts.GetValueOrDefault(ch) + 1;
        }

        if (counts.Count == 0) return null;

        int neighborsOnCurrent = counts.GetValueOrDefault(currentChannel);
        // Canais 2,4 GHz não sobrepostos preferidos: 1, 6, 11.
        int best24 = new[] { 1, 6, 11 }.OrderBy(c => counts.GetValueOrDefault(c)).First();
        var ch5 = counts.Keys.Where(c => c >= 36).ToList();
        int best5 = ch5.Count > 0 ? ch5.OrderBy(c => counts[c]).First() : 36;

        bool congested = neighborsOnCurrent >= 3;
        return new ChannelAdvice(currentChannel, best24, best5, neighborsOnCurrent, congested);
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
    public async Task<DiagnosticReport> AnalyzeAsync()
    {
        var basics = ReadBasics();
        var wifi = await ReadWifiAsync();
        var ping = await PingAsync(PreferredPingTarget(), count: 12, timeoutMs: 1500);
        var speed = await RunSpeedTestAsync();
        var tcp = await ReadTcpSettingsAsync();
        var channels = wifi.Connected && wifi.Channel > 0 ? await AnalyzeChannelsAsync(wifi.Channel) : null;
        var driver = basics.IsWifi ? await ReadWifiDriverAsync() : null;
        var score = ComputeScore(ping, wifi.SignalPercent, basics.IsWifi);

        var summary = new List<string>();
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

        return new DiagnosticReport(ping, speed, wifi, basics, tcp, channels, driver, score, summary);
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
    public async Task<DriverLatencyResult> CheckDriverLatencyAsync(int durationMs = 3000)
    {
        return await Task.Run(() =>
        {
            var deviations = new List<double>();
            var totalSw = Stopwatch.StartNew();
            var thread = Thread.CurrentThread;
            var prevPriority = thread.Priority;
            thread.Priority = ThreadPriority.Highest;
            try
            {
                var sw = new Stopwatch();
                const double intervalMs = 1.0;
                while (totalSw.Elapsed.TotalMilliseconds < durationMs)
                {
                    sw.Restart();
                    Thread.Sleep(1);
                    sw.Stop();
                    double deviation = sw.Elapsed.TotalMilliseconds - intervalMs;
                    if (deviation > 0) deviations.Add(deviation);
                }
            }
            finally { thread.Priority = prevPriority; }

            if (deviations.Count == 0) return new DriverLatencyResult(0, 0, QualityLevel.Excellent);
            double max = deviations.Max();
            double avg = deviations.Average();
            return new DriverLatencyResult(Math.Round(max, 2), Math.Round(avg, 2), DriverLatencyLevel(max));
        });
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
                StandardOutputEncoding = Encoding.UTF8,
            };
            using var proc = Process.Start(psi)!;
            string output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();
            return (proc.ExitCode, output);
        }
        catch (Exception ex) { return (1, ex.Message); }
    }
}
