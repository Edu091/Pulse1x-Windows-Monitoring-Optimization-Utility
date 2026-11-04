using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pulse1x.App.Localization;
using Pulse1x.App.Services;
using Pulse1x.App.Services.Benchmark;

namespace Pulse1x.App.ViewModels;

/// <summary>
/// Categoria Benchmark: mede CPU (uma thread e todas), memória RAM, placa de vídeo e disco. As
/// quatro primeiras formam o resultado geral; o disco é medido à parte, no SSD/HD escolhido.
/// 1000 pontos = máquina de referência (Core i5-13420H, DDR5-5200, RTX 3050 Laptop, NVMe PCIe 4.0).
/// Cada área também tem um modo stress contínuo, que mostra a pontuação ao vivo (como no CPU-Z).
/// </summary>
public partial class BenchmarkViewModel : ObservableObject
{
    /// <summary>Pontuação que enche a barra dos cartões (2× a referência).</summary>
    public const double BarMax = 2000;

    private static readonly TimeSpan CpuTime = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MemoryTime = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan GpuTime = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DiskTime = TimeSpan.FromSeconds(1.5);

    private readonly BenchmarkHistoryService _store;
    private readonly IHardwareMonitorService _hardware;
    private CancellationTokenSource? _cts;

    public BenchmarkViewModel(BenchmarkHistoryService history, IHardwareMonitorService hardware)
    {
        _store = history;
        _hardware = hardware;
        Cards = new ObservableCollection<BenchmarkCardViewModel>(
            Enum.GetValues<BenchmarkKind>().Select(k => new BenchmarkCardViewModel(k, RunOneAsync, ToggleStressAsync)));
        Loc.Instance.LanguageChanged += Reload;
        Reload();
    }

    public ObservableCollection<BenchmarkCardViewModel> Cards { get; }
    public BenchmarkCardViewModel DiskCard => Cards.First(c => c.Kind == BenchmarkKind.Disk);

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _overallText = "—";
    [ObservableProperty] private double _overallBar;
    [ObservableProperty] private string _overallDelta = "";
    [ObservableProperty] private string _overallDeltaColor = "#9CA3AF";
    [ObservableProperty] private string _overallDate = "";
    [ObservableProperty] private string _batteryWarning = "";
    [ObservableProperty] private IReadOnlyList<BenchmarkHistoryRow> _history = Array.Empty<BenchmarkHistoryRow>();
    [ObservableProperty] private bool _hasHistory;
    /// <summary>Um teste comum (não stress) está rodando — mostra a barra de progresso geral.</summary>
    [ObservableProperty] private bool _isRunningBenchmark;

    /// <summary>Atualiza o aviso de bateria e a lista de discos ao abrir a página.</summary>
    public void OnShown()
    {
        BatteryWarning = OnBattery() ? Loc.S("Bench_BatteryWarning") : "";
        if (!IsBusy) _ = DiskCard.LoadDrivesAsync();
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RunAllAsync() => RunAsync(BenchmarkScoring.CoreKinds);

    private Task RunOneAsync(BenchmarkKind kind) => RunAsync(new[] { kind });

    private bool CanRun() => !IsBusy;

    partial void OnIsBusyChanged(bool value)
    {
        RunAllCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        foreach (var card in Cards) card.CanRun = !value;
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void ClearHistory()
    {
        if (IsBusy) return;
        _store.Clear();
        Reload();
    }

    // =====================================================================================
    //  Benchmark
    // =====================================================================================

    private async Task RunAsync(IReadOnlyList<BenchmarkKind> kinds)
    {
        if (IsBusy) return;
        if (kinds.Contains(BenchmarkKind.Disk) && DiskCard.SelectedDrive is null)
        {
            StatusText = Loc.S("Bench_DiskPick");
            return;
        }

        IsBusy = true;
        IsRunningBenchmark = true;
        Progress = 0;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var results = new List<BenchmarkResult>();
        bool battery = OnBattery();
        BatteryWarning = battery ? Loc.S("Bench_BatteryWarning") : "";
        // Sem EcoQoS durante o teste: o resultado não pode depender de a janela estar na frente.
        using var noThrottling = new PowerThrottlingScope();
        var drive = DiskCard.SelectedDrive;

        try
        {
            for (int i = 0; i < kinds.Count; i++)
            {
                var kind = kinds[i];
                var card = Cards.First(c => c.Kind == kind);
                card.IsRunning = true;
                card.Progress = 0;
                StatusText = Loc.F("Bench_Running", card.Title, i + 1, kinds.Count);

                int step = i;
                var progress = new Progress<double>(p =>
                {
                    card.Progress = p;
                    Progress = (step + p) / kinds.Count;
                });

                // Thread própria e longa: os testes ocupam a CPU por segundos e não podem disputar
                // o pool de threads com a interface.
                var result = await Task.Factory.StartNew(() => kind switch
                {
                    BenchmarkKind.CpuSingle => CpuBenchmark.RunSingle(CpuTime, ct, progress),
                    BenchmarkKind.CpuMulti => CpuBenchmark.RunMulti(CpuTime, ct, progress),
                    BenchmarkKind.Memory => MemoryBenchmark.Run(MemoryTime, TotalRamGb(), ct, progress),
                    BenchmarkKind.Gpu => GpuBenchmark.Run(GpuTime, ct, progress),
                    _ => DiskBenchmark.Run(drive!, DiskTime, ct, progress),
                }, ct, TaskCreationOptions.LongRunning, TaskScheduler.Default);

                results.Add(result);
                card.IsRunning = false;
            }

            _store.Add(new BenchmarkRun
            {
                When = DateTime.Now,
                AppVersion = typeof(BenchmarkViewModel).Assembly.GetName().Version?.ToString(3) ?? "",
                OnBattery = battery,
                Results = results,
            });
            StatusText = Loc.S("Bench_Done");
            Progress = 1;
        }
        catch (OperationCanceledException)
        {
            StatusText = Loc.S("Bench_Canceled");
            Progress = 0;
        }
        catch (Exception ex)
        {
            StatusText = Loc.F("Bench_Failed", ex.Message);
            Progress = 0;
        }
        finally
        {
            foreach (var card in Cards) card.IsRunning = false;
            _cts.Dispose();
            _cts = null;
            IsRunningBenchmark = false;
            IsBusy = false;
            Reload();
            if (kinds.Contains(BenchmarkKind.Disk)) _ = DiskCard.LoadDrivesAsync(); // espaço livre mudou
        }
    }

    // =====================================================================================
    //  Stress em tempo real
    // =====================================================================================

    private BenchmarkCardViewModel? _stressCard;

    public bool IsStressRunning => _stressCard is not null;

    /// <summary>Encerra o stress em andamento (ex.: a janela foi para a bandeja).</summary>
    public void StopStress()
    {
        if (_stressCard is not null) _cts?.Cancel();
    }

    /// <summary>Liga o stress do cartão, ou desliga se ele já estiver rodando.</summary>
    private async Task ToggleStressAsync(BenchmarkCardViewModel card)
    {
        if (ReferenceEquals(_stressCard, card))
        {
            _cts?.Cancel();
            return;
        }
        if (IsBusy) return;
        if (card.Kind == BenchmarkKind.Disk && card.SelectedDrive is null)
        {
            StatusText = Loc.S("Bench_DiskPick");
            return;
        }

        IsBusy = true;
        _stressCard = card;
        card.CanRun = true; // o botão "Parar" do próprio cartão continua ativo
        card.BeginStress();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        StatusText = Loc.F("Bench_StressRunning", card.Title);
        using var noThrottling = new PowerThrottlingScope();
        // Com todos os núcleos ocupados pela carga, a interface precisa de prioridade para seguir
        // desenhando a pontuação e atendendo o botão Parar.
        var uiThread = Thread.CurrentThread;
        var uiPriority = uiThread.Priority;
        uiThread.Priority = ThreadPriority.AboveNormal;
        var drive = card.SelectedDrive;
        var update = new Progress<(BenchmarkResult Result, string Sensor)>(s => card.AddStressSample(s.Result, s.Sensor));

        try
        {
            await Task.Factory.StartNew(() =>
            {
                // O amostrador dorme quase o tempo todo; acima do normal ele acorda no horário mesmo
                // com todos os núcleos ocupados pela carga.
                Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
                PowerThrottlingScope.OptOutCurrentThread();
                using IStressSession session = card.Kind switch
                {
                    BenchmarkKind.CpuSingle => CpuBenchmark.StartStress(multi: false),
                    BenchmarkKind.CpuMulti => CpuBenchmark.StartStress(multi: true),
                    BenchmarkKind.Memory => MemoryBenchmark.StartStress(TotalRamGb(), ct),
                    BenchmarkKind.Gpu => GpuBenchmark.StartStress(ct),
                    _ => DiskBenchmark.StartStress(drive!),
                };
                // Sensores numa tarefa à parte: sob carga total, a leitura (WMI, driver de sensores)
                // pode levar segundos e não pode atrasar a pontuação de cada segundo.
                string sensor = "";
                Task? sensorTask = null;
                while (!ct.IsCancellationRequested)
                {
                    var result = session.Sample(ct);
                    if (sensorTask is null || sensorTask.IsCompleted)
                        sensorTask = Task.Run(() => sensor = ReadSensor(card.Kind));
                    ((IProgress<(BenchmarkResult, string)>)update).Report((result, sensor));
                }
            }, ct, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            StatusText = Loc.F("Bench_Failed", ex.Message);
        }
        finally
        {
            uiThread.Priority = uiPriority;
            card.EndStress();
            if (StatusText == Loc.F("Bench_StressRunning", card.Title))
                StatusText = Loc.F("Bench_StressStopped", card.Title, card.LiveSummary);
            _stressCard = null;
            _cts?.Dispose();
            _cts = null;
            IsBusy = false;
        }
    }

    /// <summary>Temperatura e clock durante o stress: mostra o calor derrubando a pontuação.</summary>
    private string ReadSensor(BenchmarkKind kind)
    {
        try
        {
            if (kind is BenchmarkKind.CpuSingle or BenchmarkKind.CpuMulti or BenchmarkKind.Memory)
            {
                var cpu = _hardware.ReadCpu();
                return Join(cpu.TemperatureCelsius is { } t ? $"{t:0} °C" : null,
                            cpu.ClockMHz is { } c ? $"{c / 1000:0.00} GHz" : null,
                            $"{Loc.S("Bench_Usage")} {cpu.UsagePercent:0}%");
            }
            if (kind == BenchmarkKind.Gpu)
            {
                var gpu = _hardware.ReadGpus().OrderByDescending(g => g.UsagePercent).FirstOrDefault();
                if (gpu is null) return "";
                return Join(gpu.TemperatureCelsius is { } t ? $"{t:0} °C" : null,
                            gpu.ClockMHz is { } c ? $"{c:0} MHz" : null,
                            $"{Loc.S("Bench_Usage")} {gpu.UsagePercent:0}%");
            }
        }
        catch { /* sensores indisponíveis: o stress segue sem eles */ }
        return "";

        static string Join(params string?[] parts) => string.Join(" · ", parts.Where(p => !string.IsNullOrEmpty(p)));
    }

    // =====================================================================================
    //  Histórico e textos
    // =====================================================================================

    /// <summary>Monta cartões, geral e histórico a partir do histórico salvo.</summary>
    private void Reload()
    {
        var runs = _store.Runs;
        foreach (var card in Cards)
        {
            var withKind = runs.Where(r => r.Results.Any(x => x.Kind == card.Kind)).ToList();
            var latest = withKind.Select(r => (Run: r, Result: r.Results.First(x => x.Kind == card.Kind))).FirstOrDefault();
            var previous = withKind.Skip(1).Select(r => r.Get(card.Kind)).FirstOrDefault(r => r is not null);
            card.Update(latest.Result, latest.Run?.When, previous);
        }

        var full = runs.Where(r => r.Overall is not null).ToList();
        if (full.Count > 0)
        {
            int overall = full[0].Overall!.Value;
            OverallText = overall.ToString("N0");
            OverallBar = Math.Min(1, overall / BarMax);
            OverallDate = full[0].When.ToString("g");
            (OverallDelta, OverallDeltaColor) = full.Count > 1 ? Delta(overall, full[1].Overall!.Value) : ("", "#9CA3AF");
        }
        else
        {
            OverallText = "—";
            OverallBar = 0;
            OverallDate = Loc.S("Bench_OverallNeedsAll");
            OverallDelta = "";
        }

        History = runs.Select(r => new BenchmarkHistoryRow(r)).ToList();
        HasHistory = History.Count > 0;
        foreach (var card in Cards) card.RefreshTexts();
    }

    internal static (string Text, string Color) Delta(int current, int previous)
    {
        if (previous <= 0) return ("", "#9CA3AF");
        double pct = (current - previous) * 100.0 / previous;
        // Até ±3% é ruído normal entre execuções (temperatura, tarefas do Windows).
        if (Math.Abs(pct) < 3) return (Loc.F("Bench_DeltaSame", pct.ToString("+0;-0;0")), "#9CA3AF");
        return pct > 0
            ? (Loc.F("Bench_DeltaUp", pct.ToString("0")), "#22C55E")
            : (Loc.F("Bench_DeltaDown", (-pct).ToString("0")), "#F97316");
    }

    private static double? TotalRamGb()
    {
        var status = new MemoryStatusEx();
        return GlobalMemoryStatusEx(status) ? status.ullTotalPhys / (1024d * 1024 * 1024) : null;
    }

    private static bool OnBattery()
    {
        try
        {
            // ACLineStatus 0 = desconectado da tomada; BatteryFlag 128 = sem bateria.
            return GetSystemPowerStatus(out var s) && s.ACLineStatus == 0 && (s.BatteryFlag & 128) == 0;
        }
        catch { return false; }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
}

/// <summary>Cartão de uma área do benchmark (resultado salvo + stress ao vivo).</summary>
public partial class BenchmarkCardViewModel : ObservableObject
{
    private const int SparkPoints = 60;
    private const double SparkWidth = 240, SparkHeight = 40;

    private readonly Func<BenchmarkKind, Task> _run;
    private readonly Func<BenchmarkCardViewModel, Task> _stress;
    private BenchmarkResult? _result;
    private BenchmarkResult? _previous;
    private DateTime? _when;
    private readonly List<int> _samples = new();
    private readonly Stopwatch _stressClock = new();

    public BenchmarkCardViewModel(BenchmarkKind kind, Func<BenchmarkKind, Task> run, Func<BenchmarkCardViewModel, Task> stress)
    {
        Kind = kind;
        _run = run;
        _stress = stress;
    }

    public BenchmarkKind Kind { get; }
    public bool IsDisk => Kind == BenchmarkKind.Disk;

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _scoreText = "—";
    [ObservableProperty] private double _bar;
    [ObservableProperty] private string _deltaText = "";
    [ObservableProperty] private string _deltaColor = "#9CA3AF";
    [ObservableProperty] private string _dateText = "";
    [ObservableProperty] private string _deviceText = "";
    [ObservableProperty] private IReadOnlyList<string> _metrics = Array.Empty<string>();
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _canRun = true;
    [ObservableProperty] private string _error = "";

    // ---- disco
    [ObservableProperty] private IReadOnlyList<DiskTarget> _drives = Array.Empty<DiskTarget>();
    [ObservableProperty] private DiskTarget? _selectedDrive;

    // ---- stress ao vivo
    [ObservableProperty] private bool _isStressing;
    [ObservableProperty] private string _liveScore = "";
    [ObservableProperty] private string _liveStats = "";
    [ObservableProperty] private string _liveSensor = "";
    [ObservableProperty] private PointCollection _spark = new();
    [ObservableProperty] private IReadOnlyList<string> _liveMetrics = Array.Empty<string>();

    public string StressButtonText => Loc.S(IsStressing ? "Bench_StressStop" : "Bench_Stress");

    /// <summary>Resumo do último stress ("méd 980 · mín 940 · máx 1.010 em 02:15").</summary>
    public string LiveSummary { get; private set; } = "";

    partial void OnIsStressingChanged(bool value) => OnPropertyChanged(nameof(StressButtonText));

    [RelayCommand]
    private Task Run() => _run(Kind);

    [RelayCommand]
    private Task Stress() => _stress(this);

    public async Task LoadDrivesAsync()
    {
        if (!IsDisk) return;
        var drives = await Task.Run(DiskBenchmark.ListDrives);
        string? previous = SelectedDrive?.Root;
        Drives = drives;
        SelectedDrive = drives.FirstOrDefault(d => d.Root == previous)
                        ?? drives.FirstOrDefault(d => string.Equals(d.Root, System.IO.Path.GetPathRoot(Environment.SystemDirectory), StringComparison.OrdinalIgnoreCase))
                        ?? drives.FirstOrDefault();
    }

    public void BeginStress()
    {
        _samples.Clear();
        Spark = new PointCollection();
        LiveScore = "…";
        LiveStats = Loc.S("Bench_StressWarming");
        LiveSensor = "";
        LiveMetrics = Array.Empty<string>();
        _stressClock.Restart();
        IsStressing = true;
    }

    public void AddStressSample(BenchmarkResult result, string sensor)
    {
        if (!IsStressing || result.Score <= 0) return;
        _samples.Add(result.Score);
        LiveScore = result.Score.ToString("N0");
        LiveSensor = sensor;
        LiveMetrics = FormatMetrics(result);
        int min = _samples.Min(), max = _samples.Max();
        double avg = _samples.Average();
        string elapsed = _stressClock.Elapsed.ToString(_stressClock.Elapsed.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss");
        LiveStats = Loc.F("Bench_StressStats", avg.ToString("N0"), min.ToString("N0"), max.ToString("N0"), elapsed);
        LiveSummary = LiveStats;

        // Gráfico dos últimos 60 s, com a escala fixa em zero para quedas aparecerem do tamanho real.
        var recent = _samples.Skip(Math.Max(0, _samples.Count - SparkPoints)).ToList();
        double top = Math.Max(1, recent.Max() * 1.1);
        var points = new PointCollection(recent.Count);
        for (int i = 0; i < recent.Count; i++)
            points.Add(new Point(i * SparkWidth / (SparkPoints - 1), SparkHeight - recent[i] / top * SparkHeight));
        points.Freeze();
        Spark = points;
    }

    public void EndStress()
    {
        _stressClock.Stop();
        IsStressing = false;
    }

    public void Update(BenchmarkResult? result, DateTime? when, BenchmarkResult? previous)
    {
        _result = result;
        _when = when;
        _previous = previous;
        RefreshTexts();
    }

    public void RefreshTexts()
    {
        Title = Loc.S("Bench_Title" + Kind);
        Description = Loc.S("Bench_Desc" + Kind);
        OnPropertyChanged(nameof(StressButtonText));

        var r = _result;
        Error = r?.Error is { } err ? Loc.F("Bench_Unavailable", err) : "";
        if (r is null || r.Error is not null)
        {
            ScoreText = "—";
            Bar = 0;
            Metrics = Array.Empty<string>();
            DeltaText = "";
            DateText = r is null ? Loc.S("Bench_NotRunYet") : "";
            DeviceText = r?.Device ?? "";
            return;
        }

        ScoreText = r.Score.ToString("N0");
        Bar = Math.Min(1, r.Score / BenchmarkViewModel.BarMax);
        Metrics = FormatMetrics(r);
        var threads = r.Metrics.FirstOrDefault(m => m.LabelKey == "Bench_Threads");
        DeviceText = r.Device.Length > 0 ? r.Device
            : threads is not null && Kind == BenchmarkKind.CpuMulti ? Loc.F("Bench_ThreadsUsed", (int)threads.Value) : "";
        DateText = _when?.ToString("g") ?? "";
        // Disco: a comparação só vale contra o mesmo disco.
        bool comparable = _previous is { Error: null } p && (Kind != BenchmarkKind.Disk || p.Device == r.Device);
        (DeltaText, DeltaColor) = comparable ? BenchmarkViewModel.Delta(r.Score, _previous!.Score) : ("", "#9CA3AF");
    }

    private static IReadOnlyList<string> FormatMetrics(BenchmarkResult r) => r.Metrics
        .Where(m => m.LabelKey != "Bench_Threads")
        .Select(m => $"{Loc.S(m.LabelKey)}: {m.Value.ToString("N" + m.Decimals)} {m.Unit}")
        .ToList();
}

/// <summary>Linha do histórico.</summary>
public sealed class BenchmarkHistoryRow
{
    public BenchmarkHistoryRow(BenchmarkRun run)
    {
        Date = run.When.ToString("g") + (run.OnBattery ? " 🔋" : "");
        Overall = run.Overall?.ToString("N0") ?? "—";
        CpuSingle = Format(run, BenchmarkKind.CpuSingle);
        CpuMulti = Format(run, BenchmarkKind.CpuMulti);
        Memory = Format(run, BenchmarkKind.Memory);
        Gpu = Format(run, BenchmarkKind.Gpu);
        Disk = Format(run, BenchmarkKind.Disk);
    }

    public string Date { get; }
    public string Overall { get; }
    public string CpuSingle { get; }
    public string CpuMulti { get; }
    public string Memory { get; }
    public string Gpu { get; }
    public string Disk { get; }

    private static string Format(BenchmarkRun run, BenchmarkKind kind) => run.Get(kind)?.Score.ToString("N0") ?? "—";
}
