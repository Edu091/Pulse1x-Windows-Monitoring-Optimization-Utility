using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pulse1x.App.Localization;
using Pulse1x.App.Services;
using Pulse1x.App.Services.Benchmark;

namespace Pulse1x.App.ViewModels;

/// <summary>
/// Categoria Benchmark: mede CPU (uma thread e todas), memória RAM e placa de vídeo, e combina as
/// quatro num resultado geral. 1000 pontos = máquina de referência (Core i5-13420H, DDR5-5200,
/// RTX 3050 Laptop). Cada execução vai para o histórico, para comparar antes e depois de otimizar.
/// </summary>
public partial class BenchmarkViewModel : ObservableObject
{
    /// <summary>Pontuação que enche a barra dos cartões (2× a referência).</summary>
    public const double BarMax = 2000;

    private static readonly TimeSpan CpuTime = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MemoryTime = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan GpuTime = TimeSpan.FromSeconds(2);

    private readonly BenchmarkHistoryService _store;
    private readonly IHardwareMonitorService _hardware;
    private CancellationTokenSource? _cts;

    public BenchmarkViewModel(BenchmarkHistoryService history, IHardwareMonitorService hardware)
    {
        _store = history;
        _hardware = hardware;
        Cards = new ObservableCollection<BenchmarkCardViewModel>(
            Enum.GetValues<BenchmarkKind>().Select(k => new BenchmarkCardViewModel(k, RunOneAsync)));
        Loc.Instance.LanguageChanged += Reload;
        Reload();
    }

    public ObservableCollection<BenchmarkCardViewModel> Cards { get; }

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

    public string CpuName => _hardware.CpuName;

    /// <summary>Atualiza o aviso de bateria ao abrir a página (no Windows, a bateria limita CPU e GPU).</summary>
    public void OnShown()
    {
        BatteryWarning = OnBattery() ? Loc.S("Bench_BatteryWarning") : "";
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RunAllAsync() => RunAsync(Enum.GetValues<BenchmarkKind>());

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

    private async Task RunAsync(IReadOnlyList<BenchmarkKind> kinds)
    {
        if (IsBusy) return;
        IsBusy = true;
        Progress = 0;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var results = new List<BenchmarkResult>();
        bool battery = OnBattery();
        BatteryWarning = battery ? Loc.S("Bench_BatteryWarning") : "";
        // Sem EcoQoS durante o teste: o resultado não pode depender de a janela estar na frente.
        using var noThrottling = new PowerThrottlingScope();

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
                    _ => GpuBenchmark.Run(GpuTime, ct, progress),
                }, ct, TaskCreationOptions.LongRunning, TaskScheduler.Default);

                results.Add(result);
                card.IsRunning = false;
            }

            var run = new BenchmarkRun
            {
                When = DateTime.Now,
                AppVersion = typeof(BenchmarkViewModel).Assembly.GetName().Version?.ToString(3) ?? "",
                OnBattery = battery,
                Results = results,
            };
            _store.Add(run);
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
            IsBusy = false;
            Reload();
        }
    }

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

/// <summary>Cartão de uma área do benchmark.</summary>
public partial class BenchmarkCardViewModel : ObservableObject
{
    private readonly Func<BenchmarkKind, Task> _run;
    private BenchmarkResult? _result;
    private BenchmarkResult? _previous;

    public BenchmarkCardViewModel(BenchmarkKind kind, Func<BenchmarkKind, Task> run)
    {
        Kind = kind;
        _run = run;
    }

    public BenchmarkKind Kind { get; }

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
    private DateTime? _when;

    public string Icon => Kind switch
    {
        BenchmarkKind.CpuSingle => "Flash24",
        BenchmarkKind.CpuMulti => "DeveloperBoard24",
        BenchmarkKind.Memory => "Ram20",
        _ => "Games24",
    };

    [RelayCommand]
    private Task Run() => _run(Kind);

    public void Update(BenchmarkResult? result, DateTime? when, BenchmarkResult? previous)
    {
        _result = result;
        _when = when;
        _previous = previous;
        RefreshTexts();
    }

    public void RefreshTexts()
    {
        string key = Kind switch
        {
            BenchmarkKind.CpuSingle => "CpuSingle",
            BenchmarkKind.CpuMulti => "CpuMulti",
            BenchmarkKind.Memory => "Memory",
            _ => "Gpu",
        };
        Title = Loc.S("Bench_Title" + key);
        Description = Loc.S("Bench_Desc" + key);

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
        Metrics = r.Metrics
            .Where(m => m.LabelKey != "Bench_Threads")
            .Select(m => $"{Loc.S(m.LabelKey)}: {m.Value.ToString("N" + m.Decimals)} {m.Unit}")
            .ToList();
        var threads = r.Metrics.FirstOrDefault(m => m.LabelKey == "Bench_Threads");
        DeviceText = r.Device.Length > 0 ? r.Device
            : threads is not null && Kind == BenchmarkKind.CpuMulti ? Loc.F("Bench_ThreadsUsed", (int)threads.Value) : "";
        DateText = _when?.ToString("g") ?? "";
        (DeltaText, DeltaColor) = _previous is { Error: null } p ? BenchmarkViewModel.Delta(r.Score, p.Score) : ("", "#9CA3AF");
    }
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
    }

    public string Date { get; }
    public string Overall { get; }
    public string CpuSingle { get; }
    public string CpuMulti { get; }
    public string Memory { get; }
    public string Gpu { get; }

    private static string Format(BenchmarkRun run, BenchmarkKind kind) => run.Get(kind)?.Score.ToString("N0") ?? "—";
}
