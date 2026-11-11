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
    public const string Neutral = "#808080";

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
/// ViewModel da categoria Latência. Reúne o painel em tempo real (ping da internet e do roteador,
/// jitter/perda, download/upload, Wi-Fi), os gráficos leves, a nota geral, a central de Wi-Fi
/// (varredura de redes, canais e ajustes do driver), os testes avançados (bufferbloat, DNS e MTU),
/// as ferramentas de otimização reversíveis, o diagnóstico inteligente e os servidores de teste.
/// Atualiza os textos ao trocar de idioma (assina <see cref="Loc.LanguageChanged"/>).
/// </summary>
public partial class LatencyViewModel : ObservableObject, IDisposable
{
    private const int MaxChartPoints = 60;

    private readonly NetworkLatencyService _net;
    private readonly NetworkOptimizationService _opt;
    private readonly ISystemMetricsService _metrics;
    private readonly DispatcherTimer _timer;

    /// <summary>Central "Status dos Servidores" — subseção desta categoria com seu próprio ciclo
    /// de atualização (ligado/desligado junto com a visibilidade da página).</summary>
    public ServerStatusViewModel ServerStatus { get; }

    // Seções recolhíveis: o painel em tempo real começa aberto; as demais, fechadas por padrão.
    [ObservableProperty] private bool livePanelExpanded = true;
    [ObservableProperty] private bool wifiExpanded;
    [ObservableProperty] private bool advancedTestsExpanded;
    [ObservableProperty] private bool diagnosticExpanded;
    [ObservableProperty] private bool driverExpanded;
    [ObservableProperty] private bool testServersExpanded;
    [ObservableProperty] private bool toolsExpanded;
    private DateTime _lastSample = DateTime.Now;
    private bool _refreshing;

    // ---- Painel em tempo real ----
    [ObservableProperty] private string pingText = "--";
    [ObservableProperty] private string pingColor = QualityColors.Neutral;
    [ObservableProperty] private string routerPingText = "--";
    [ObservableProperty] private string routerPingColor = QualityColors.Neutral;
    [ObservableProperty] private string jitterText = "--";
    [ObservableProperty] private string jitterColor = QualityColors.Neutral;
    [ObservableProperty] private string lossText = "--";
    [ObservableProperty] private string lossColor = QualityColors.Neutral;
    [ObservableProperty] private string downloadText = "--";
    [ObservableProperty] private string uploadText = "--";
    [ObservableProperty] private string linkSpeedText = "--";
    [ObservableProperty] private string signalText = "--";
    [ObservableProperty] private string signalColor = QualityColors.Neutral;
    [ObservableProperty] private string bandChannelText = "--";
    [ObservableProperty] private string adapterText = "--";
    [ObservableProperty] private string dnsText = "--";
    [ObservableProperty] private string ipText = "--";
    [ObservableProperty] private string ssidText = "--";
    [ObservableProperty] private bool isWifi;
    /// <summary>Windows 11 24H2+ sem permissão de localização: o Windows esconde os dados do Wi-Fi.</summary>
    [ObservableProperty] private bool wifiLocationBlocked;

    // ---- Nota geral ----
    [ObservableProperty] private int scoreValue;
    [ObservableProperty] private string scoreText = "--";
    [ObservableProperty] private string scoreColor = QualityColors.Neutral;
    [ObservableProperty] private string scoreRatingText = "";
    [ObservableProperty] private bool isMeasuringScore;
    private QualityLevel? _scoreLevel;

    // ---- Gráficos ----
    public ObservableCollection<DateTimePoint> DownloadChart { get; } = new();
    public ObservableCollection<DateTimePoint> UploadChart { get; } = new();
    public ObservableCollection<DateTimePoint> PingChart { get; } = new();

    // ---- Ferramentas ----
    public ObservableCollection<NetworkToolViewModel> Actions { get; } = new();   // ações pontuais
    public ObservableCollection<NetworkToolViewModel> Tweaks { get; } = new();     // tweaks reversíveis
    public ObservableCollection<NetworkToolViewModel> AdapterTweaks { get; } = new(); // driver da placa
    public ObservableCollection<NetworkToolViewModel> Profiles { get; } = new();   // perfis (competitivo/estabilidade)
    public ObservableCollection<NetworkToolViewModel> WifiTools { get; } = new();  // ajustes do driver Wi-Fi

    [ObservableProperty] private bool isApplyingAll;
    [ObservableProperty] private string applyAllStatus = "";
    [ObservableProperty] private bool hasChanges;

    // ---- DNS ----
    private static readonly DnsProvider[] DnsChoices =
        { DnsProvider.Automatic, DnsProvider.Cloudflare, DnsProvider.Google, DnsProvider.Quad9, DnsProvider.OpenDNS, DnsProvider.AdGuard };
    public ObservableCollection<DnsOptionViewModel> DnsOptions { get; } = new();
    [ObservableProperty] private int selectedDnsIndex;
    [ObservableProperty] private bool isApplyingDns;

    // ---- Diagnóstico ----
    [ObservableProperty] private bool isAnalyzing;
    [ObservableProperty] private bool hasDiagnostic;
    [ObservableProperty] private string diagnosticHeadline = "";
    public ObservableCollection<DiagnosticLineViewModel> DiagnosticLines { get; } = new();

    // ---- Servidores de teste ----
    public ObservableCollection<TestServerViewModel> TestServers { get; } = new();
    [ObservableProperty] private bool isTestingServers;

    // ---- Verificação de latência de drivers (estilo LatencyMon) ----
    [ObservableProperty] private bool isCheckingDriverLatency;
    [ObservableProperty] private bool hasDriverLatencyResult;
    [ObservableProperty] private string driverLatencyHeadline = "";
    [ObservableProperty] private string driverLatencyColor = QualityColors.Neutral;
    public ObservableCollection<DiagnosticLineViewModel> DriverLatencyLines { get; } = new();

    // ---- Central de Wi-Fi ----
    [ObservableProperty] private bool hasWifiAdapter;
    [ObservableProperty] private bool isScanningWifi;
    [ObservableProperty] private bool hasWifiScan;
    [ObservableProperty] private string wifiScanSummary = "";
    public ObservableCollection<WifiNetworkViewModel> WifiNetworks { get; } = new();
    public ObservableCollection<DiagnosticLineViewModel> WifiAdviceLines { get; } = new();
    [ObservableProperty] private bool wifiGameMode;
    [ObservableProperty] private bool isWifiGameModeBusy;
    [ObservableProperty] private string wifiGameModeStatus = "";
    private bool _syncingGameMode;

    // ---- Testes avançados: bufferbloat ----
    [ObservableProperty] private bool isTestingBufferbloat;
    [ObservableProperty] private bool hasBufferbloat;
    [ObservableProperty] private string bufferbloatProgress = "";
    [ObservableProperty] private string bufferbloatGrade = "";
    [ObservableProperty] private string bufferbloatColor = QualityColors.Neutral;
    [ObservableProperty] private string bufferbloatIdleText = "";
    [ObservableProperty] private string bufferbloatDownText = "";
    [ObservableProperty] private string bufferbloatUpText = "";
    [ObservableProperty] private string bufferbloatAdvice = "";

    // ---- Testes avançados: DNS ----
    [ObservableProperty] private bool isBenchmarkingDns;
    [ObservableProperty] private string dnsBenchStatus = "";
    [ObservableProperty] private bool canApplyFastestDns;
    public ObservableCollection<DnsResultViewModel> DnsResults { get; } = new();
    private DnsProvider? _fastestDns;

    // ---- Testes avançados: MTU ----
    [ObservableProperty] private bool isTestingMtu;
    [ObservableProperty] private string mtuText = "";
    [ObservableProperty] private bool canApplyMtu;
    private int _foundMtu;

    public LatencyViewModel(NetworkLatencyService net, NetworkOptimizationService opt, ISystemMetricsService metrics, ServerStatusService serverStatus)
    {
        _net = net;
        _opt = opt;
        _metrics = metrics;
        ServerStatus = new ServerStatusViewModel(serverStatus);

        BuildTools();
        BuildDnsOptions();
        foreach (var (name, host) in NetworkLatencyService.TestServers)
            TestServers.Add(new TestServerViewModel(name, host));

        // O modo partida do Wi-Fi é de sessão: se o app fechou sem religar a busca automática
        // (queda de energia, encerrado à força), religa agora.
        _ = Task.Run(() =>
        {
            try { _opt.RestoreSessionWifiSettings(); } catch { }
        });

        RefreshChangeState();
        Loc.Instance.LanguageChanged += OnLanguageChanged;

        // Laço leve do painel em tempo real (2s) — coleta fora da UI para não travar a interface.
        // Só roda enquanto a página está visível (ver SetActive).
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => _ = RefreshLiveAsync();
    }

    /// <summary>Liga/desliga o monitoramento em tempo real conforme a página fica visível.</summary>
    public void SetActive(bool active)
    {
        _pageActive = active;
        ApplyActiveState();
    }

    private bool _pageActive;
    private bool _gamingMode;
    private bool _initialStateRead;

    /// <summary>Suspende o monitoramento durante uma partida (Modo Gaming do GameHub) e o retoma depois.</summary>
    public void SetGamingMode(bool active)
    {
        _gamingMode = active;
        ApplyActiveState();
    }

    private void ApplyActiveState()
    {
        bool active = _pageActive && !_gamingMode;
        ServerStatus.SetActive(active);
        if (active)
        {
            _lastSample = DateTime.Now;
            _timer.Start();
            _ = RefreshLiveAsync();
            _ = RefreshScoreAsync();
            if (!_initialStateRead)
            {
                _initialStateRead = true;
                _ = ReadInitialStateAsync();
            }
        }
        else
        {
            _timer.Stop();
            // Sair da página (ou entrar no Modo Jogo) interrompe os testes que consomem banda.
            _pageCts.Cancel();
            _pageCts.Dispose();
            _pageCts = new CancellationTokenSource();
        }
    }

    private CancellationTokenSource _pageCts = new();

    // Estado que não muda a cada 2 s: DNS configurado, se há placa Wi-Fi e o modo partida.
    private async Task ReadInitialStateAsync()
    {
        try
        {
            var dns = await Task.Run(NetworkOptimizationService.CurrentDnsProvider);
            SelectedDnsIndex = dns is { } p ? Array.IndexOf(DnsChoices, p) : -1;
            HasWifiAdapter = await Task.Run(() => NetworkLatencyService.WifiAdapter() is not null);
            if (HasWifiAdapter)
            {
                var scanOn = await _opt.ReadWifiBackgroundScanAsync();
                SetGameModeSilently(scanOn == false);
            }
        }
        catch { }
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

            // Coleta pesada (adaptadores, netsh, ping, throughput) fora da thread de UI.
            var data = await Task.Run(async () =>
            {
                var basics = _net.ReadBasics();
                var wifiTask = _net.ReadWifiAsync();
                var routerTask = basics.Gateway != "—" ? _net.QuickPingAsync(basics.Gateway) : Task.FromResult<long?>(null);
                var internetTask = _net.QuickPingAsync(NetworkLatencyService.InternetPingTarget);
                var net = _metrics.ReadNetwork(elapsed);
                return (basics, wifi: await wifiTask, net, router: await routerTask, internet: await internetTask);
            });

            ApplyLive(data.basics, data.wifi, data.net, data.router, data.internet, now);
        }
        catch { /* leitura volátil; tenta de novo no próximo tick */ }
        finally { _refreshing = false; }
    }

    private void ApplyLive(NetworkBasics basics, WifiInfo wifi, NetworkReading net, long? routerMs, long? internetMs, DateTime now)
    {
        IsWifi = basics.IsWifi;
        WifiLocationBlocked = wifi.LocationBlocked;
        IpText = basics.LocalIp;
        DnsText = basics.Dns;
        AdapterText = basics.AdapterName;
        SsidText = wifi.Connected && wifi.Ssid.Length > 0 ? wifi.Ssid : "—";

        DownloadText = FormatSpeed(net.DownloadKbps);
        UploadText = FormatSpeed(net.UploadKbps);

        if (wifi.Connected && basics.IsWifi)
        {
            LinkSpeedText = wifi.LinkMbps > 0 ? $"{wifi.LinkMbps:0} Mbps" : FormatLink(basics.LinkBps);
            SignalText = $"{wifi.SignalPercent}% ({wifi.SignalDbm} dBm)";
            SignalColor = QualityColors.Hex(NetworkLatencyService.SignalLevel(wifi.SignalPercent));
            BandChannelText = wifi.Channel > 0 ? $"{wifi.Band} · {wifi.Channel}" : wifi.Band;
        }
        else
        {
            // Cabo: a velocidade do link vem da própria placa (antes ficava "—").
            LinkSpeedText = FormatLink(basics.LinkBps);
            SignalText = BandChannelText = "—";
            SignalColor = QualityColors.Neutral;
        }

        if (internetMs is { } ms)
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

        if (routerMs is { } rms)
        {
            RouterPingText = $"{rms} ms";
            RouterPingColor = QualityColors.Hex(RouterPingLevel(rms));
        }
        else
        {
            RouterPingText = "—";
            RouterPingColor = basics.Gateway == "—" ? QualityColors.Neutral : "#DC2626";
        }

        AppendPoint(DownloadChart, now, net.DownloadKbps / 1024.0); // MB/s no gráfico
        AppendPoint(UploadChart, now, net.UploadKbps / 1024.0);
    }

    // Até o roteador o normal é 1–5 ms; acima de 20 ms já indica Wi-Fi ruim ou rede local cheia.
    private static QualityLevel RouterPingLevel(double ms) => ms switch
    {
        < 5 => QualityLevel.Excellent,
        < 12 => QualityLevel.Good,
        < 30 => QualityLevel.Fair,
        _ => QualityLevel.Poor,
    };

    // ===================== Nota geral (rajada de ping curta) =====================

    [RelayCommand]
    private async Task RefreshScoreAsync()
    {
        if (IsMeasuringScore) return;
        IsMeasuringScore = true;
        try
        {
            var wifiTask = _net.ReadWifiAsync();
            var basics = await Task.Run(_net.ReadBasics);
            // A nota usa a latência até a internet: é ela que o jogo sente. Antes media só o
            // roteador (1–3 ms no Wi-Fi), e quase qualquer conexão tirava 100.
            var ping = await _net.MeasureInternetAsync(count: 8);
            IsWifi = basics.IsWifi;
            ApplyScore(ping, await wifiTask);
        }
        catch
        {
            // Leituras de rede voláteis: tenta de novo na próxima vez que a seção for aberta.
        }
        finally
        {
            IsMeasuringScore = false;
        }
    }

    private void ApplyScore(PingStats ping, WifiInfo wifi)
    {
        var score = _net.ComputeScore(ping, wifi.SignalPercent, IsWifi);
        SetScore(score);

        JitterText = ping.Success ? $"{ping.JitterMs:0.#} ms" : "—";
        JitterColor = ping.Success ? QualityColors.Hex(NetworkLatencyService.JitterLevel(ping.JitterMs)) : QualityColors.Neutral;
        LossText = ping.Success ? $"{ping.LossPercent:0.#}%" : "—";
        LossColor = ping.Success ? QualityColors.Hex(NetworkLatencyService.LossLevel(ping.LossPercent)) : "#DC2626";
    }

    private void SetScore(ConnectionScore score)
    {
        _scoreLevel = score.Level;
        ScoreValue = score.Score;
        ScoreText = $"{score.Score}/100";
        ScoreColor = QualityColors.Hex(score.Level);
        ScoreRatingText = Loc.S(QualityColors.LabelKey(score.Level));
    }

    // ===================== Ferramentas =====================

    private void BuildTools()
    {
        Actions.Clear();
        Tweaks.Clear();
        AdapterTweaks.Clear();
        Profiles.Clear();
        WifiTools.Clear();

        // Ações pontuais
        // Ações pontuais. Os dois resets apagam configuração do Windows: guardam uma cópia antes e
        // ganham "Reverter"; as demais não mudam nada persistente (não há o que desfazer).
        Add(Actions, "🧹", "Lat_ToolFlushDns", "Lat_ToolFlushDnsDesc", () => _opt.FlushDnsAsync(), reversible: false);
        Add(Actions, "🔄", "Lat_ToolRenewIp", "Lat_ToolRenewIpDesc", () => _opt.RenewIpAsync(), reversible: false);
        Add(Actions, "♻️", "Lat_ToolResetWinsock", "Lat_ToolResetWinsockDesc", () => _opt.ResetWinsockAsync());
        Add(Actions, "🌐", "Lat_ToolResetTcp", "Lat_ToolResetTcpDesc", () => _opt.ResetTcpIpAsync());
        Add(Actions, "📶", "Lat_ToolRestartAdapter", "Lat_ToolRestartAdapterDesc", () => _opt.RestartAdapterAsync(), reversible: false);
        Add(Actions, "🔁", "Lat_ToolRestartServices", "Lat_ToolRestartServicesDesc", () => _opt.RestartNetworkServicesAsync(), reversible: false);

        // Tweaks reversíveis do Windows
        Add(Tweaks, "🔋", "Lat_ToolWifiPower", "Lat_ToolWifiPowerDesc", () => _opt.DisableWifiPowerSavingAsync());
        Add(Tweaks, "🔌", "Lat_ToolSelectiveSuspend", "Lat_ToolSelectiveSuspendDesc", () => _opt.DisableSelectiveSuspendAsync());
        Add(Tweaks, "💤", "Lat_ToolAdapterPower", "Lat_ToolAdapterPowerDesc", () => _opt.PreventAdapterPowerOffAsync());
        Add(Tweaks, "📤", "Lat_ToolDeliveryOpt", "Lat_ToolDeliveryOptDesc", () => _opt.DisableDeliveryOptimizationP2PAsync());
        Add(Tweaks, "🚦", "Lat_ToolThrottling", "Lat_ToolThrottlingDesc", () => _opt.RemoveNetworkThrottlingAsync());
        Add(Tweaks, "🎯", "Lat_ToolNagle", "Lat_ToolNagleDesc", () => _opt.DisableNagleAsync());
        Add(Tweaks, "📦", "Lat_ToolRsc", "Lat_ToolRscDesc", () => _opt.DisableRscAsync());
        Add(Tweaks, "🧮", "Lat_ToolRss", "Lat_ToolRssDesc", () => _opt.EnableRssAsync());
        Add(Tweaks, "📥", "Lat_ToolAutoTuning", "Lat_ToolAutoTuningDesc", () => _opt.SetAutoTuningNormalAsync());

        // Driver da placa ativa (reinicia a placa por alguns segundos)
        Add(AdapterTweaks, "⚡", "Lat_ToolDriverPower", "Lat_ToolDriverPowerDesc", () => _opt.ApplyAdapterTweakAsync(AdapterTweak.PowerSaving));
        Add(AdapterTweaks, "⏱️", "Lat_ToolInterruptMod", "Lat_ToolInterruptModDesc", () => _opt.ApplyAdapterTweakAsync(AdapterTweak.InterruptModeration));
        Add(AdapterTweaks, "📨", "Lat_ToolLso", "Lat_ToolLsoDesc", () => _opt.ApplyAdapterTweakAsync(AdapterTweak.LargeSendOffload));
        Add(AdapterTweaks, "🚥", "Lat_ToolFlowControl", "Lat_ToolFlowControlDesc", () => _opt.ApplyAdapterTweakAsync(AdapterTweak.FlowControl));

        // Perfis
        Add(Profiles, "🎮", "Lat_ToolCompetitive", "Lat_ToolCompetitiveDesc", () => _opt.ApplyCompetitiveProfileAsync());
        Add(Profiles, "🛡️", "Lat_ToolStability", "Lat_ToolStabilityDesc", () => _opt.ApplyStabilityProfileAsync());

        // Wi-Fi (driver do adaptador sem fio)
        Add(WifiTools, "🧭", "Lat_ToolWifiRoaming", "Lat_ToolWifiRoamingDesc", () => _opt.ApplyAdapterTweakAsync(AdapterTweak.WifiRoaming));
        Add(WifiTools, "📡", "Lat_ToolWifiPrefer5", "Lat_ToolWifiPrefer5Desc", () => _opt.ApplyAdapterTweakAsync(AdapterTweak.WifiPrefer5GHz));
        Add(WifiTools, "📋", "Lat_ToolWlanReport", "Lat_ToolWlanReportDesc", () => _opt.OpenWlanReportAsync(), "Lat_Open", reversible: false);
    }

    private void Add(ObservableCollection<NetworkToolViewModel> target, string icon, string titleKey, string descKey,
        Func<Task<OpResult>> action, string buttonKey = "Lat_Apply", bool reversible = true)
    {
        // Só ficam sem "Reverter" as ações que não mudam configuração (flush DNS, renovar IP,
        // reiniciar placa/serviços, relatório). Tudo o mais grava no log e é desfeito pelo card.
        var tool = reversible
            ? new NetworkToolViewModel(icon, titleKey, descKey, () => _opt.RunScopedAsync(titleKey, action), RefreshChangeState,
                buttonKey, revert: () => _opt.RevertScopeAsync(titleKey))
            : new NetworkToolViewModel(icon, titleKey, descKey, action, RefreshChangeState, buttonKey);
        target.Add(tool);
    }

    // Após qualquer alteração ou reversão: o que ainda dá para desfazer, por card e no geral.
    private void RefreshChangeState()
    {
        HasChanges = _opt.HasChanges();
        foreach (var t in Actions.Concat(Tweaks).Concat(AdapterTweaks).Concat(Profiles).Concat(WifiTools).Where(t => t.IsReversible))
            t.CanRevert = _opt.HasChangesFor(t.TitleKey);
        DnsCanRevert = _opt.HasChangesFor("Lat_ToolDns");
        MtuCanRevert = _opt.HasChangesFor("Lat_MtuTitle");
    }

    partial void OnHasChangesChanged(bool value) => OnPropertyChanged(nameof(CanRestoreAll));
    partial void OnIsApplyingAllChanged(bool value) => OnPropertyChanged(nameof(CanRestoreAll));

    /// <summary>O "Desfazer tudo" fica sempre à vista; só desabilita quando não há o que desfazer.</summary>
    public bool CanRestoreAll => HasChanges && !IsApplyingAll;

    [ObservableProperty] private bool dnsCanRevert;
    [ObservableProperty] private bool mtuCanRevert;

    [RelayCommand]
    private async Task RevertDnsAsync()
    {
        if (IsApplyingDns) return;
        IsApplyingDns = true;
        try
        {
            var r = await Task.Run(() => _opt.RevertScopeAsync("Lat_ToolDns"));
            ApplyAllStatus = "↩ " + r.Text;
            var dns = await Task.Run(NetworkOptimizationService.CurrentDnsProvider);
            SelectedDnsIndex = dns is { } p ? Array.IndexOf(DnsChoices, p) : -1;
        }
        catch (Exception ex) { ApplyAllStatus = "❌ " + ex.Message; }
        finally { IsApplyingDns = false; RefreshChangeState(); }
    }

    [RelayCommand]
    private async Task RevertMtuAsync()
    {
        try
        {
            var r = await Task.Run(() => _opt.RevertScopeAsync("Lat_MtuTitle"));
            MtuText = "↩ " + r.Text;
        }
        catch (Exception ex) { MtuText = "❌ " + ex.Message; }
        RefreshChangeState();
    }

    [RelayCommand]
    private async Task ApplyAllAsync()
    {
        if (IsApplyingAll) return;
        IsApplyingAll = true;
        ApplyAllStatus = Loc.S("Lat_Applying");
        try
        {
            var r = await Task.Run(_opt.ApplyAllAsync);
            ApplyAllStatus = "✅ " + r.Text;
        }
        catch (Exception ex) { ApplyAllStatus = "❌ " + ex.Message; }
        finally { IsApplyingAll = false; RefreshChangeState(); }
    }

    private void BuildDnsOptions()
    {
        int keep = SelectedDnsIndex;
        DnsOptions.Clear();
        foreach (var p in DnsChoices)
        {
            string label = Loc.S("Lat_DnsAutomatic");
            if (p != DnsProvider.Automatic)
            {
                var s = NetworkLatencyService.PublicDnsServers.First(x => x.Name == p.ToString());
                label = $"{s.Name} ({s.Primary})";
            }
            DnsOptions.Add(new DnsOptionViewModel(p, label));
        }
        SelectedDnsIndex = keep;
    }

    [RelayCommand]
    private async Task ApplyDnsAsync()
    {
        if (IsApplyingDns || SelectedDnsIndex < 0 || SelectedDnsIndex >= DnsChoices.Length) return;
        await SetDnsCoreAsync(DnsChoices[SelectedDnsIndex], status => ApplyAllStatus = status);
    }

    private async Task SetDnsCoreAsync(DnsProvider provider, Action<string> report)
    {
        IsApplyingDns = true;
        try
        {
            var r = await Task.Run(() => _opt.SetDnsAsync(provider));
            report((r.Success ? "✅ " : "❌ ") + r.Text);
            if (r.Success) SelectedDnsIndex = Array.IndexOf(DnsChoices, provider);
        }
        // O adaptador pode sumir entre a detecção e a leitura (NetworkInformationException).
        catch (Exception ex) { report("❌ " + ex.Message); }
        finally
        {
            IsApplyingDns = false;
            RefreshChangeState();
        }
    }

    [RelayCommand]
    private async Task RestoreAllAsync()
    {
        // "Desfazer" e "Aplicar tudo" ao mesmo tempo percorriam o log de alterações juntos.
        if (IsApplyingAll) return;
        var confirm = System.Windows.MessageBox.Show(
            Loc.S("Lat_RestoreConfirmBody"), Loc.S("Lat_RestoreConfirmTitle"),
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        IsApplyingAll = true;
        ApplyAllStatus = Loc.S("Lat_Restoring");
        try
        {
            var r = await Task.Run(_opt.RestoreAllAsync);
            ApplyAllStatus = "↩ " + r.Text;
            SetGameModeSilently(false);
            // Os "✅ aplicado" dos cards deixariam de ser verdade depois de desfazer tudo.
            foreach (var t in Actions.Concat(Tweaks).Concat(AdapterTweaks).Concat(Profiles).Concat(WifiTools).Where(t => t.IsReversible))
                t.StatusMessage = "";
            var dns = await Task.Run(NetworkOptimizationService.CurrentDnsProvider);
            SelectedDnsIndex = dns is { } p ? Array.IndexOf(DnsChoices, p) : -1;
        }
        catch (Exception ex) { ApplyAllStatus = "❌ " + ex.Message; }
        finally { IsApplyingAll = false; RefreshChangeState(); }
    }

    // ===================== Central de Wi-Fi =====================

    [RelayCommand]
    private void OpenLocationSettings()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:privacy-location") { UseShellExecute = true }); }
        catch { }
    }

    [RelayCommand]
    private async Task ScanWifiAsync()
    {
        if (IsScanningWifi) return;
        IsScanningWifi = true;
        WifiScanSummary = Loc.S("Lat_WifiScanning");
        try
        {
            var current = await _net.ReadWifiAsync();
            var guid = current.InterfaceGuid;
            if (guid.Length == 0)
                guid = await Task.Run(() => NetworkLatencyService.WifiAdapter()?.Id.Trim('{', '}') ?? "");
            var (networks, blocked) = await _net.ScanWifiNetworksAsync(guid, _pageCts.Token);
            WifiLocationBlocked = blocked || current.LocationBlocked;

            WifiNetworks.Clear();
            WifiAdviceLines.Clear();
            foreach (var n in networks.OrderByDescending(n => n.SignalPercent))
            {
                bool isCurrent = current.Bssid.Length > 0 && n.Bssid.Equals(current.Bssid, StringComparison.OrdinalIgnoreCase);
                bool sameRouter = NetworkLatencyService.SameAccessPoint(n.Bssid, current.Bssid);
                bool interferes = current.Channel > 0 && !sameRouter && NetworkLatencyService.Interferes(n.Channel, current.Channel);
                WifiNetworks.Add(new WifiNetworkViewModel(n, isCurrent, interferes));
            }

            if (WifiLocationBlocked)
            {
                WifiScanSummary = Loc.S("Lat_WifiLocationBlocked");
                HasWifiScan = true;
                return;
            }

            WifiScanSummary = Loc.F("Lat_WifiScanSummary", networks.Count,
                networks.Select(n => n.Channel).Where(c => c > 0).Distinct().Count());
            BuildWifiAdvice(current, networks);
            HasWifiScan = true;
        }
        catch (OperationCanceledException) { WifiScanSummary = ""; }
        catch (Exception ex) { WifiScanSummary = "❌ " + ex.Message; HasWifiScan = true; }
        finally { IsScanningWifi = false; }
    }

    private void BuildWifiAdvice(WifiInfo current, List<WifiNetwork> networks)
    {
        if (!current.Connected || current.Channel <= 0)
        {
            WifiAdviceLines.Add(new DiagnosticLineViewModel("ℹ️", Loc.S("Lat_WifiNotConnected"), QualityColors.Neutral));
            return;
        }

        var advice = NetworkLatencyService.BuildChannelAdvice(networks, current.Channel, current.Bssid);
        bool on24 = current.Channel <= 14;
        if (advice is not null)
        {
            var level = advice.Congested ? QualityLevel.Fair : advice.NeighborsOnCurrent == 0 ? QualityLevel.Excellent : QualityLevel.Good;
            WifiAdviceLines.Add(new DiagnosticLineViewModel(advice.Congested ? "⚠️" : "📶",
                Loc.F("Lat_WifiChannelNow", current.Channel, current.Band, advice.NeighborsOnCurrent), QualityColors.Hex(level)));

            int best = on24 ? advice.SuggestedChannel24 : advice.SuggestedChannel5;
            if (advice.Congested && best != current.Channel)
                WifiAdviceLines.Add(new DiagnosticLineViewModel("🔀", Loc.F("Lat_WifiChangeChannel", best), "#F97316"));
            else
                WifiAdviceLines.Add(new DiagnosticLineViewModel("✅", Loc.S("Lat_WifiChannelOk"), "#22C55E"));
        }

        // Ocupação do canal informada pelo próprio roteador (802.11k/QBSS), quando o driver traz.
        var own = networks.FirstOrDefault(n => n.Bssid.Equals(current.Bssid, StringComparison.OrdinalIgnoreCase));
        if (own?.UtilizationPercent is { } util)
        {
            var level = util < 30 ? QualityLevel.Excellent : util < 50 ? QualityLevel.Good : util < 75 ? QualityLevel.Fair : QualityLevel.Poor;
            WifiAdviceLines.Add(new DiagnosticLineViewModel("📊", Loc.F("Lat_WifiUtilization", util, own.Stations ?? 0), QualityColors.Hex(level)));
        }

        if (on24)
        {
            bool has5 = networks.Any(n => n.Channel > 14 && n.SignalPercent >= 40
                                          && (NetworkLatencyService.SameAccessPoint(n.Bssid, current.Bssid)
                                              || n.Ssid.Equals(current.Ssid, StringComparison.Ordinal)));
            WifiAdviceLines.Add(new DiagnosticLineViewModel("💡", Loc.S(has5 ? "Lat_WifiHas5Ghz" : "Lat_FindUse5Ghz"), "#F97316"));
        }

        if (current.SignalPercent is > 0 and < 50)
            WifiAdviceLines.Add(new DiagnosticLineViewModel("📉", Loc.S("Lat_FindWeakSignal"), "#F97316"));
    }

    partial void OnWifiGameModeChanged(bool value)
    {
        if (_syncingGameMode) return;
        _ = ApplyWifiGameModeAsync(value);
    }

    private async Task ApplyWifiGameModeAsync(bool on)
    {
        IsWifiGameModeBusy = true;
        try
        {
            var r = await _opt.SetWifiBackgroundScanAsync(enabled: !on);
            WifiGameModeStatus = (r.Success ? "✅ " : "❌ ") + r.Text;
            if (!r.Success) SetGameModeSilently(!on);
        }
        catch (Exception ex)
        {
            WifiGameModeStatus = "❌ " + ex.Message;
            SetGameModeSilently(!on);
        }
        finally
        {
            IsWifiGameModeBusy = false;
            RefreshChangeState();
        }
    }

    private void SetGameModeSilently(bool value)
    {
        _syncingGameMode = true;
        WifiGameMode = value;
        _syncingGameMode = false;
    }

    // ===================== Testes avançados =====================

    [RelayCommand]
    private async Task TestBufferbloatAsync()
    {
        if (IsTestingBufferbloat) return;
        IsTestingBufferbloat = true;
        HasBufferbloat = false;
        var progress = new Progress<string>(phase => BufferbloatProgress = Loc.S(phase switch
        {
            "download" => "Lat_BbPhaseDownload",
            "upload" => "Lat_BbPhaseUpload",
            _ => "Lat_BbPhaseIdle",
        }));
        try
        {
            var r = await _net.RunBufferbloatTestAsync(progress, _pageCts.Token);
            if (!r.Success)
            {
                BufferbloatGrade = "—";
                BufferbloatColor = "#DC2626";
                BufferbloatIdleText = BufferbloatDownText = BufferbloatUpText = "";
                BufferbloatAdvice = Loc.S("Lat_BbNoInternet");
            }
            else
            {
                BufferbloatGrade = r.Grade;
                BufferbloatColor = QualityColors.Hex(r.Level);
                BufferbloatIdleText = $"{r.IdleMs:0} ms";
                BufferbloatDownText = $"{r.DownloadLoadedMs:0} ms (+{Math.Max(0, r.DownloadLoadedMs - r.IdleMs):0}) · {r.DownloadMbps:0.#} Mbps";
                BufferbloatUpText = $"{r.UploadLoadedMs:0} ms (+{Math.Max(0, r.UploadLoadedMs - r.IdleMs):0}) · {r.UploadMbps:0.#} Mbps";
                BufferbloatAdvice = Loc.S(r.Level switch
                {
                    QualityLevel.Excellent => "Lat_BbAdviceGood",
                    QualityLevel.Good => "Lat_BbAdviceOk",
                    _ => "Lat_BbAdviceBad",
                });
            }
            HasBufferbloat = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            BufferbloatGrade = "—";
            BufferbloatAdvice = "❌ " + ex.Message;
            HasBufferbloat = true;
        }
        finally
        {
            IsTestingBufferbloat = false;
            BufferbloatProgress = "";
        }
    }

    [RelayCommand]
    private async Task BenchmarkDnsAsync()
    {
        if (IsBenchmarkingDns) return;
        IsBenchmarkingDns = true;
        CanApplyFastestDns = false;
        _fastestDns = null;
        DnsResults.Clear();
        DnsBenchStatus = Loc.S("Lat_DnsBenchRunning");
        try
        {
            var basics = await Task.Run(_net.ReadBasics);
            var current = basics.Dns.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            string currentLabel = Loc.S("Lat_DnsCurrent");
            var results = await _net.BenchmarkDnsAsync(current, currentLabel, _pageCts.Token);
            var fastest = results.FirstOrDefault(r => r.Success);
            foreach (var r in results)
                DnsResults.Add(new DnsResultViewModel(r, ReferenceEquals(r, fastest)));

            if (fastest is null)
            {
                DnsBenchStatus = Loc.S("Lat_DnsBenchFailed");
                return;
            }
            var currentResult = results.FirstOrDefault(r => r.IsCurrent);
            // Diferença de poucos ms não justifica trocar o DNS do provedor (que às vezes acelera CDNs locais).
            bool currentIsGoodEnough = currentResult is { Success: true } && currentResult.MedianMs - fastest.MedianMs < 5;
            if (fastest.IsCurrent || currentIsGoodEnough)
                DnsBenchStatus = Loc.S("Lat_DnsBenchCurrentBest");
            else if (Enum.TryParse<DnsProvider>(fastest.Name, out var provider))
            {
                _fastestDns = provider;
                CanApplyFastestDns = true;
                DnsBenchStatus = Loc.F("Lat_DnsBenchFastest", fastest.Name, $"{fastest.MedianMs:0}");
            }
        }
        catch (OperationCanceledException) { DnsBenchStatus = ""; }
        catch (Exception ex) { DnsBenchStatus = "❌ " + ex.Message; }
        finally { IsBenchmarkingDns = false; }
    }

    [RelayCommand]
    private async Task ApplyFastestDnsAsync()
    {
        if (_fastestDns is not { } provider || IsApplyingDns) return;
        await SetDnsCoreAsync(provider, status => DnsBenchStatus = status);
        CanApplyFastestDns = false;
    }

    [RelayCommand]
    private async Task TestMtuAsync()
    {
        if (IsTestingMtu) return;
        IsTestingMtu = true;
        CanApplyMtu = false;
        MtuText = Loc.S("Lat_MtuRunning");
        try
        {
            var basics = await Task.Run(_net.ReadBasics);
            int current = basics.AdapterName == "—" ? 0 : await _opt.ReadMtuAsync(basics.AdapterName);
            var r = await _net.FindMtuAsync(current, _pageCts.Token);
            if (!r.Success)
            {
                MtuText = Loc.S("Lat_MtuFailed");
                return;
            }
            _foundMtu = r.BestMtu;
            if (current > 0 && r.BestMtu < current)
            {
                MtuText = Loc.F("Lat_MtuLower", r.BestMtu, current);
                CanApplyMtu = true;
            }
            else if (current > 0 && current < r.BestMtu)
            {
                MtuText = Loc.F("Lat_MtuHigher", r.BestMtu, current);
                CanApplyMtu = true;
            }
            else
                MtuText = Loc.F("Lat_MtuOk", r.BestMtu);
        }
        catch (OperationCanceledException) { MtuText = ""; }
        catch (Exception ex) { MtuText = "❌ " + ex.Message; }
        finally { IsTestingMtu = false; }
    }

    [RelayCommand]
    private async Task ApplyMtuAsync()
    {
        if (_foundMtu <= 0) return;
        CanApplyMtu = false;
        try
        {
            var r = await _opt.SetMtuAsync(_foundMtu);
            MtuText = (r.Success ? "✅ " : "❌ ") + r.Text;
        }
        catch (Exception ex) { MtuText = "❌ " + ex.Message; }
        RefreshChangeState();
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
            var report = await _net.AnalyzeAsync(_pageCts.Token);

            IsWifi = report.Basics.IsWifi;
            ApplyScore(report.Ping, report.Wifi);

            DiagnosticHeadline = report.Ping.Success
                ? Loc.F("Lat_DiagHeadline", report.Score.Score, Loc.S(QualityColors.LabelKey(report.Score.Level)).ToLowerInvariant())
                : Loc.S("Lat_DiagHeadlineNoInternet");

            // Métricas medidas
            DiagnosticLines.Add(new DiagnosticLineViewModel("📊", Loc.F("Lat_DiagSpeed",
                $"{report.Speed.DownloadMbps:0.#}", $"{report.Speed.UploadMbps:0.#}"), QualityColors.Neutral));
            if (report.RouterPing is { Success: true } router)
                DiagnosticLines.Add(new DiagnosticLineViewModel("🏠", Loc.F("Lat_DiagRouter",
                    $"{router.AvgMs:0.#}", $"{router.JitterMs:0.#}", $"{router.LossPercent:0.#}"),
                    QualityColors.Hex(RouterPingLevel(router.AvgMs))));
            DiagnosticLines.Add(report.Ping.Success
                ? new DiagnosticLineViewModel("🌍", Loc.F("Lat_DiagPing",
                    $"{report.Ping.AvgMs:0.#}", $"{report.Ping.JitterMs:0.#}", $"{report.Ping.LossPercent:0.#}"),
                    QualityColors.Hex(NetworkLatencyService.PingLevel(report.Ping.AvgMs)))
                : new DiagnosticLineViewModel("🌍", Loc.S("Lat_DiagPingFailed"), "#DC2626"));

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
        catch (OperationCanceledException)
        {
            // Página deixada no meio da análise: sem relatório parcial nem mensagem de erro.
            HasDiagnostic = false;
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
            // Em paralelo: antes eram até ~100 s em fila, um servidor por vez.
            await Task.WhenAll(TestServers.Select(async vm =>
            {
                var stats = await _net.MeasureTestServerAsync(vm.Host, _pageCts.Token);
                vm.SetResult(stats);
            }));
        }
        catch (OperationCanceledException) { }
        finally
        {
            // Os que ficaram "…" voltam ao estado sem medição.
            foreach (var s in TestServers.Where(s => s.IsTesting)) s.ResetPending();
            IsTestingServers = false;
        }
    }

    // ===================== Verificação de latência de drivers =====================

    /// <summary>Roda a verificação simplificada de latência de drivers e monta avisos diferentes
    /// conforme a causa mais provável encontrada.</summary>
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
            DriverLatencyHeadline = Loc.F(headlineKey, $"{result.MaxDeviationMs:0.##}");

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

    private async Task AddDriverLatencyCausesAsync()
    {
        bool foundCause = false;
        var basics = await Task.Run(_net.ReadBasics);

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
        foreach (var t in Actions.Concat(Tweaks).Concat(AdapterTweaks).Concat(Profiles).Concat(WifiTools)) t.RefreshTexts();
        foreach (var n in WifiNetworks) n.RefreshTexts();
        BuildDnsOptions();
        if (_scoreLevel is { } level)
            ScoreRatingText = Loc.S(QualityColors.LabelKey(level));
    }

    // ===================== Helpers =====================

    private static void AppendPoint(ObservableCollection<DateTimePoint> series, DateTime time, double value)
    {
        series.Add(new DateTimePoint(time, value));
        while (series.Count > MaxChartPoints) series.RemoveAt(0);
    }

    private static string FormatSpeed(double kbps) =>
        kbps >= 1024 ? $"{kbps / 1024:0.##} MB/s" : $"{kbps:0.#} KB/s";

    private static string FormatLink(long bps) => bps switch
    {
        <= 0 => "—",
        >= 1_000_000_000 => $"{bps / 1_000_000_000.0:0.#} Gbps",
        _ => $"{bps / 1_000_000.0:0} Mbps",
    };

    public void Dispose()
    {
        _timer.Stop();
        ServerStatus.Dispose();
        Loc.Instance.LanguageChanged -= OnLanguageChanged;
    }
}

/// <summary>Card de uma ferramenta de rede (ação pontual, tweak reversível ou perfil). O texto vem de
/// chaves de localização para acompanhar a troca de idioma ao vivo.</summary>
public partial class NetworkToolViewModel : ObservableObject
{
    private readonly Func<Task<OpResult>> _action;
    private readonly Func<Task<OpResult>>? _revert;
    private readonly Action _onCompleted;

    public string Icon { get; }
    public string TitleKey { get; }
    public string DescKey { get; }
    public string ButtonKey { get; }

    public string Title => Loc.S(TitleKey);
    public string Description => Loc.S(DescKey);
    public string ButtonText => Loc.S(ButtonKey);

    /// <summary>Ferramenta que muda configuração (e por isso pode ser revertida)? Ações pontuais
    /// como limpar o cache DNS não têm o que desfazer.</summary>
    public bool IsReversible => _revert is not null;

    [ObservableProperty] private bool isRunning;
    [ObservableProperty] private string statusMessage = "";
    /// <summary>Esta ferramenta tem alterações ativas que o botão "Reverter" desfaz.</summary>
    [ObservableProperty] private bool canRevert;
    public bool HasStatus => StatusMessage.Length > 0;

    public NetworkToolViewModel(string icon, string titleKey, string descKey, Func<Task<OpResult>> action, Action onCompleted,
        string buttonKey = "Lat_Apply", Func<Task<OpResult>>? revert = null)
    {
        Icon = icon;
        TitleKey = titleKey;
        DescKey = descKey;
        ButtonKey = buttonKey;
        _action = action;
        _revert = revert;
        _onCompleted = onCompleted;
    }

    partial void OnStatusMessageChanged(string value) => OnPropertyChanged(nameof(HasStatus));

    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(ButtonText));
    }

    [RelayCommand]
    private Task Run() => ExecuteAsync(_action);

    [RelayCommand]
    private Task Revert() => _revert is null ? Task.CompletedTask : ExecuteAsync(_revert, reverting: true);

    private async Task ExecuteAsync(Func<Task<OpResult>> work, bool reverting = false)
    {
        if (IsRunning) return;
        IsRunning = true;
        StatusMessage = "";
        try
        {
            // Fora da UI: várias ações listam os adaptadores (lento com VPN/Hyper-V) antes do primeiro await.
            var r = await Task.Run(work);
            StatusMessage = (reverting ? "↩ " : r.Success ? "✅ " : "⚠️ ") + r.Text;
        }
        catch (Exception ex) { StatusMessage = "❌ " + ex.Message; }
        finally
        {
            IsRunning = false;
            _onCompleted();
        }
    }
}

/// <summary>Opção da lista de DNS (rótulo localizável).</summary>
public sealed class DnsOptionViewModel
{
    public DnsProvider Provider { get; }
    public string Label { get; }

    public DnsOptionViewModel(DnsProvider provider, string label)
    {
        Provider = provider;
        Label = label;
    }

    public override string ToString() => Label;
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

/// <summary>Uma rede vizinha na varredura de Wi-Fi.</summary>
public partial class WifiNetworkViewModel : ObservableObject
{
    private readonly WifiNetwork _n;

    public string Ssid => _n.Ssid.Length > 0 ? _n.Ssid : Loc.S("Lat_WifiHidden");
    public string Bssid => _n.Bssid;
    public int SignalPercent => _n.SignalPercent;
    public string SignalText => $"{_n.SignalPercent}%";
    public double SignalBarWidth => Math.Clamp(_n.SignalPercent, 0, 100) * 0.5;
    public string Color => QualityColors.Hex(NetworkLatencyService.SignalLevel(_n.SignalPercent));
    public bool IsCurrent { get; }
    public bool Interferes { get; }
    public string Detail
    {
        get
        {
            var parts = new List<string> { Loc.F("Lat_WifiChannelShort", _n.Channel), _n.Band };
            if (_n.Radio.Length > 0) parts.Add(_n.Radio);
            if (_n.Stations is { } s) parts.Add(Loc.F("Lat_WifiStations", s));
            return string.Join(" · ", parts.Where(p => p.Length > 0 && p != "—"));
        }
    }

    public WifiNetworkViewModel(WifiNetwork n, bool isCurrent, bool interferes)
    {
        _n = n;
        IsCurrent = isCurrent;
        Interferes = interferes;
    }

    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(Ssid));
        OnPropertyChanged(nameof(Detail));
    }
}

/// <summary>Resultado de um servidor no benchmark de DNS.</summary>
public sealed class DnsResultViewModel
{
    public string Name { get; }
    public string Address { get; }
    public string TimeText { get; }
    public string Color { get; }
    public bool IsFastest { get; }

    public DnsResultViewModel(DnsBenchmarkResult r, bool isFastest)
    {
        // O DNS atual aparece marcado também quando é um dos públicos (antes sumia da comparação).
        Name = r.IsCurrent && r.Name != Loc.S("Lat_DnsCurrent") ? $"{r.Name} ({Loc.S("Lat_DnsCurrentShort")})" : r.Name;
        Address = r.Secondary.Length > 0 ? $"{r.Primary} · {r.Secondary}" : r.Primary;
        IsFastest = isFastest;
        if (!r.Success)
        {
            TimeText = "—";
            Color = "#DC2626";
            return;
        }
        TimeText = $"{r.MedianMs:0} ms";
        Color = QualityColors.Hex(r.MedianMs switch
        {
            < 20 => QualityLevel.Excellent,
            < 50 => QualityLevel.Good,
            < 100 => QualityLevel.Fair,
            _ => QualityLevel.Poor,
        });
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
    [ObservableProperty] private string color = QualityColors.Neutral;
    [ObservableProperty] private bool isTesting;

    public TestServerViewModel(string name, string host)
    {
        Name = name;
        Host = host;
    }

    /// <summary>Volta ao estado "ainda não medido" (teste interrompido).</summary>
    public void ResetPending()
    {
        IsTesting = false;
        PingText = JitterText = LossText = "--";
        Color = QualityColors.Neutral;
    }

    public void SetTesting()
    {
        IsTesting = true;
        PingText = JitterText = LossText = "…";
        Color = QualityColors.Neutral;
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
