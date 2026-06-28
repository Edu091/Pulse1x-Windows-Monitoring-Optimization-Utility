using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore.Defaults;
using Pulse1x.App.Localization;
using Pulse1x.App.Services;

namespace Pulse1x.App.ViewModels;

/// <summary>Mapeia uma faixa de qualidade para cor (hex) e emoji do indicador (🟢🟡🟠🔴).</summary>
public static class QualityColors
{
    public static string Hex(QualityLevel l) => l switch
    {
        QualityLevel.Excellent => "#22C55E",
        QualityLevel.Good => "#EAB308",
        QualityLevel.Fair => "#F97316",
        _ => "#DC2626",
    };

    public static string Emoji(QualityLevel l) => l switch
    {
        QualityLevel.Excellent => "🟢",
        QualityLevel.Good => "🟡",
        QualityLevel.Fair => "🟠",
        _ => "🔴",
    };

    public static string LabelKey(QualityLevel l) => l switch
    {
        QualityLevel.Excellent => "Lat_QualExcellent",
        QualityLevel.Good => "Lat_QualGood",
        QualityLevel.Fair => "Lat_QualFair",
        _ => "Lat_QualPoor",
    };
}

/// <summary>
/// ViewModel da categoria Latência. Reúne o painel em tempo real (ping/jitter/perda, download/upload,
/// Wi-Fi), os gráficos leves, a nota geral, as ferramentas de otimização reversíveis, o diagnóstico
/// inteligente e os servidores de teste. Atualiza os textos ao trocar de idioma (assina
/// <see cref="Loc.LanguageChanged"/>), conforme a regra bilíngue do projeto.
/// </summary>
public partial class LatencyViewModel : ObservableObject, IDisposable
{
    private const int MaxChartPoints = 60;

    private readonly NetworkLatencyService _net;
    private readonly NetworkOptimizationService _opt;
    private readonly ISystemMetricsService _metrics;
    private readonly DispatcherTimer _timer;
    private DateTime _lastSample = DateTime.Now;
    private bool _refreshing;

    // ---- Painel em tempo real ----
    [ObservableProperty] private string pingText = "--";
    [ObservableProperty] private string pingColor = "#808080";
    [ObservableProperty] private string jitterText = "--";
    [ObservableProperty] private string jitterColor = "#808080";
    [ObservableProperty] private string lossText = "--";
    [ObservableProperty] private string lossColor = "#808080";
    [ObservableProperty] private string downloadText = "--";
    [ObservableProperty] private string uploadText = "--";
    [ObservableProperty] private string linkSpeedText = "--";
    [ObservableProperty] private string signalText = "--";
    [ObservableProperty] private string signalColor = "#808080";
    [ObservableProperty] private string bandText = "--";
    [ObservableProperty] private string channelText = "--";
    [ObservableProperty] private string dnsText = "--";
    [ObservableProperty] private string ipText = "--";
    [ObservableProperty] private string ssidText = "--";
    [ObservableProperty] private bool isWifi;

    // ---- Nota geral ----
    [ObservableProperty] private int scoreValue;
    [ObservableProperty] private string scoreText = "--";
    [ObservableProperty] private string scoreColor = "#808080";
    [ObservableProperty] private string scoreRatingText = "";

    // ---- Gráficos ----
    public ObservableCollection<DateTimePoint> DownloadChart { get; } = new();
    public ObservableCollection<DateTimePoint> UploadChart { get; } = new();
    public ObservableCollection<DateTimePoint> PingChart { get; } = new();

    // ---- Ferramentas ----
    public ObservableCollection<NetworkToolViewModel> Actions { get; } = new();   // ações pontuais
    public ObservableCollection<NetworkToolViewModel> Tweaks { get; } = new();     // tweaks reversíveis
    public ObservableCollection<NetworkToolViewModel> Profiles { get; } = new();   // perfis (competitivo/estabilidade)

    [ObservableProperty] private bool isApplyingAll;
    [ObservableProperty] private string applyAllStatus = "";
    [ObservableProperty] private bool hasChanges;

    // ---- DNS ----
    public ObservableCollection<string> DnsOptions { get; } = new() { "Automático / Automatic", "Google (8.8.8.8)", "Cloudflare (1.1.1.1)" };
    [ObservableProperty] private int selectedDnsIndex;

    // ---- Diagnóstico ----
    [ObservableProperty] private bool isAnalyzing;
    [ObservableProperty] private bool hasDiagnostic;
    [ObservableProperty] private string diagnosticHeadline = "";
    public ObservableCollection<DiagnosticLineViewModel> DiagnosticLines { get; } = new();

    // ---- Servidores de teste ----
    public ObservableCollection<TestServerViewModel> TestServers { get; } = new();
    [ObservableProperty] private bool isTestingServers;

    [ObservableProperty] private bool isExplainerExpanded;

    // ---- Verificação de latência de drivers (estilo LatencyMon) ----
    [ObservableProperty] private bool isCheckingDriverLatency;
    [ObservableProperty] private bool hasDriverLatencyResult;
    [ObservableProperty] private string driverLatencyHeadline = "";
    [ObservableProperty] private string driverLatencyColor = "#808080";
    public ObservableCollection<DiagnosticLineViewModel> DriverLatencyLines { get; } = new();

    public LatencyViewModel(NetworkLatencyService net, NetworkOptimizationService opt, ISystemMetricsService metrics)
    {
        _net = net;
        _opt = opt;
        _metrics = metrics;

        BuildTools();
        foreach (var (name, host) in NetworkLatencyService.TestServers)
            TestServers.Add(new TestServerViewModel(name, host));

        HasChanges = _opt.HasChanges();
        Loc.Instance.LanguageChanged += OnLanguageChanged;

        // Laço leve do painel em tempo real (2s) — coleta fora da UI para não travar a interface.
        // Só roda enquanto a página está visível (ver SetActive): como cada tick dispara netsh +
        // ping, mantê-lo ligado fora da categoria desperdiçaria CPU/bateria à toa.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => _ = RefreshLiveAsync();
    }

    /// <summary>Liga/desliga o monitoramento em tempo real conforme a página fica visível.
    /// Chamado pelo code-behind da <c>LatencyPage</c> no evento de visibilidade.</summary>
    public void SetActive(bool active)
    {
        if (active)
        {
            _timer.Start();
            _ = RefreshLiveAsync();
            // Jitter, perda e nota precisam de uma rajada de pings — calcula uma vez ao abrir a
            // página para os indicadores não ficarem "--" até o usuário tocar em "Medir".
            _ = RefreshScoreAsync();
        }
        else
        {
            _timer.Stop();
        }
    }

    // ===================== Painel em tempo real =====================

    private async Task RefreshLiveAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            var now = DateTime.Now;
            var elapsed = now - _lastSample;
            _lastSample = now;

            string pingTarget = _net.PreferredPingTarget();

            // Coleta pesada (netsh + ping + throughput) fora da thread de UI.
            var data = await Task.Run(async () =>
            {
                var basics = _net.ReadBasics();
                var wifi = await _net.ReadWifiAsync();
                var net = _metrics.ReadNetwork(elapsed);
                var pingMs = await _net.QuickPingAsync(pingTarget);
                return (basics, wifi, net, pingMs);
            });

            ApplyLive(data.basics, data.wifi, data.net, data.pingMs, now);
        }
        catch { /* leitura volátil; tenta de novo no próximo tick */ }
        finally { _refreshing = false; }
    }

    private void ApplyLive(NetworkBasics basics, WifiInfo wifi, NetworkReading net, long? pingMs, DateTime now)
    {
        IsWifi = basics.IsWifi;
        IpText = basics.LocalIp;
        DnsText = basics.Dns;
        SsidText = wifi.Connected ? wifi.Ssid : "—";

        DownloadText = FormatSpeed(net.DownloadKbps);
        UploadText = FormatSpeed(net.UploadKbps);

        if (wifi.Connected)
        {
            LinkSpeedText = wifi.LinkMbps > 0 ? $"{wifi.LinkMbps:0} Mbps" : "—";
            SignalText = $"{wifi.SignalPercent}% ({wifi.SignalDbm} dBm)";
            SignalColor = QualityColors.Hex(NetworkLatencyService.SignalLevel(wifi.SignalPercent));
            BandText = wifi.Band;
            ChannelText = wifi.Channel > 0 ? wifi.Channel.ToString() : "—";
        }
        else
        {
            LinkSpeedText = SignalText = BandText = ChannelText = "—";
            SignalColor = "#808080";
        }

        if (pingMs is { } ms)
        {
            PingText = $"{ms} ms";
            PingColor = QualityColors.Hex(NetworkLatencyService.PingLevel(ms));
            AppendPoint(PingChart, now, ms);
        }
        else
        {
            PingText = "—";
            PingColor = "#DC2626";
        }

        AppendPoint(DownloadChart, now, net.DownloadKbps / 1024.0); // MB/s no gráfico
        AppendPoint(UploadChart, now, net.UploadKbps / 1024.0);
    }

    // ===================== Nota geral (rajada de ping curta) =====================

    [RelayCommand]
    private async Task RefreshScoreAsync()
    {
        var wifi = await _net.ReadWifiAsync();
        var ping = await _net.PingAsync(_net.PreferredPingTarget(), count: 8, timeoutMs: 1000);
        ApplyScore(ping, wifi);
    }

    private void ApplyScore(PingStats ping, WifiInfo wifi)
    {
        var score = _net.ComputeScore(ping, wifi.SignalPercent, IsWifi);
        ScoreValue = score.Score;
        ScoreText = $"{score.Score}/100";
        ScoreColor = QualityColors.Hex(score.Level);
        ScoreRatingText = QualityColors.Emoji(score.Level) + " " + Loc.S(QualityColors.LabelKey(score.Level));

        JitterText = ping.Success ? $"{ping.JitterMs:0.#} ms" : "—";
        JitterColor = QualityColors.Hex(NetworkLatencyService.JitterLevel(ping.JitterMs));
        LossText = ping.Success ? $"{ping.LossPercent:0.#}%" : "—";
        LossColor = QualityColors.Hex(NetworkLatencyService.LossLevel(ping.LossPercent));
    }

    // ===================== Ferramentas =====================

    private void BuildTools()
    {
        Actions.Clear();
        Tweaks.Clear();
        Profiles.Clear();

        // Ações pontuais
        Add(Actions, "🧹", "Lat_ToolFlushDns", "Lat_ToolFlushDnsDesc", () => _opt.FlushDnsAsync());
        Add(Actions, "🔄", "Lat_ToolRenewIp", "Lat_ToolRenewIpDesc", () => _opt.RenewIpAsync());
        Add(Actions, "♻️", "Lat_ToolResetWinsock", "Lat_ToolResetWinsockDesc", () => _opt.ResetWinsockAsync());
        Add(Actions, "🌐", "Lat_ToolResetTcp", "Lat_ToolResetTcpDesc", () => _opt.ResetTcpIpAsync());
        Add(Actions, "📶", "Lat_ToolRestartAdapter", "Lat_ToolRestartAdapterDesc", () => _opt.RestartAdapterAsync());
        Add(Actions, "🔁", "Lat_ToolRestartServices", "Lat_ToolRestartServicesDesc", () => _opt.RestartNetworkServicesAsync());

        // Tweaks reversíveis
        Add(Tweaks, "🔋", "Lat_ToolWifiPower", "Lat_ToolWifiPowerDesc", () => _opt.DisableWifiPowerSavingAsync());
        Add(Tweaks, "🔌", "Lat_ToolSelectiveSuspend", "Lat_ToolSelectiveSuspendDesc", () => _opt.DisableSelectiveSuspendAsync());
        Add(Tweaks, "🧮", "Lat_ToolRss", "Lat_ToolRssDesc", () => _opt.EnableRssAsync());
        Add(Tweaks, "📥", "Lat_ToolAutoTuning", "Lat_ToolAutoTuningDesc", () => _opt.SetAutoTuningNormalAsync());

        // Perfis
        Add(Profiles, "🎮", "Lat_ToolCompetitive", "Lat_ToolCompetitiveDesc", () => _opt.ApplyCompetitiveProfileAsync());
        Add(Profiles, "🛡️", "Lat_ToolStability", "Lat_ToolStabilityDesc", () => _opt.ApplyStabilityProfileAsync());
    }

    private void Add(ObservableCollection<NetworkToolViewModel> target, string icon, string titleKey, string descKey,
        Func<Task<OpResult>> action)
    {
        target.Add(new NetworkToolViewModel(icon, titleKey, descKey, action, AfterToolRun));
    }

    // Após qualquer ferramenta rodar, atualiza o indicador de "há alterações para desfazer".
    private void AfterToolRun() => HasChanges = _opt.HasChanges();

    [RelayCommand]
    private async Task ApplyAllAsync()
    {
        if (IsApplyingAll) return;
        IsApplyingAll = true;
        ApplyAllStatus = Loc.S("Lat_Applying");
        try
        {
            var r = await _opt.ApplyAllAsync();
            ApplyAllStatus = "✅ " + Loc.S(r.MessageKey);
        }
        catch (Exception ex) { ApplyAllStatus = "❌ " + ex.Message; }
        finally { IsApplyingAll = false; HasChanges = _opt.HasChanges(); }
    }

    [RelayCommand]
    private async Task ApplyDnsAsync()
    {
        var provider = SelectedDnsIndex switch { 1 => DnsProvider.Google, 2 => DnsProvider.Cloudflare, _ => DnsProvider.Automatic };
        var r = await _opt.SetDnsAsync(provider);
        ApplyAllStatus = (r.Success ? "✅ " : "❌ ") + Loc.S(r.MessageKey);
        HasChanges = _opt.HasChanges();
    }

    [RelayCommand]
    private async Task RestoreAllAsync()
    {
        var confirm = System.Windows.MessageBox.Show(
            Loc.S("Lat_RestoreConfirmBody"), Loc.S("Lat_RestoreConfirmTitle"),
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        IsApplyingAll = true;
        ApplyAllStatus = Loc.S("Lat_Restoring");
        try
        {
            var r = await _opt.RestoreAllAsync();
            ApplyAllStatus = "↩ " + Loc.S(r.MessageKey);
        }
        catch (Exception ex) { ApplyAllStatus = "❌ " + ex.Message; }
        finally { IsApplyingAll = false; HasChanges = _opt.HasChanges(); }
    }

    // ===================== Diagnóstico inteligente =====================

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        if (IsAnalyzing) return;
        IsAnalyzing = true;
        HasDiagnostic = false;
        DiagnosticLines.Clear();
        try
        {
            var report = await _net.AnalyzeAsync();

            ScoreValue = report.Score.Score;
            ScoreText = $"{report.Score.Score}/100";
            ScoreColor = QualityColors.Hex(report.Score.Level);
            ScoreRatingText = QualityColors.Emoji(report.Score.Level) + " " + Loc.S(QualityColors.LabelKey(report.Score.Level));
            ApplyScore(report.Ping, report.Wifi);

            DiagnosticHeadline = Loc.F("Lat_DiagHeadline", report.Score.Score,
                Loc.S(QualityColors.LabelKey(report.Score.Level)).ToLowerInvariant());

            // Métricas medidas
            DiagnosticLines.Add(new DiagnosticLineViewModel("📊", Loc.F("Lat_DiagSpeed",
                $"{report.Speed.DownloadMbps:0.#}", $"{report.Speed.UploadMbps:0.#}"), "#808080"));
            DiagnosticLines.Add(new DiagnosticLineViewModel("📡", Loc.F("Lat_DiagPing",
                $"{report.Ping.AvgMs:0.#}", $"{report.Ping.JitterMs:0.#}", $"{report.Ping.LossPercent:0.#}"),
                QualityColors.Hex(NetworkLatencyService.PingLevel(report.Ping.AvgMs))));

            if (report.Wifi.Connected)
                DiagnosticLines.Add(new DiagnosticLineViewModel("📶", Loc.F("Lat_DiagWifi",
                    report.Wifi.SignalPercent, report.Wifi.Band, report.Wifi.Channel),
                    QualityColors.Hex(NetworkLatencyService.SignalLevel(report.Wifi.SignalPercent))));

            // Observações + sugestões em linguagem simples
            foreach (var key in report.SummaryKeys)
            {
                bool good = key == "Lat_FindAllGood";
                DiagnosticLines.Add(new DiagnosticLineViewModel(good ? "✅" : "⚠️", Loc.S(key),
                    good ? "#22C55E" : "#F97316"));
            }

            HasDiagnostic = true;
        }
        catch (Exception ex)
        {
            DiagnosticHeadline = "❌ " + ex.Message;
            HasDiagnostic = true;
        }
        finally { IsAnalyzing = false; }
    }

    // ===================== Servidores de teste =====================

    [RelayCommand]
    private async Task TestServersAsync()
    {
        if (IsTestingServers) return;
        IsTestingServers = true;
        foreach (var s in TestServers) s.SetTesting();
        try
        {
            foreach (var vm in TestServers)
            {
                var stats = await _net.MeasureTestServerAsync(vm.Host);
                vm.SetResult(stats);
            }
        }
        finally { IsTestingServers = false; }
    }

    [RelayCommand]
    private void ToggleExplainer() => IsExplainerExpanded = !IsExplainerExpanded;

    // ===================== Verificação de latência de drivers =====================

    /// <summary>Roda a verificação simplificada de latência de drivers e monta avisos diferentes
    /// conforme a causa mais provável encontrada (driver Wi-Fi desatualizado, economia de energia
    /// ativa, ou nenhuma causa específica identificável).</summary>
    [RelayCommand]
    private async Task CheckDriverLatencyAsync()
    {
        if (IsCheckingDriverLatency) return;
        IsCheckingDriverLatency = true;
        HasDriverLatencyResult = false;
        DriverLatencyLines.Clear();
        try
        {
            var result = await _net.CheckDriverLatencyAsync();
            DriverLatencyColor = QualityColors.Hex(result.Level);

            string headlineKey = result.Level switch
            {
                QualityLevel.Poor => "Lat_DrvHeadlinePoor",
                QualityLevel.Fair => "Lat_DrvHeadlineFair",
                _ => "Lat_DrvHeadlineGood",
            };
            DriverLatencyHeadline = Loc.F(headlineKey, $"{result.MaxDeviationMs:0.#}");

            if (result.Level is QualityLevel.Poor or QualityLevel.Fair)
                await AddDriverLatencyCausesAsync();
            else
                DriverLatencyLines.Add(new DiagnosticLineViewModel("✅", Loc.S("Lat_DrvFindAllGood"), "#22C55E"));

            HasDriverLatencyResult = true;
        }
        catch (Exception ex)
        {
            DriverLatencyHeadline = "❌ " + ex.Message;
            HasDriverLatencyResult = true;
        }
        finally { IsCheckingDriverLatency = false; }
    }

    // Investiga as causas mais comuns de latência de driver e mostra um aviso específico para cada
    // uma encontrada; se nenhuma bater, mostra o aviso genérico com as sugestões padrão.
    private async Task AddDriverLatencyCausesAsync()
    {
        bool foundCause = false;
        var basics = _net.ReadBasics();

        if (basics.IsWifi)
        {
            var driver = await _net.ReadWifiDriverAsync();
            if (driver is { LooksOutdated: true })
            {
                DriverLatencyLines.Add(new DiagnosticLineViewModel("📶", Loc.S("Lat_DrvFindWifiDriver"), "#F97316"));
                foundCause = true;
            }
        }

        var (wifiPowerSavingOn, usbSuspendOn) = await _opt.ReadPowerSavingStatesAsync();
        if (wifiPowerSavingOn || usbSuspendOn)
        {
            DriverLatencyLines.Add(new DiagnosticLineViewModel("🔋", Loc.S("Lat_DrvFindPowerSaving"), "#F97316"));
            foundCause = true;
        }

        if (!foundCause)
            DriverLatencyLines.Add(new DiagnosticLineViewModel("⚠️", Loc.S("Lat_DrvFindGeneric"), "#F97316"));
    }

    // ===================== Idioma =====================

    private void OnLanguageChanged()
    {
        foreach (var t in Actions.Concat(Tweaks).Concat(Profiles)) t.RefreshTexts();
        if (ScoreValue > 0)
        {
            var level = ScoreValue switch { >= 85 => QualityLevel.Excellent, >= 65 => QualityLevel.Good, >= 40 => QualityLevel.Fair, _ => QualityLevel.Poor };
            ScoreRatingText = QualityColors.Emoji(level) + " " + Loc.S(QualityColors.LabelKey(level));
        }
    }

    // ===================== Helpers =====================

    private static void AppendPoint(ObservableCollection<DateTimePoint> series, DateTime time, double value)
    {
        series.Add(new DateTimePoint(time, value));
        while (series.Count > MaxChartPoints) series.RemoveAt(0);
    }

    private static string FormatSpeed(double kbps) =>
        kbps >= 1024 ? $"{kbps / 1024:0.##} MB/s" : $"{kbps:0.#} KB/s";

    public void Dispose()
    {
        _timer.Stop();
        Loc.Instance.LanguageChanged -= OnLanguageChanged;
    }
}

/// <summary>Card de uma ferramenta de rede (ação pontual, tweak reversível ou perfil). O texto vem de
/// chaves de localização para acompanhar a troca de idioma ao vivo.</summary>
public partial class NetworkToolViewModel : ObservableObject
{
    private readonly Func<Task<OpResult>> _action;
    private readonly Action _onCompleted;

    public string Icon { get; }
    public string TitleKey { get; }
    public string DescKey { get; }

    public string Title => Loc.S(TitleKey);
    public string Description => Loc.S(DescKey);

    [ObservableProperty] private bool isRunning;
    [ObservableProperty] private string statusMessage = "";
    public bool HasStatus => StatusMessage.Length > 0;

    public NetworkToolViewModel(string icon, string titleKey, string descKey, Func<Task<OpResult>> action, Action onCompleted)
    {
        Icon = icon;
        TitleKey = titleKey;
        DescKey = descKey;
        _action = action;
        _onCompleted = onCompleted;
    }

    partial void OnStatusMessageChanged(string value) => OnPropertyChanged(nameof(HasStatus));

    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Description));
    }

    [RelayCommand]
    private async Task Run()
    {
        if (IsRunning) return;
        IsRunning = true;
        StatusMessage = "";
        try
        {
            var r = await _action();
            StatusMessage = (r.Success ? "✅ " : "⚠️ ") + Loc.S(r.MessageKey);
        }
        catch (Exception ex) { StatusMessage = "❌ " + ex.Message; }
        finally
        {
            IsRunning = false;
            _onCompleted();
        }
    }
}

/// <summary>Linha do diagnóstico inteligente (ícone + texto + cor de severidade).</summary>
public partial class DiagnosticLineViewModel : ObservableObject
{
    public string Icon { get; }
    public string Text { get; }
    public string Color { get; }

    public DiagnosticLineViewModel(string icon, string text, string color)
    {
        Icon = icon;
        Text = text;
        Color = color;
    }
}

/// <summary>Resultado de latência de um servidor de teste, com estado de progresso.</summary>
public partial class TestServerViewModel : ObservableObject
{
    public string Name { get; }
    public string Host { get; }

    [ObservableProperty] private string pingText = "--";
    [ObservableProperty] private string jitterText = "--";
    [ObservableProperty] private string lossText = "--";
    [ObservableProperty] private string color = "#808080";
    [ObservableProperty] private bool isTesting;

    public TestServerViewModel(string name, string host)
    {
        Name = name;
        Host = host;
    }

    public void SetTesting()
    {
        IsTesting = true;
        PingText = JitterText = LossText = "…";
        Color = "#808080";
    }

    public void SetResult(PingStats stats)
    {
        IsTesting = false;
        if (!stats.Success)
        {
            PingText = JitterText = LossText = "—";
            Color = "#DC2626";
            return;
        }
        PingText = $"{stats.AvgMs:0} ms";
        JitterText = $"{stats.JitterMs:0.#} ms";
        LossText = $"{stats.LossPercent:0.#}%";
        Color = QualityColors.Hex(NetworkLatencyService.PingLevel(stats.AvgMs));
    }
}
