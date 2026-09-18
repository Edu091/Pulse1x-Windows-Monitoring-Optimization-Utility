using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pulse1x.App.Localization;
using Pulse1x.App.Models;
using Pulse1x.App.Services;
using LiveChartsCore.Defaults;

namespace Pulse1x.App.ViewModels;

public partial class DashboardViewModel : ObservableObject, IDisposable
{
    private const int MaxChartPoints = 300;

    private static readonly Color[] GpuAccentColors =
    {
        Color.FromRgb(0xA8, 0x55, 0xF7), Color.FromRgb(0xEC, 0x48, 0x99), Color.FromRgb(0xF4, 0x3F, 0x5E)
    };

    private readonly IHardwareMonitorService _hardwareMonitor;
    private readonly ISystemMetricsService _systemMetrics;
    private readonly SystemInfoService _systemInfo;
    private readonly SettingsService _settingsService;
    private readonly DispatcherTimer _timer;
    private DateTime _lastNetworkSampleTime = DateTime.Now;

    private InfoItemViewModel? _memoryInfoItem;
    private InfoItemViewModel? _storageInfoItem;
    private InfoItemViewModel? _uptimeInfoItem;
    private InfoItemViewModel? _processInfoItem;

    [ObservableProperty] private string cpuName = "CPU";
    [ObservableProperty] private double cpuUsagePercent;
    [ObservableProperty] private string cpuDetailsText = "--";
    [ObservableProperty] private UsageLevel cpuUsageLevel;

    [ObservableProperty] private double ramUsagePercent;
    [ObservableProperty] private string ramUsedText = "--";
    [ObservableProperty] private UsageLevel ramUsageLevel;

    [ObservableProperty] private string downloadSpeedText = "--";
    [ObservableProperty] private string uploadSpeedText = "--";

    [ObservableProperty] private bool sensorWarningVisible;
    [ObservableProperty] private string sensorWarningMessage = "";

    [ObservableProperty] private bool isCustomizing;

    public ObservableCollection<DateTimePoint> CpuChartValues { get; } = new();
    public ObservableCollection<DateTimePoint> RamChartValues { get; } = new();
    public ObservableCollection<DateTimePoint> NetworkChartValues { get; } = new();

    public ObservableCollection<GpuMetricViewModel> GpuMetrics { get; } = new();

    public ObservableCollection<DiskMetricViewModel> Disks { get; } = new();

    public ObservableCollection<InfoItemViewModel> SystemInfo { get; } = new();

    // Seções reordenáveis/ocultáveis do dashboard. Os três atalhos abaixo apontam para a
    // mesma instância dentro de Sections — permitem bindings diretos no XAML (ex.:
    // "MetricsSection.IsVisible") que continuam válidos mesmo após o usuário reordenar.
    public ObservableCollection<DashboardSectionViewModel> Sections { get; } = new();
    public DashboardSectionViewModel SystemInfoSection { get; }
    public DashboardSectionViewModel MetricsSection { get; }
    public DashboardSectionViewModel ChartsSection { get; }

    public DashboardViewModel(
        IHardwareMonitorService hardwareMonitor,
        ISystemMetricsService systemMetrics,
        SystemInfoService systemInfo,
        SettingsService settingsService)
    {
        _hardwareMonitor = hardwareMonitor;
        _systemMetrics = systemMetrics;
        _systemInfo = systemInfo;
        _settingsService = settingsService;

        if (!string.IsNullOrWhiteSpace(_hardwareMonitor.CpuName))
            CpuName = _hardwareMonitor.CpuName;

        BuildSystemInfo();

        SystemInfoSection = new DashboardSectionViewModel(DashboardSectionKind.SystemInfo, "Dashboard_SectionSystemInfo", true);
        MetricsSection = new DashboardSectionViewModel(DashboardSectionKind.Metrics, "Dashboard_SectionMetrics", true);
        ChartsSection = new DashboardSectionViewModel(DashboardSectionKind.Charts, "Dashboard_SectionCharts", true);
        InitializeSections();

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(_settingsService.Current.UpdateIntervalMs)
        };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();

        Refresh();
    }

    public void UpdateInterval(int milliseconds)
    {
        _timer.Interval = TimeSpan.FromMilliseconds(milliseconds);
    }

    /// <summary>
    /// Liga/desliga a leitura contínua de hardware. Usado pelo Modo Gaming do GameHub: enquanto um
    /// jogo estiver aberto, não faz sentido o Pulse1x continuar consultando sensores a cada segundo
    /// para atualizar uma janela que ninguém está olhando.
    /// </summary>
    public void SetActive(bool active)
    {
        if (active)
        {
            _timer.Start();
            Refresh();
        }
        else
        {
            _timer.Stop();
        }
    }

    private void InitializeSections()
    {
        var defaults = new[] { SystemInfoSection, MetricsSection, ChartsSection };

        var savedOrder = _settingsService.Current.DashboardSectionOrder;
        var orderedSections = savedOrder.Count > 0
            ? savedOrder
                .Select(name => defaults.FirstOrDefault(s => s.Kind.ToString() == name))
                .Where(s => s is not null)
                .Concat(defaults.Where(s => !savedOrder.Contains(s.Kind.ToString())))
                .Cast<DashboardSectionViewModel>()
            : defaults;

        var savedVisibility = _settingsService.Current.DashboardSectionVisibility;
        foreach (var section in orderedSections)
        {
            if (savedVisibility.TryGetValue(section.Kind.ToString(), out var visible))
                section.IsVisible = visible;

            Sections.Add(section);
            section.PropertyChanged += (_, _) => SaveSectionSettings();
        }

        Sections.CollectionChanged += OnSectionsCollectionChanged;
    }

    private void OnSectionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => SaveSectionSettings();

    private void SaveSectionSettings()
    {
        _settingsService.Current.DashboardSectionOrder = Sections.Select(s => s.Kind.ToString()).ToList();
        _settingsService.Current.DashboardSectionVisibility = Sections.ToDictionary(s => s.Kind.ToString(), s => s.IsVisible);
        _settingsService.Save();
    }

    [RelayCommand]
    private void ToggleCustomizing() => IsCustomizing = !IsCustomizing;

    [RelayCommand]
    private void MoveSectionUp(DashboardSectionViewModel? section)
    {
        if (section is null) return;
        int index = Sections.IndexOf(section);
        if (index > 0) Sections.Move(index, index - 1);
    }

    [RelayCommand]
    private void MoveSectionDown(DashboardSectionViewModel? section)
    {
        if (section is null) return;
        int index = Sections.IndexOf(section);
        if (index >= 0 && index < Sections.Count - 1) Sections.Move(index, index + 1);
    }

    private void BuildSystemInfo()
    {
        var info = _systemInfo.GetStaticInfo();

        SystemInfo.Add(new InfoItemViewModel("Dashboard_InfoSystem", info.SystemModel));
        SystemInfo.Add(new InfoItemViewModel("Dashboard_InfoProcessor", CpuName));
        SystemInfo.Add(new InfoItemViewModel("Dashboard_InfoGpu", info.GpuName));
        SystemInfo.Add(new InfoItemViewModel("Dashboard_InfoWindows", info.WindowsVersion));

        // Itens dinâmicos: guardamos a referência para atualizar a cada Refresh.
        _memoryInfoItem = new InfoItemViewModel("Dashboard_InfoMemory", "--");
        _storageInfoItem = new InfoItemViewModel("Dashboard_InfoStorage", "--");
        _uptimeInfoItem = new InfoItemViewModel("Dashboard_InfoUptime", "--");
        _processInfoItem = new InfoItemViewModel("Dashboard_InfoProcesses", "--");

        SystemInfo.Add(_memoryInfoItem);
        SystemInfo.Add(_storageInfoItem);
        SystemInfo.Add(_uptimeInfoItem);
        SystemInfo.Add(_processInfoItem);
    }

    // Evita que dois ciclos de coleta se sobreponham caso uma leitura demore mais
    // que o intervalo do timer. Lido/escrito apenas na thread de UI.
    private bool _isRefreshing;

    private async void Refresh()
    {
        if (_isRefreshing) return;
        _isRefreshing = true;
        try
        {
            var now = DateTime.Now;
            var elapsed = now - _lastNetworkSampleTime;
            _lastNetworkSampleTime = now;

            // Coleta pesada (sensores LHM + consultas WMI + rede + contagem de
            // processos) executada FORA da thread de UI. Era isto que causava as
            // microtravadas: rodando aqui, o desenho/rolagem da interface não trava
            // mais a cada atualização. O 'await' retoma na thread de UI (contexto do
            // Dispatcher), então aplicar os valores abaixo continua thread-safe.
            var snapshot = await Task.Run(() => new MetricsSnapshot(
                _hardwareMonitor.ReadCpu(),
                _hardwareMonitor.ReadGpus(),
                _systemMetrics.ReadRam(),
                _systemMetrics.ReadDisks(),
                _systemMetrics.ReadNetwork(elapsed),
                _systemInfo.GetUptimeText(),
                _systemInfo.GetProcessCount()));

            ApplySnapshot(snapshot, now);
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void ApplySnapshot(MetricsSnapshot s, DateTime now)
    {
        CpuUsagePercent = s.Cpu.UsagePercent;
        CpuDetailsText = FormatDetails(s.Cpu.TemperatureCelsius, s.Cpu.ClockMHz, s.Cpu.TemperatureIsApproximate);
        CpuUsageLevel = UsageLevelHelper.FromPercent(s.Cpu.UsagePercent);

        UpdateSensorWarning(s.Cpu.TemperatureCelsius is null, s.Cpu.TemperatureIsApproximate);

        RamUsagePercent = s.Ram.UsagePercent;
        RamUsedText = $"{s.Ram.UsedGb:0.#} / {s.Ram.TotalGb:0.#} GB";
        RamUsageLevel = UsageLevelHelper.FromPercent(s.Ram.UsagePercent);

        SyncDiskMetrics(s.Disks);
        UpdateSystemInfo(s.Ram, s.Disks, s.UptimeText, s.ProcessCount);

        DownloadSpeedText = FormatSpeed(s.Network.DownloadKbps);
        UploadSpeedText = FormatSpeed(s.Network.UploadKbps);

        SyncGpuMetrics(s.Gpus, now);

        AppendPoint(CpuChartValues, now, s.Cpu.UsagePercent);
        AppendPoint(RamChartValues, now, s.Ram.UsagePercent);
        AppendPoint(NetworkChartValues, now, s.Network.DownloadKbps);
    }

    // Pacote imutável com tudo que foi lido em background, repassado de uma vez para
    // a thread de UI.
    private readonly record struct MetricsSnapshot(
        HardwareReading Cpu,
        IReadOnlyList<GpuReading> Gpus,
        RamReading Ram,
        IReadOnlyList<DiskReading> Disks,
        NetworkReading Network,
        string UptimeText,
        int ProcessCount);

    private void UpdateSensorWarning(bool cpuTemperatureMissing, bool cpuTemperatureApproximate)
    {
        if (!_hardwareMonitor.HasElevatedAccess)
        {
            SensorWarningVisible = true;
            SensorWarningMessage = Loc.S("Dashboard_WarnRunAsAdmin");
        }
        else if (cpuTemperatureMissing)
        {
            SensorWarningVisible = true;
            SensorWarningMessage = Loc.S("Dashboard_WarnCpuTempUnavailable");
        }
        else if (cpuTemperatureApproximate)
        {
            SensorWarningVisible = true;
            SensorWarningMessage = _hardwareMonitor.IsMemoryIntegrityEnabled
                ? Loc.S("Dashboard_WarnApproxMemInteg")
                : Loc.S("Dashboard_WarnApproxOtherApp");
        }
        else
        {
            SensorWarningVisible = false;
        }
    }

    private void SyncGpuMetrics(IReadOnlyList<GpuReading> gpus, DateTime now)
    {
        for (int i = 0; i < gpus.Count; i++)
        {
            var reading = gpus[i];

            if (i >= GpuMetrics.Count)
            {
                GpuMetrics.Add(new GpuMetricViewModel(reading.Name, GpuAccentColors[i % GpuAccentColors.Length]));
            }

            var metric = GpuMetrics[i];
            metric.Name = reading.Name;
            metric.UsagePercent = reading.UsagePercent;
            metric.TemperatureText = FormatDetails(reading.TemperatureCelsius, reading.ClockMHz);
            metric.UsageLevel = UsageLevelHelper.FromPercent(reading.UsagePercent);
            AppendPoint(metric.ChartValues, now, reading.UsagePercent);
        }

        while (GpuMetrics.Count > gpus.Count)
            GpuMetrics.RemoveAt(GpuMetrics.Count - 1);
    }

    private void SyncDiskMetrics(IReadOnlyList<DiskReading> disks)
    {
        // Remove discos que foram desconectados (ex.: HD externo removido).
        for (int i = Disks.Count - 1; i >= 0; i--)
        {
            if (disks.All(d => d.Name != Disks[i].Name))
                Disks.RemoveAt(i);
        }

        // Adiciona/atualiza por identidade (letra), preservando a ordem de descoberta.
        for (int i = 0; i < disks.Count; i++)
        {
            var reading = disks[i];
            var metric = Disks.FirstOrDefault(d => d.Name == reading.Name);
            if (metric is null)
            {
                metric = new DiskMetricViewModel(reading.Name);
                Disks.Insert(Math.Min(i, Disks.Count), metric);
            }

            metric.Title = $"{LocalizeDiskLabel(reading.Label)} • {LocalizeDiskType(reading.TypeText)}";
            metric.PrimaryValue = $"{reading.UsagePercent:0.#}%";
            metric.SecondaryValue = $"{reading.UsedGb:0.#} / {reading.TotalGb:0.#} GB • {reading.FreeGb:0.#} {Loc.S("Dashboard_FreeGbSuffix")}";
            metric.UsagePercent = reading.UsagePercent;
            metric.UsageLevel = UsageLevelHelper.FromPercent(reading.UsagePercent);
        }
    }

    private void UpdateSystemInfo(RamReading ram, IReadOnlyList<DiskReading> disks, string uptimeText, int processCount)
    {
        if (_memoryInfoItem is not null)
            _memoryInfoItem.Value = Loc.F("Dashboard_MemInUse", $"{Math.Round(ram.TotalGb):0}", $"{ram.UsagePercent:0.#}");

        if (_storageInfoItem is not null)
        {
            double internalGb = disks.Where(d => d.TypeText == "Interno").Sum(d => d.TotalGb);
            double allGb = disks.Sum(d => d.TotalGb);
            // Mostra o total interno; se houver discos externos, indica o total geral entre parênteses.
            _storageInfoItem.Value = allGb > internalGb + 0.1
                ? $"{FormatStorage(internalGb)} {Loc.S("Dashboard_StorageInternalSuffix")} • {FormatStorage(allGb)} {Loc.S("Dashboard_StorageTotalSuffix")}"
                : FormatStorage(internalGb);
        }

        if (_uptimeInfoItem is not null)
            _uptimeInfoItem.Value = uptimeText;

        if (_processInfoItem is not null)
            _processInfoItem.Value = processCount.ToString();
    }

    private static string FormatStorage(double gigabytes)
    {
        return gigabytes >= 1024
            ? $"{gigabytes / 1024:0.##} TB"
            : $"{gigabytes:0.#} GB";
    }

    private static void AppendPoint(ObservableCollection<DateTimePoint> series, DateTime time, double value)
    {
        series.Add(new DateTimePoint(time, value));
        while (series.Count > MaxChartPoints)
            series.RemoveAt(0);
    }

    private static string FormatDetails(double? temperatureCelsius, double? clockMHz, bool temperatureIsApproximate = false)
    {
        var tempText = temperatureCelsius is { } temp
            ? $"{temp:0.#} °C{(temperatureIsApproximate ? Loc.S("Dashboard_Approx") : "")}"
            : "N/D";
        var clockText = clockMHz is { } clock ? $"{clock / 1000:0.##} GHz" : "N/D";
        return $"{tempText} • {clockText}";
    }

    // O serviço usa "Disco Local" como rótulo neutro quando o volume não tem nome; traduz aqui.
    private static string LocalizeDiskLabel(string label) =>
        label == "Disco Local" ? Loc.S("Dashboard_DiskLocalLabel") : label;

    private static string LocalizeDiskType(string typeText) => typeText switch
    {
        "Interno" => Loc.S("Dashboard_DiskInternal"),
        "Rede" => Loc.S("Dashboard_DiskNetwork"),
        "Externo (removível)" => Loc.S("Dashboard_DiskRemovable"),
        "Externo (USB)" => Loc.S("Dashboard_DiskUsb"),
        "Outro" => Loc.S("Dashboard_DiskOther"),
        _ => typeText
    };

    private static string FormatSpeed(double kbps)
    {
        return kbps >= 1024 ? $"{kbps / 1024:0.##} MB/s" : $"{kbps:0.#} KB/s";
    }

    public void Dispose()
    {
        _timer.Stop();
    }
}
