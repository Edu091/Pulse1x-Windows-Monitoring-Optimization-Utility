using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Win32;
using Pulse1x.App.Localization;
using Pulse1x.App.Models;

namespace Pulse1x.App.Services;

/// <summary>
/// Motor do "Diagnóstico Inteligente": coleta um retrato do estado de saúde do PC
/// (CPU, GPU, RAM, discos, sistema, rede, temperaturas e segurança), atribui notas
/// de 0 a 100 por componente e geral, lista problemas com severidade, gera
/// recomendações em linguagem simples e estima riscos futuros.
///
/// É TOTALMENTE somente-leitura: apenas consulta sensores, WMI e o registro. Nunca
/// altera nada no sistema. A coleta roda só quando o usuário inicia, em segundo plano,
/// e o resultado fica em cache no ViewModel — sem monitoramento contínuo.
/// </summary>
public class HealthDiagnosticsService
{
    private readonly IHardwareMonitorService _hardware;
    private readonly ISystemMetricsService _metrics;
    private readonly SystemInfoService _systemInfo;

    public HealthDiagnosticsService(
        IHardwareMonitorService hardware,
        ISystemMetricsService metrics,
        SystemInfoService systemInfo)
    {
        _hardware = hardware;
        _metrics = metrics;
        _systemInfo = systemInfo;
    }

    // Acumula tudo que cada componente vai contribuir para o relatório final.
    private sealed class Context
    {
        public HealthReport Report { get; } = new();
        public void Problem(string desc, ProblemSeverity sev) =>
            Report.Problems.Add(new HealthProblem { Description = desc, Severity = sev });
        public void Recommend(string text) =>
            Report.Recommendations.Add(new Recommendation { Text = text });
        public void Risk(string name, RiskLevel level) =>
            Report.Risks.Add(new RiskItem { Name = name, Level = level });
    }

    /// <summary>Executa o diagnóstico completo em segundo plano e devolve o relatório.</summary>
    public Task<HealthReport> RunAsync(IProgress<string>? progress = null)
    {
        return Task.Run(() =>
        {
            var ctx = new Context();

            progress?.Report(Loc.S("Health_Progress_CpuSensors"));
            var sample = SampleSensors();

            progress?.Report(Loc.S("Health_Progress_Cpu"));
            var cpu = AnalyzeCpu(ctx, sample);

            progress?.Report(Loc.S("Health_Progress_Gpu"));
            var gpu = AnalyzeGpu(ctx, sample);

            progress?.Report(Loc.S("Health_Progress_Ram"));
            var ram = AnalyzeRam(ctx, sample);

            progress?.Report(Loc.S("Health_Progress_Disks"));
            var storage = AnalyzeStorage(ctx);

            progress?.Report(Loc.S("Health_Progress_System"));
            var system = AnalyzeSystem(ctx);

            progress?.Report(Loc.S("Health_Progress_Network"));
            var network = AnalyzeNetwork(ctx);

            progress?.Report(Loc.S("Health_Progress_Temps"));
            var temps = AnalyzeTemperatures(ctx, sample);

            progress?.Report(Loc.S("Health_Progress_Security"));
            var security = AnalyzeSecurity(ctx);

            var components = new[] { cpu, gpu, ram }
                .Concat(storage)
                .Concat(new[] { system, network, temps, security })
                .Where(c => c is not null)
                .Select(c => c!)
                .ToList();

            ctx.Report.Components.AddRange(components);

            // Nota geral = média das notas dos componentes (todos com o mesmo peso).
            ctx.Report.OverallScore = components.Count > 0
                ? (int)Math.Round(components.Average(c => c.Score))
                : 0;

            BuildOverallRisks(ctx, sample);

            // Sem problemas? Garante ao menos uma recomendação positiva.
            if (ctx.Report.Recommendations.Count == 0)
                ctx.Recommend(Loc.S("Health_Rec_AllGood"));

            return ctx.Report;
        });
    }

    // ===================== Amostragem de sensores (curta) =====================

    private sealed class SensorSample
    {
        public double CpuUsageAvg;
        public double CpuUsageMax;
        public double? CpuTempAvg;
        public double? CpuTempMax;
        public bool CpuTempApproximate;
        public double? CpuClockCurrent;
        public double? CpuClockMax;

        public string? GpuName;
        public double GpuUsageAvg;
        public double GpuUsageMax;
        public double? GpuTempAvg;
        public double? GpuTempMax;
        public double? GpuClock;

        public double RamUsageAvg;
        public double RamUsageMax;
        public double RamTotalGb;
        public double RamUsedGb;
    }

    // Lê os sensores algumas vezes ao longo de ~1,8s para obter média e pico — leve e rápido.
    private SensorSample SampleSensors()
    {
        const int samples = 6;
        var cpuUsage = new List<double>();
        var cpuTemp = new List<double>();
        var cpuClock = new List<double>();
        bool approximate = false;

        var gpuUsage = new List<double>();
        var gpuTemp = new List<double>();
        var gpuClock = new List<double>();
        string? gpuName = null;

        var ramUsage = new List<double>();
        double ramTotal = 0, ramUsed = 0;

        for (int i = 0; i < samples; i++)
        {
            try
            {
                var cpu = _hardware.ReadCpu();
                if (cpu.UsagePercent > 0) cpuUsage.Add(cpu.UsagePercent);
                if (cpu.TemperatureCelsius is { } t) cpuTemp.Add(t);
                if (cpu.ClockMHz is { } c) cpuClock.Add(c);
                approximate |= cpu.TemperatureIsApproximate;
            }
            catch { /* sensor indisponível nesta amostra */ }

            try
            {
                var gpus = _hardware.ReadGpus();
                var g = gpus.FirstOrDefault();
                if (g is not null)
                {
                    gpuName ??= g.Name;
                    if (g.UsagePercent > 0) gpuUsage.Add(g.UsagePercent);
                    if (g.TemperatureCelsius is { } gt) gpuTemp.Add(gt);
                    if (g.ClockMHz is { } gc) gpuClock.Add(gc);
                }
            }
            catch { /* sem GPU legível */ }

            try
            {
                var ram = _metrics.ReadRam();
                ramUsage.Add(ram.UsagePercent);
                ramTotal = ram.TotalGb;
                ramUsed = ram.UsedGb;
            }
            catch { /* ignora */ }

            if (i < samples - 1) Thread.Sleep(300);
        }

        return new SensorSample
        {
            CpuUsageAvg = cpuUsage.Count > 0 ? cpuUsage.Average() : 0,
            CpuUsageMax = cpuUsage.Count > 0 ? cpuUsage.Max() : 0,
            CpuTempAvg = cpuTemp.Count > 0 ? cpuTemp.Average() : null,
            CpuTempMax = cpuTemp.Count > 0 ? cpuTemp.Max() : null,
            CpuTempApproximate = approximate,
            CpuClockCurrent = cpuClock.Count > 0 ? cpuClock.Last() : null,
            CpuClockMax = cpuClock.Count > 0 ? cpuClock.Max() : null,
            GpuName = gpuName,
            GpuUsageAvg = gpuUsage.Count > 0 ? gpuUsage.Average() : 0,
            GpuUsageMax = gpuUsage.Count > 0 ? gpuUsage.Max() : 0,
            GpuTempAvg = gpuTemp.Count > 0 ? gpuTemp.Average() : null,
            GpuTempMax = gpuTemp.Count > 0 ? gpuTemp.Max() : null,
            GpuClock = gpuClock.Count > 0 ? gpuClock.Max() : null,
            RamUsageAvg = ramUsage.Count > 0 ? ramUsage.Average() : 0,
            RamUsageMax = ramUsage.Count > 0 ? ramUsage.Max() : 0,
            RamTotalGb = ramTotal,
            RamUsedGb = ramUsed,
        };
    }

    // ===================== CPU =====================

    private ComponentHealth AnalyzeCpu(Context ctx, SensorSample s)
    {
        var c = new ComponentHealth { Key = "cpu", Name = "CPU", Icon = "🧠" };
        int score = 100;

        double maxClock = ReadMaxCpuClockMhz() ?? s.CpuClockMax ?? 0;

        c.Metrics.Add(new(Loc.S("Health_M_Processor"), _hardware.CpuName));
        c.Metrics.Add(new(Loc.S("Health_M_Temperature"), FormatTemp(s.CpuTempAvg, s.CpuTempApproximate)));
        c.Metrics.Add(new(Loc.S("Health_M_MaxTemperature"), FormatTemp(s.CpuTempMax, s.CpuTempApproximate)));
        c.Metrics.Add(new(Loc.S("Health_M_AvgUsage"), $"{s.CpuUsageAvg:0}%"));
        c.Metrics.Add(new(Loc.S("Health_M_CurrentClock"), s.CpuClockCurrent is { } cc ? $"{cc / 1000:0.00} GHz" : "N/D"));
        c.Metrics.Add(new(Loc.S("Health_M_MaxClock"), maxClock > 0 ? $"{maxClock / 1000:0.00} GHz" : "N/D"));

        // Thermal throttling: inferido pela temperatura de pico (sem leitura direta universal).
        bool throttling = s.CpuTempMax is { } tmax && tmax >= 95;
        c.Metrics.Add(new("Thermal throttling", s.CpuTempMax is null ? "N/D"
            : throttling ? Loc.S("Health_M_ThrottlingLikely") : Loc.S("Health_M_ThrottlingNotDetected")));

        c.Metrics.Add(new(Loc.S("Health_M_CpuGpuBottleneck"), DescribeCpuGpuBottleneck(s)));

        int processCount = SafeProcessCount();
        c.Metrics.Add(new(Loc.S("Health_M_ActiveProcesses"), processCount > 0 ? processCount.ToString() : "N/D"));

        // Pontuação.
        if (s.CpuTempMax is { } mt)
        {
            if (mt >= 95) { score -= 35; ctx.Problem(Loc.F("Health_P_CpuMaxTemp", $"{mt:0}"), ProblemSeverity.Critica); ctx.Recommend(Loc.S("Health_R_CpuCoolingCritical")); }
            else if (mt >= 88) { score -= 20; ctx.Problem(Loc.F("Health_P_CpuMaxTemp", $"{mt:0}"), ProblemSeverity.Alta); ctx.Recommend(Loc.S("Health_R_CpuCoolingHigh")); }
            else if (mt >= 80) { score -= 8; ctx.Problem(Loc.F("Health_P_CpuTempSlightlyHigh", $"{mt:0}"), ProblemSeverity.Baixa); }
        }
        if (s.CpuUsageAvg >= 90) { score -= 15; ctx.Problem(Loc.F("Health_P_CpuUsageHigh", $"{s.CpuUsageAvg:0}"), ProblemSeverity.Moderada); ctx.Recommend(Loc.S("Health_R_CpuCloseHeavyApps")); }
        else if (s.CpuUsageAvg >= 75) { score -= 6; }
        if (processCount > 300) { score -= 6; ctx.Problem(Loc.F("Health_P_ManyProcesses", processCount), ProblemSeverity.Baixa); }

        c.Score = Clamp(score);
        c.Summary = StateText(c.Score);
        return c;
    }

    private double? ReadMaxCpuClockMhz()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT MaxClockSpeed FROM Win32_Processor");
            foreach (ManagementObject mo in searcher.Get())
                if (mo["MaxClockSpeed"] is { } v) return Convert.ToDouble(v);
        }
        catch { }
        return null;
    }

    // ===================== GPU =====================

    private ComponentHealth AnalyzeGpu(Context ctx, SensorSample s)
    {
        var c = new ComponentHealth { Key = "gpu", Name = "GPU", Icon = "🎮" };
        int score = 100;

        var (gpuName, vramText) = ReadGpuInfo(s.GpuName);

        c.Metrics.Add(new(Loc.S("Health_M_Gpu"), gpuName));
        c.Metrics.Add(new(Loc.S("Health_M_Temperature"), FormatTemp(s.GpuTempAvg, false)));
        c.Metrics.Add(new(Loc.S("Health_M_MaxTemperature"), FormatTemp(s.GpuTempMax, false)));
        c.Metrics.Add(new(Loc.S("Health_M_Usage"), s.GpuUsageAvg > 0 ? $"{s.GpuUsageAvg:0}%" : Loc.S("Health_M_Idle")));
        c.Metrics.Add(new("VRAM", vramText));
        c.Metrics.Add(new(Loc.S("Health_M_Clock"), s.GpuClock is { } gc ? $"{gc:0} MHz" : "N/D"));
        c.Metrics.Add(new(Loc.S("Health_M_CpuGpuBottleneck"), DescribeCpuGpuBottleneck(s)));
        c.Metrics.Add(new(Loc.S("Health_M_Power"), "N/D"));

        if (s.GpuTempMax is { } mt)
        {
            if (mt >= 90) { score -= 30; ctx.Problem(Loc.F("Health_P_GpuMaxTemp", $"{mt:0}"), ProblemSeverity.Alta); ctx.Recommend(Loc.S("Health_R_GpuCleanDust")); }
            else if (mt >= 83) { score -= 12; ctx.Problem(Loc.F("Health_P_GpuTempHigh", $"{mt:0}"), ProblemSeverity.Baixa); }
        }
        else if (s.GpuName is null)
        {
            // Sem GPU legível: não penaliza com nota máxima irreal; informa.
            c.Summary = Loc.S("Health_Summary_GpuSensorsUnavailable");
            score -= 0;
        }

        c.Score = Clamp(score);
        if (string.IsNullOrEmpty(c.Summary)) c.Summary = StateText(c.Score);
        return c;
    }

    private (string name, string vram) ReadGpuInfo(string? sensorName)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, AdapterRAM FROM Win32_VideoController");
            string? best = null;
            long bestRam = 0;
            foreach (ManagementObject mo in searcher.Get())
            {
                var name = mo["Name"]?.ToString();
                if (string.IsNullOrWhiteSpace(name)) continue;
                long ram = 0;
                try { ram = Convert.ToInt64(mo["AdapterRAM"] ?? 0L); } catch { }
                // AdapterRAM é um DWORD de 32 bits e estoura em GPUs com 4 GB+ de VRAM;
                // o registro guarda o valor real (qwMemorySize), sem esse limite.
                ulong vramFromRegistry = GpuVramReader.ReadVramBytes(name);
                if (vramFromRegistry > 0) ram = (long)vramFromRegistry;
                bool discrete = name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("GeForce", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("AMD", StringComparison.OrdinalIgnoreCase);
                if (best is null || discrete || ram > bestRam) { best = name.Trim(); bestRam = ram; }
            }
            string vram = bestRam > 0 ? $"{bestRam / (1024.0 * 1024 * 1024):0.#} GB" : "N/D";
            return (best ?? sensorName ?? "GPU", vram);
        }
        catch
        {
            return (sensorName ?? "GPU", "N/D");
        }
    }

    // ===================== RAM =====================

    private ComponentHealth AnalyzeRam(Context ctx, SensorSample s)
    {
        var c = new ComponentHealth { Key = "ram", Name = Loc.S("Dashboard_Ram"), Icon = "📦" };
        int score = 100;

        double standbyMb = ReadStandbyMemoryMb();

        c.Metrics.Add(new(Loc.S("Health_M_Installed"), s.RamTotalGb > 0 ? $"{s.RamTotalGb:0.0} GB" : "N/D"));
        c.Metrics.Add(new(Loc.S("Health_M_CurrentUsage"), $"{s.RamUsageAvg:0}% ({s.RamUsedGb:0.0} GB)"));
        c.Metrics.Add(new(Loc.S("Health_M_PeakUsage"), $"{s.RamUsageMax:0}%"));
        c.Metrics.Add(new(Loc.S("Health_M_MemoryPressure"), PressureText(s.RamUsageMax)));
        c.Metrics.Add(new(Loc.S("Health_M_StandbyMemory"), standbyMb > 0 ? $"{standbyMb / 1024:0.0} GB" : "N/D"));

        if (s.RamUsageMax >= 92) { score -= 25; ctx.Problem(Loc.F("Health_P_RamUsageVeryHigh", $"{s.RamUsageMax:0}"), ProblemSeverity.Alta); ctx.Recommend(Loc.S("Health_R_RamCloseApps")); }
        else if (s.RamUsageMax >= 85) { score -= 12; ctx.Problem(Loc.F("Health_P_RamUsageHigh", $"{s.RamUsageMax:0}"), ProblemSeverity.Moderada); ctx.Recommend(Loc.S("Health_R_RamOptimize")); }
        else if (s.RamUsageMax >= 75) { score -= 5; }
        if (s.RamTotalGb > 0 && s.RamTotalGb < 8) { score -= 10; ctx.Problem(Loc.F("Health_P_RamLowInstalled", $"{s.RamTotalGb:0.0}"), ProblemSeverity.Baixa); }

        c.Score = Clamp(score);
        c.Summary = StateText(c.Score);
        return c;
    }

    private static string PressureText(double maxUsage) => maxUsage switch
    {
        >= 90 => Loc.S("Health_PressureHigh"),
        >= 75 => Loc.S("Health_PressureModerate"),
        _ => Loc.S("Health_PressureLow"),
    };

    private double ReadStandbyMemoryMb()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\CIMV2",
                "SELECT StandbyCacheCoreBytes, StandbyCacheNormalPriorityBytes, StandbyCacheReserveBytes FROM Win32_PerfRawData_PerfOS_Memory");
            foreach (ManagementObject mo in searcher.Get())
            {
                double bytes = 0;
                foreach (var f in new[] { "StandbyCacheCoreBytes", "StandbyCacheNormalPriorityBytes", "StandbyCacheReserveBytes" })
                    if (mo[f] is { } v) bytes += Convert.ToDouble(v);
                return bytes / (1024 * 1024);
            }
        }
        catch { }
        return 0;
    }

    // ===================== Armazenamento (por disco físico, com SMART) =====================

    private List<ComponentHealth> AnalyzeStorage(Context ctx)
    {
        var result = new List<ComponentHealth>();
        var storageHealth = ReadStorageHealth();   // fonte principal (Storage API: SATA + NVMe)
        var smartFromWmi = ReadSmartFromWmi();      // fallback (provedor WMI legado)
        var volumes = SafeReadDisks();

        // Mapeia cada volume (letra) para o índice do disco físico que o hospeda, para podermos
        // mostrar o espaço livre de TODOS os discos — não só do disco do sistema (C:).
        var volumesByDiskIndex = MapVolumesToPhysicalDisks(volumes);
        var physicalDisks = ReadPhysicalDisks();

        // Índice do disco físico que REALMENTE contém o Windows — não supõe mais que é sempre
        // o índice 0 (que falhava em PCs onde o disco de boot não é o primeiro enumerado).
        int? systemDiskIndex = ResolveSystemDiskIndex();

        // Letra do volume do sistema (ex.: "C:") — só ele recebe penalidade de espaço livre na nota.
        string systemVolName = (Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\")
            .TrimEnd('\\', '/');

        if (physicalDisks.Count == 0)
        {
            // Sem WMI de disco físico: cai para os volumes lógicos.
            int i = 0;
            foreach (var v in volumes.Where(v => v.TypeText == "Interno"))
            {
                bool isSystem = i == 0;
                result.Add(BuildVolumeHealth(ctx, v, null, isSystem));
                i++;
            }
            if (result.Count == 0)
                result.Add(new ComponentHealth { Key = "disk", Name = Loc.S("Dashboard_InfoStorage"), Icon = "💽", Score = 80, Summary = Loc.S("Health_Summary_NoDiskData") });
            return result;
        }

        for (int idx = 0; idx < physicalDisks.Count; idx++)
        {
            var disk = physicalDisks[idx];
            // Se a associação WMI funcionou, usa o índice real do disco de boot; senão, cai de
            // volta na suposição antiga (índice 0) só como último recurso.
            bool isSystem = systemDiskIndex is { } sysIdx ? disk.Index == sysIdx : idx == 0;

            // Combina as três fontes (sem sobrescrever o que já temos): a Storage API do Windows
            // dá o veredito de saúde e o desgaste (funciona em SATA e NVMe); o acesso ATA direto
            // enriquece com setores realocados/pendentes e total gravado (SATA); o WMI legado é o
            // último recurso. Assim conseguimos exibir SMART em praticamente qualquer disco.
            // Correlaciona pela posição (Index) e, se não bater, pelo número de série — em alguns PCs
            // o DeviceId da Storage API não coincide com o Index do Win32_DiskDrive, e era isso que
            // deixava o segundo disco sem o veredito de saúde.
            SmartData? diskSmart = MatchStorageHealth(storageHealth, disk);
            // Prefere a classificação NVMe da Storage API (BusType=17, confiável) ao palpite pelo nome
            // do modelo — assim a leitura direta usa a via NVMe e não a ATA (que, em NVMe, pode retornar
            // dados traduzidos/lixo).
            bool isNvme = diskSmart?.MediaTypeText?.Contains("NVMe", StringComparison.OrdinalIgnoreCase) ?? disk.IsNvme;
            var fromAta = ReadSmartDirect(disk.Index, isNvme);
            if (fromAta is not null)
            {
                if (diskSmart is null) diskSmart = fromAta;
                else MergeSmart(diskSmart, fromAta);
            }
            var fromWmi = smartFromWmi.FirstOrDefault(sm => sm.Index == disk.Index);
            if (fromWmi is not null)
            {
                if (diskSmart is null) diskSmart = fromWmi;
                else MergeSmart(diskSmart, fromWmi);
            }
            if (diskSmart is not null && !diskSmart.HasAnyData) diskSmart = null;

            var c = new ComponentHealth
            {
                Key = isSystem ? "ssd_main" : $"disk_{idx}",
                Name = isSystem ? Loc.S("Health_MainDisk") : Loc.F("Health_DiskN", idx),
                Icon = "💽",
            };
            int score = 100;

            c.Metrics.Add(new(Loc.S("Health_M_Model"), disk.Model));
            // Prefere a classificação da Storage API (confiável); só cai no Win32 se faltar.
            c.Metrics.Add(new(Loc.S("Health_M_Type"), diskSmart?.MediaTypeText ?? disk.MediaType));
            c.Metrics.Add(new(Loc.S("Health_M_Capacity"), disk.SizeGb > 0 ? FormatGb(disk.SizeGb) : "N/D"));

            // Espaço livre: mostra cada volume hospedado por ESTE disco físico (não só o do
            // sistema) — um disco pode ter mais de uma partição com letra.
            if (!volumesByDiskIndex.TryGetValue(disk.Index, out var diskVolumes) || diskVolumes.Count == 0)
            {
                // Sem associação WMI para este disco: se for o disco do sistema, cai de volta no
                // palpite antigo (primeiro volume "C:") só como último recurso.
                if (isSystem)
                {
                    var fallback = volumes.FirstOrDefault(v => v.Name.StartsWith("C", StringComparison.OrdinalIgnoreCase));
                    if (fallback is not null) diskVolumes = new List<DiskReading> { fallback };
                }
            }

            if (diskVolumes is { Count: > 0 })
            {
                foreach (var vol in diskVolumes)
                {
                    double freePct = 100 - vol.UsagePercent;
                    string label = diskVolumes.Count > 1 ? Loc.F("Health_M_FreeSpaceOf", vol.Name) : Loc.S("Health_M_FreeSpace");
                    c.Metrics.Add(new(label, $"{vol.FreeGb:0.0} GB " + Loc.F("Health_OfN", $"{vol.TotalGb:0.0} GB") + $" ({freePct:0}%)"));

                    // Espaço livre NÃO é saúde de hardware — não derruba a nota do disco como antes
                    // (uma partição pequena cheia não significa SSD desgastado). Partições pequenas/
                    // especiais (≤ 64 GB: recuperação, drivers, EFI, ferramentas OEM) são esperadas
                    // cheias e por isso ignoradas aqui.
                    if (vol.TotalGb <= 64) continue;

                    bool isSystemVol = vol.Name.Equals(systemVolName, StringComparison.OrdinalIgnoreCase);
                    if (isSystemVol)
                    {
                        // Só o volume do sistema, e ainda assim de forma leve: um C: criticamente
                        // cheio é um risco real de desempenho, mas não condena a saúde do disco.
                        if (freePct < 8) { score -= 10; ctx.Problem(Loc.F("Health_P_VolumeLowFree", vol.Name, $"{freePct:0}"), ProblemSeverity.Alta); ctx.Recommend(Loc.F("Health_R_FreeUpVolume50", vol.Name)); }
                        else if (freePct < 15) { ctx.Problem(Loc.F("Health_P_VolumeLowFreeModerate", vol.Name, $"{freePct:0}"), ProblemSeverity.Moderada); ctx.Recommend(Loc.F("Health_R_FreeUpVolume", vol.Name)); }
                    }
                    else if (freePct < 8)
                    {
                        // Volume de dados quase cheio: apenas informa, sem penalizar a nota do disco.
                        ctx.Problem(Loc.F("Health_P_VolumeLowFreeModerate", vol.Name, $"{freePct:0}"), ProblemSeverity.Baixa);
                        ctx.Recommend(Loc.F("Health_R_FreeUpVolume", vol.Name));
                    }
                }
            }

            if (diskSmart is not null)
            {
                if (diskSmart.Temperature is { } temp)
                {
                    c.Metrics.Add(new(Loc.S("Health_M_Temperature"), $"{temp:0}°C"));
                    if (temp >= 60) { score -= 10; ctx.Problem(Loc.F("Health_P_DiskHot", disk.Model, $"{temp:0}"), ProblemSeverity.Moderada); }
                }

                if (diskSmart.PowerOnHours is { } hoursOn)
                {
                    c.Metrics.Add(new(Loc.S("Health_M_UsageHours"), $"{hoursOn:n0} h ({hoursOn / 24.0 / 365.0:0.0} " + Loc.S("Health_Years") + ")"));

                    // Quanto mais horas ligado, maior o desgaste mecânico/elétrico acumulado —
                    // a nota passa a refletir isso, não só os defeitos já manifestados no SMART.
                    if (hoursOn >= 50000) { score -= 20; ctx.Problem(Loc.F("Health_P_DiskOldHours", disk.Model, $"{hoursOn:n0}", $"{hoursOn / 8760.0:0.#}"), ProblemSeverity.Alta); ctx.Recommend(Loc.F("Health_R_DiskReplace", disk.Model)); }
                    else if (hoursOn >= 30000) { score -= 10; ctx.Problem(Loc.F("Health_P_DiskHoursModerate", disk.Model, $"{hoursOn:n0}", $"{hoursOn / 8760.0:0.#}"), ProblemSeverity.Baixa); }
                    else if (hoursOn >= 20000) { score -= 4; }
                }
                else
                {
                    // Explícito em vez de omitir a linha — evita parecer um bug quando, na
                    // verdade, é o driver/controlador deste disco que não expõe esse dado (em discos
                    // USB, a ponte do gabinete externo costuma bloquear o SMART detalhado).
                    c.Metrics.Add(new(Loc.S("Health_M_UsageHours"),
                        diskSmart.IsUsb ? Loc.S("Health_Unavailable_Usb") : Loc.S("Health_Unavailable_OnThisDisk")));
                }

                // Calcula uma PORCENTAGEM de saúde precisa (estilo CrystalDiskInfo), combinando o
                // veredito do firmware, o desgaste do SSD e os defeitos físicos acumulados.
                int healthPercent = ComputeDiskHealthPercent(diskSmart);
                string verdict = HealthVerdictLabel(diskSmart, healthPercent);
                bool verdictHealthy = healthPercent >= 70 && !diskSmart.PredictFailure && diskSmart.Health != DiskHealthStatus.Unhealthy;

                c.Metrics.Add(new(Loc.S("Health_M_Health"), $"{verdict} ({healthPercent}%)"));

                // Tabela de atributos SMART, no estilo CrystalDiskInfo: nome + valor + saudável?
                c.SmartAttributes.Add(new SmartAttributeRow
                {
                    Name = Loc.S("Health_Smart_OverallHealth"),
                    Value = $"{verdict} — {healthPercent}%",
                    IsHealthy = verdictHealthy,
                });
                if (diskSmart.ReallocatedSectors is not null)
                    c.SmartAttributes.Add(new SmartAttributeRow
                    {
                        Name = Loc.S("Health_Smart_ReallocatedSectors"),
                        Value = diskSmart.ReallocatedSectors.ToString()!,
                        IsHealthy = diskSmart.ReallocatedSectors == 0,
                    });
                if (diskSmart.PendingSectors is not null)
                    c.SmartAttributes.Add(new SmartAttributeRow
                    {
                        Name = Loc.S("Health_Smart_PendingSectors"),
                        Value = diskSmart.PendingSectors.ToString()!,
                        IsHealthy = diskSmart.PendingSectors == 0,
                    });
                if (diskSmart.UncorrectableErrors is not null)
                    c.SmartAttributes.Add(new SmartAttributeRow
                    {
                        Name = Loc.S("Health_Smart_UncorrectableErrors"),
                        Value = diskSmart.UncorrectableErrors.ToString()!,
                        IsHealthy = diskSmart.UncorrectableErrors == 0,
                    });
                if (diskSmart.PowerOnHours is { } h)
                    c.SmartAttributes.Add(new SmartAttributeRow { Name = Loc.S("Health_Smart_PowerOnHours"), Value = $"{h:n0} h", IsHealthy = true });
                if (diskSmart.TerabytesWritten is { } tb)
                    c.SmartAttributes.Add(new SmartAttributeRow { Name = Loc.S("Health_Smart_TotalWritten"), Value = $"{tb:0.0} TB", IsHealthy = true });
                if (diskSmart.LifeRemainingPercent is { } life)
                    c.SmartAttributes.Add(new SmartAttributeRow
                    {
                        Name = Loc.S("Health_Smart_LifeRemaining"),
                        Value = $"{life}%",
                        IsHealthy = life > 40,
                    });
                if (diskSmart.Temperature is { } dtemp)
                    c.SmartAttributes.Add(new SmartAttributeRow
                    {
                        Name = Loc.S("Health_Smart_TemperatureAttr"),
                        Value = $"{dtemp:0}°C",
                        IsHealthy = dtemp < 60,
                    });
                if (diskSmart.Warnings.Count > 0)
                    c.SmartAttributes.Add(new SmartAttributeRow { Name = Loc.S("Health_Smart_Warnings"), Value = string.Join("; ", diskSmart.Warnings), IsHealthy = false });

                // A nota do componente passa a ser a própria porcentagem de saúde do hardware,
                // depois descontado o impacto de pouco espaço livre (aplicado acima). Assim a
                // nota reflete de fato o estado do disco, sem "pegar leve".
                score = Math.Min(score, healthPercent);

                // Problemas/recomendações conforme a gravidade.
                if (diskSmart.PredictFailure || diskSmart.Health == DiskHealthStatus.Unhealthy)
                { ctx.Problem(Loc.F("Health_P_SmartFailure", disk.Model), ProblemSeverity.Critica); ctx.Recommend(Loc.F("Health_R_BackupNow", disk.Model)); }
                else if (diskSmart.Health == DiskHealthStatus.Warning)
                { ctx.Problem(Loc.F("Health_P_DiskWarningState", disk.Model), ProblemSeverity.Alta); ctx.Recommend(Loc.F("Health_R_MonitorDisk", disk.Model)); }

                if (diskSmart.ReallocatedSectors is { } realloc && realloc > 0)
                {
                    var sev = realloc >= 50 ? ProblemSeverity.Critica : realloc >= 8 ? ProblemSeverity.Alta : ProblemSeverity.Moderada;
                    ctx.Problem(Loc.F("Health_P_ReallocatedSectors", disk.Model, realloc), sev);
                    ctx.Recommend(Loc.F("Health_R_MonitorDiskClosely", disk.Model));
                }
                if (diskSmart.PendingSectors is { } pending && pending > 0)
                    ctx.Problem(Loc.F("Health_P_PendingSectors", disk.Model, pending), pending >= 20 ? ProblemSeverity.Critica : ProblemSeverity.Alta);
                if (diskSmart.UncorrectableErrors is { } unc && unc > 0)
                    ctx.Problem(Loc.F("Health_P_UncorrectableErrors", disk.Model, unc), ProblemSeverity.Alta);
                if (diskSmart.LifeRemainingPercent is { } lr)
                {
                    if (lr <= 5) { ctx.Problem(Loc.F("Health_P_SsdLifeCritical", disk.Model, lr), ProblemSeverity.Critica); ctx.Recommend(Loc.F("Health_R_SsdReplaceNow", disk.Model)); }
                    else if (lr <= 20) { ctx.Problem(Loc.F("Health_P_SsdLifeLow", disk.Model, lr), ProblemSeverity.Alta); ctx.Recommend(Loc.F("Health_R_SsdPlanReplace", disk.Model)); }
                    else if (lr <= 40) { ctx.Problem(Loc.F("Health_P_SsdLifeModerate", disk.Model, lr), ProblemSeverity.Baixa); }
                }
            }
            else
            {
                c.SmartAttributes.Add(new SmartAttributeRow
                {
                    Name = Loc.S("Health_Smart_SmartHealth"),
                    Value = disk.MediaType.Contains("USB", StringComparison.OrdinalIgnoreCase)
                        ? Loc.S("Health_Smart_UnavailableUsb")
                        : Loc.S("Health_Smart_UnavailableRaid"),
                    IsHealthy = true,
                });
            }

            c.Score = Clamp(score);
            c.Summary = diskSmart is not null
                ? $"{HealthVerdictLabel(diskSmart, ComputeDiskHealthPercent(diskSmart))} — {StateText(c.Score)}"
                : StateText(c.Score);
            result.Add(c);
        }

        return result;
    }

    private ComponentHealth BuildVolumeHealth(Context ctx, DiskReading v, SmartData? smart, bool isSystem)
    {
        var c = new ComponentHealth
        {
            Key = isSystem ? "ssd_main" : $"disk_{v.Name}",
            Name = isSystem ? Loc.S("Health_MainDisk") : Loc.F("Health_DiskName", v.Name),
            Icon = "💽",
        };
        int score = 100;
        double freePct = 100 - v.UsagePercent;

        c.Metrics.Add(new(Loc.S("Health_M_Drive"), $"{v.Name} ({v.Label})"));
        c.Metrics.Add(new(Loc.S("Health_M_Capacity"), $"{v.TotalGb:0.0} GB"));
        c.Metrics.Add(new(Loc.S("Health_M_FreeSpace"), $"{v.FreeGb:0.0} GB ({freePct:0}%)"));

        if (freePct < 8) { score -= 25; ctx.Problem(Loc.F("Health_P_VolumeLowFree", v.Name, $"{freePct:0}"), ProblemSeverity.Alta); ctx.Recommend(Loc.F("Health_R_FreeUpVolume50Plain", v.Name)); }
        else if (freePct < 15) { score -= 12; ctx.Problem(Loc.F("Health_P_VolumeLowFreeModerate", v.Name, $"{freePct:0}"), ProblemSeverity.Moderada); }

        c.Score = Clamp(score);
        c.Summary = StateText(c.Score);
        return c;
    }

    // Mapeia CADA volume com letra (não só o do sistema) para o índice do disco físico que o
    // hospeda, usando a mesma cadeia de associação do WMI: LogicalDisk → Partition → DiskDrive.
    // Isso permite mostrar o espaço livre de discos secundários no Diagnóstico Inteligente.
    private static Dictionary<int, List<DiskReading>> MapVolumesToPhysicalDisks(IReadOnlyList<DiskReading> volumes)
    {
        var map = new Dictionary<int, List<DiskReading>>();
        foreach (var v in volumes)
        {
            try
            {
                string driveLetter = v.Name.TrimEnd('\\') + (v.Name.EndsWith(":") ? "" : ":");

                using var partitionSearcher = new ManagementObjectSearcher(
                    $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{driveLetter}'}} WHERE AssocClass=Win32_LogicalDiskToPartition");

                foreach (ManagementObject partition in partitionSearcher.Get())
                {
                    string? partitionId = partition["DeviceID"]?.ToString();
                    if (string.IsNullOrEmpty(partitionId)) continue;

                    using var diskSearcher = new ManagementObjectSearcher(
                        $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partitionId}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition");

                    foreach (ManagementObject disk in diskSearcher.Get())
                    {
                        if (disk["Index"] is not { } idxObj) continue;
                        int idx = Convert.ToInt32(idxObj);
                        if (!map.TryGetValue(idx, out var list)) { list = new List<DiskReading>(); map[idx] = list; }
                        list.Add(v);
                    }
                }
            }
            catch { /* associação indisponível para este volume — ele simplesmente não aparece */ }
        }
        return map;
    }

    // Descobre qual disco físico hospeda de fato o volume do Windows (C:), em vez de supor que
    // é sempre o índice 0 — em PCs com mais de um disco, o Windows pode enumerar o disco de
    // dados ou um disco externo antes do disco de boot, então essa suposição estava errada em
    // várias máquinas. Usa a cadeia de associação do WMI: LogicalDisk → Partition → DiskDrive.
    private static int? ResolveSystemDiskIndex()
    {
        try
        {
            string sysRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string driveLetter = Path.GetPathRoot(sysRoot)?.TrimEnd('\\') ?? "C:";

            using var partitionSearcher = new ManagementObjectSearcher(
                $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{driveLetter}'}} WHERE AssocClass=Win32_LogicalDiskToPartition");

            foreach (ManagementObject partition in partitionSearcher.Get())
            {
                string? partitionId = partition["DeviceID"]?.ToString();
                if (string.IsNullOrEmpty(partitionId)) continue;

                using var diskSearcher = new ManagementObjectSearcher(
                    $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partitionId}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition");

                foreach (ManagementObject disk in diskSearcher.Get())
                {
                    if (disk["Index"] is { } idx) return Convert.ToInt32(idx);
                }
            }
        }
        catch { /* associação indisponível — quem chama cai de volta no índice 0 */ }

        return null;
    }

    // Porcentagem de saúde do disco (0-100), no espírito do "Health Status %" do CrystalDiskInfo:
    // parte de 100 e desconta conforme defeitos físicos irreversíveis e desgaste do SSD; o veredito
    // do firmware (falha prevista / Unhealthy) impõe um teto baixo.
    private static int ComputeDiskHealthPercent(SmartData s)
    {
        double health = 100;

        // Desgaste do SSD (se conhecido) define o piso natural de "vida restante".
        if (s.LifeRemainingPercent is { } life)
            health = Math.Min(health, life);

        // Defeitos físicos — irreversíveis, descontam forte e de forma crescente.
        long realloc = s.ReallocatedSectors ?? 0;
        if (realloc > 0) health -= Math.Min(60, 10 + realloc * 1.5);

        long pending = s.PendingSectors ?? 0;
        if (pending > 0) health -= Math.Min(55, 15 + pending * 2.0);

        long unc = s.UncorrectableErrors ?? 0;
        if (unc > 0) health -= Math.Min(50, 15 + unc * 2.0);

        // Temperatura muito alta é um sinal de risco (não permanente).
        if (s.Temperature is { } t && t >= 65) health -= 8;

        // Veredito do próprio firmware tem a palavra final.
        if (s.Health == DiskHealthStatus.Warning) health = Math.Min(health, 55);
        if (s.PredictFailure || s.Health == DiskHealthStatus.Unhealthy) health = Math.Min(health, 15);

        return (int)Math.Clamp(Math.Round(health), 0, 100);
    }

    private static string HealthVerdictLabel(SmartData s, int healthPercent)
    {
        if (s.PredictFailure || s.Health == DiskHealthStatus.Unhealthy) return Loc.S("Health_Verdict_Bad");
        if (s.Health == DiskHealthStatus.Warning || healthPercent < 70) return Loc.S("Health_Verdict_Warning");
        if (healthPercent < 90) return Loc.S("Health_Verdict_Good");
        return Loc.S("Health_Verdict_Excellent");
    }

    private IReadOnlyList<DiskReading> SafeReadDisks()
    {
        try { return _metrics.ReadDisks(); }
        catch { return Array.Empty<DiskReading>(); }
    }

    private sealed record PhysicalDisk(int Index, string Model, string MediaType, double SizeGb, bool IsNvme, string? Serial);

    private List<PhysicalDisk> ReadPhysicalDisks()
    {
        var list = new List<PhysicalDisk>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Index, Model, Size, MediaType, InterfaceType, SerialNumber FROM Win32_DiskDrive");
            foreach (ManagementObject mo in searcher.Get())
            {
                int index = mo["Index"] is { } i ? Convert.ToInt32(i) : -1;
                string model = mo["Model"]?.ToString()?.Trim() ?? Loc.S("Health_GenericDisk");
                double sizeGb = 0;
                try { sizeGb = Convert.ToDouble(mo["Size"] ?? 0) / (1024.0 * 1024 * 1024); } catch { }
                string media = mo["MediaType"]?.ToString() ?? "";
                string iface = mo["InterfaceType"]?.ToString() ?? "";
                // "MediaType" do WMI raramente distingue SSD/HDD; usa heurística pelo modelo.
                string type = ClassifyDisk(model, media, iface);
                bool isNvme = type.Contains("NVMe", StringComparison.OrdinalIgnoreCase);
                list.Add(new PhysicalDisk(index, model, type, sizeGb, isNvme, NormalizeSerial(mo["SerialNumber"]?.ToString())));
            }
        }
        catch { }
        return list.OrderBy(d => d.Index).ToList();
    }

    // Normaliza o número de série para comparação: o Win32_DiskDrive às vezes devolve o serial
    // codificado em hex/com espaços, então removemos espaços e zeros à esquerda e comparamos sem
    // diferenciar maiúsculas — só para correlacionar a mesma unidade entre duas fontes do WMI.
    private static string? NormalizeSerial(string? serial)
    {
        if (string.IsNullOrWhiteSpace(serial)) return null;
        string s = new string(serial.Where(char.IsLetterOrDigit).ToArray()).TrimStart('0');
        return s.Length == 0 ? null : s.ToLowerInvariant();
    }

    private static string ClassifyDisk(string model, string media, string iface)
    {
        string m = model.ToLowerInvariant();
        if (m.Contains("nvme") || iface.Contains("NVMe", StringComparison.OrdinalIgnoreCase)) return "SSD NVMe";
        if (m.Contains("ssd")) return "SSD";
        if (media.Contains("SSD", StringComparison.OrdinalIgnoreCase)) return "SSD";
        if (media.Contains("Fixed", StringComparison.OrdinalIgnoreCase)) return "HDD";
        return string.IsNullOrWhiteSpace(media) ? Loc.S("Health_GenericDisk") : media;
    }

    // --- SMART ---

    private enum DiskHealthStatus { Unknown, Healthy, Warning, Unhealthy }

    private sealed class SmartData
    {
        public int Index;
        public string? Serial;          // p/ correlacionar com Win32_DiskDrive quando o índice não bate
        public bool IsUsb;              // disco externo USB: o SMART detalhado costuma ser bloqueado pela ponte USB
        public bool PredictFailure;
        public DiskHealthStatus Health = DiskHealthStatus.Unknown;
        public double? Temperature;
        public long? PowerOnHours;
        public double? TerabytesWritten;
        public int? LifeRemainingPercent;
        public long? ReallocatedSectors;
        public long? PendingSectors;
        public long? UncorrectableErrors;
        public string? MediaTypeText;   // ex.: "SSD NVMe", "SSD", "HDD" — vindo da Storage API
        public List<string> Warnings = new();

        /// <summary>True se conseguimos QUALQUER dado real de saúde (veredito, desgaste,
        /// setores...) — usado para decidir se mostramos a tabela ou o aviso de indisponível.</summary>
        public bool HasAnyData =>
            Health != DiskHealthStatus.Unknown || PredictFailure || Temperature is not null ||
            PowerOnHours is not null || LifeRemainingPercent is not null ||
            ReallocatedSectors is not null || PendingSectors is not null ||
            TerabytesWritten is not null || UncorrectableErrors is not null;
    }

    // Funde dados de uma fonte secundária na principal, sem sobrescrever o que já existe.
    private static void MergeSmart(SmartData into, SmartData from)
    {
        into.PredictFailure |= from.PredictFailure;
        if (into.Health == DiskHealthStatus.Unknown) into.Health = from.Health;
        into.Temperature ??= from.Temperature;
        into.PowerOnHours ??= from.PowerOnHours;
        into.TerabytesWritten ??= from.TerabytesWritten;
        into.LifeRemainingPercent ??= from.LifeRemainingPercent;
        into.ReallocatedSectors ??= from.ReallocatedSectors;
        into.PendingSectors ??= from.PendingSectors;
        into.UncorrectableErrors ??= from.UncorrectableErrors;
        into.MediaTypeText ??= from.MediaTypeText;
        foreach (var w in from.Warnings)
            if (!into.Warnings.Contains(w)) into.Warnings.Add(w);
    }

    // Fonte PRINCIPAL de saúde: a API de Armazenamento do Windows (root\Microsoft\Windows\Storage).
    // MSFT_PhysicalDisk.HealthStatus + MSFT_StorageReliabilityCounter funcionam através da pilha de
    // armazenamento do Windows — portanto valem tanto para SATA quanto para NVMe, e são o que o
    // PowerShell (Get-PhysicalDisk / Get-StorageReliabilityCounter) e várias ferramentas usam.
    // É a forma mais confiável de obter o veredito de saúde e o desgaste do disco.
    private Dictionary<int, SmartData> ReadStorageHealth()
    {
        var map = new Dictionary<int, SmartData>();
        try
        {
            var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
            scope.Connect();

            using var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT DeviceId, HealthStatus, MediaType, BusType, SerialNumber FROM MSFT_PhysicalDisk"));

            foreach (ManagementObject disk in searcher.Get())
            {
                if (!int.TryParse(disk["DeviceId"]?.ToString(), out int devId)) continue;

                var data = new SmartData { Index = devId, Serial = NormalizeSerial(disk["SerialNumber"]?.ToString()) };
                if (disk["HealthStatus"] is { } hs)
                {
                    int h = Convert.ToInt32(hs);
                    data.Health = h switch
                    {
                        0 => DiskHealthStatus.Healthy,
                        1 => DiskHealthStatus.Warning,
                        2 => DiskHealthStatus.Unhealthy,
                        _ => DiskHealthStatus.Unknown,
                    };
                    data.PredictFailure = h == 2;
                }

                // Classificação confiável do tipo de disco (o Win32_DiskDrive mente: reporta NVMe
                // como "SCSI"). MediaType: 3=HDD, 4=SSD, 5=SCM. BusType: 17=NVMe, 11=SATA, 7=USB.
                int mediaType = disk["MediaType"] is { } mt ? Convert.ToInt32(mt) : 0;
                int busType = disk["BusType"] is { } bt ? Convert.ToInt32(bt) : 0;
                data.IsUsb = busType == 7;
                data.MediaTypeText = (mediaType, busType) switch
                {
                    (4, 17) => "SSD NVMe",
                    (4, _) => "SSD",
                    (3, _) => "HDD",
                    (_, 17) => "SSD NVMe",
                    (_, 7) => Loc.S("Dashboard_DiskUsb"),
                    _ => null,
                };

                // Contadores de confiabilidade (associação MSFT_PhysicalDiskToStorageReliabilityCounter).
                try
                {
                    foreach (ManagementBaseObject rc in disk.GetRelated("MSFT_StorageReliabilityCounter"))
                    {
                        if (rc["Wear"] is { } wear)
                        {
                            int w = Convert.ToInt32(wear);
                            if (w is >= 0 and <= 100) data.LifeRemainingPercent = 100 - w;
                        }
                        if (rc["PowerOnHours"] is { } poh)
                        {
                            long h = Convert.ToInt64(poh);
                            if (h > 0) data.PowerOnHours = h;
                        }
                        if (rc["Temperature"] is { } t)
                        {
                            double c = Convert.ToDouble(t);
                            if (c is > 0 and < 125) data.Temperature = c;
                        }
                        if (rc["ReadErrorsUncorrected"] is { } re)
                        {
                            long v = Convert.ToInt64(re);
                            if (v > 0) { data.UncorrectableErrors = (data.UncorrectableErrors ?? 0) + v; data.Warnings.Add(Loc.F("Health_Warn_ReadErrors", v)); }
                        }
                        if (rc["WriteErrorsUncorrected"] is { } we)
                        {
                            long v = Convert.ToInt64(we);
                            if (v > 0) { data.UncorrectableErrors = (data.UncorrectableErrors ?? 0) + v; data.Warnings.Add(Loc.F("Health_Warn_WriteErrors", v)); }
                        }
                    }
                }
                catch { /* contador indisponível — o HealthStatus acima já é suficiente */ }

                map[devId] = data;
            }
        }
        catch { /* namespace de Storage indisponível (Windows muito antigo) — usamos os outros métodos */ }

        return map;
    }

    // Encontra os dados da Storage API para um disco físico: primeiro pela posição (DeviceId == Index),
    // que é o caso comum; se não houver, pelo número de série (cobre os PCs onde os dois números
    // divergem). Retorna uma CÓPIA para não mutar o objeto compartilhado do mapa ao fundir as fontes.
    private static SmartData? MatchStorageHealth(Dictionary<int, SmartData> storageHealth, PhysicalDisk disk)
    {
        SmartData? found = storageHealth.TryGetValue(disk.Index, out var byIndex) ? byIndex : null;
        if (found is null && disk.Serial is not null)
            found = storageHealth.Values.FirstOrDefault(d => d.Serial is not null && d.Serial == disk.Serial);
        if (found is null) return null;

        // Clona para que o MergeSmart subsequente não altere a entrada original do mapa.
        return new SmartData
        {
            Index = disk.Index,
            Serial = found.Serial,
            IsUsb = found.IsUsb,
            PredictFailure = found.PredictFailure,
            Health = found.Health,
            Temperature = found.Temperature,
            PowerOnHours = found.PowerOnHours,
            TerabytesWritten = found.TerabytesWritten,
            LifeRemainingPercent = found.LifeRemainingPercent,
            ReallocatedSectors = found.ReallocatedSectors,
            PendingSectors = found.PendingSectors,
            UncorrectableErrors = found.UncorrectableErrors,
            MediaTypeText = found.MediaTypeText,
            Warnings = new List<string>(found.Warnings),
        };
    }

    // Lê SMART diretamente do disco via IOCTL — mesmo mecanismo de baixo nível usado por
    // ferramentas como o CrystalDiskInfo. IMPORTANTE: NÃO confiamos no palpite "isNvme" (o
    // Win32_DiskDrive reporta a interface de discos NVMe como "SCSI", então a detecção falhava
    // e mandava discos NVMe pela via ATA, que não os entende — por isso as horas vinham vazias).
    // Em vez disso, tentamos AS DUAS vias de leitura: a que casar com o disco responde, a outra
    // falha de forma inofensiva. O "isNvme" serve apenas para decidir qual tentar primeiro.
    private SmartData? ReadSmartDirect(int index, bool isNvme)
    {
        try
        {
            SmartData? nvme = ReadNvmeDirect(index);
            SmartData? ata = ReadAtaDirect(index);

            // Disco atrás de ponte USB-SATA: o IOCTL_ATA_PASS_THROUGH comum não é encaminhado pelo
            // driver USB, então tentamos a via SCSI/SAT (como o CrystalDiskInfo). Só quando a ATA
            // direta não trouxe nada, para não abrir o disco à toa em SATA/NVMe internos.
            if (ata is null) ata = ReadAtaViaScsi(index);

            // Prefere a fonte que trouxe mais dados; funde a outra para completar lacunas.
            SmartData? primary = isNvme ? (nvme ?? ata) : (ata ?? nvme);
            SmartData? secondary = ReferenceEquals(primary, nvme) ? ata : nvme;
            if (primary is null) primary = secondary;
            else if (secondary is not null) MergeSmart(primary, secondary);

            if (primary is not null && primary.HasAnyData) return primary;

            // Nenhuma via detalhada respondeu: ao menos o veredito de falha prevista.
            if (SmartReader.TryReadGenericPredictFailure(index, out bool predict))
                return new SmartData { Index = index, PredictFailure = predict };

            return primary;
        }
        catch
        {
            // Acesso ao disco bruto pode falhar (RAID, virtualização, permissões) — cai no
            // fallback de WMI sem interromper o diagnóstico.
            return null;
        }
    }

    // Lê a página de log SMART/Health do NVMe pelas duas vias possíveis e funde os campos.
    private SmartData? ReadNvmeDirect(int index)
    {
        bool got1 = SmartReader.TryReadNvmeHealth(index, out var nvme1);
        bool got2 = SmartReader.TryReadNvmeHealthViaPredictFailure(index, out var nvme2);
        if (!got1 && !got2) return null;

        var nvme = got1 ? nvme1 : nvme2;
        if (got1 && got2)
        {
            if (nvme.PowerOnHours <= 0) nvme.PowerOnHours = nvme2.PowerOnHours;
            if (nvme.TerabytesWritten <= 0) nvme.TerabytesWritten = nvme2.TerabytesWritten;
            if (nvme.TemperatureCelsius <= 0) nvme.TemperatureCelsius = nvme2.TemperatureCelsius;
            if (nvme.PercentageUsed <= 0) nvme.PercentageUsed = nvme2.PercentageUsed;
            if (nvme.MediaErrors <= 0) nvme.MediaErrors = nvme2.MediaErrors;
            nvme.CriticalWarning |= nvme2.CriticalWarning;
        }

        var data = new SmartData
        {
            Index = index,
            PredictFailure = nvme.CriticalWarning,
            Temperature = nvme.TemperatureCelsius is > 0 and < 125 ? nvme.TemperatureCelsius : null,
            PowerOnHours = nvme.PowerOnHours > 0 ? nvme.PowerOnHours : null,
            TerabytesWritten = nvme.TerabytesWritten > 0 ? nvme.TerabytesWritten : null,
            LifeRemainingPercent = nvme.PercentageUsed > 0 ? Math.Clamp(100 - nvme.PercentageUsed, 0, 100) : null,
            UncorrectableErrors = nvme.MediaErrors > 0 ? nvme.MediaErrors : null,
        };
        if (nvme.AvailableSparePercent is > 0 and < 100)
            data.Warnings.Add(Loc.F("Health_Warn_AvailableSpare", nvme.AvailableSparePercent));
        return data.HasAnyData ? data : null;
    }

    // Lê a tabela de atributos SMART de um disco ATA/SATA.
    private SmartData? ReadAtaDirect(int index)
    {
        if (!SmartReader.TryReadAtaSmart(index, out byte[] table, out bool predictFailure))
            return null;

        var data = new SmartData { Index = index, PredictFailure = predictFailure };
        ParseSmartAttributes(table, data);
        return data.HasAnyData ? data : null;
    }

    // Lê a tabela de atributos SMART via SCSI/SAT — usado para discos atrás de pontes USB-SATA.
    // O layout de 512 bytes é o mesmo do SMART READ DATA, então reusamos o mesmo parser.
    private SmartData? ReadAtaViaScsi(int index)
    {
        if (!SmartReader.TryReadAtaSmartViaScsi(index, out byte[] table))
            return null;

        var data = new SmartData { Index = index };
        ParseSmartAttributes(table, data);
        return data.HasAnyData ? data : null;
    }

    private List<SmartData> ReadSmartFromWmi()
    {
        var map = new Dictionary<int, SmartData>();

        // Predição de falha (booleano oficial do driver).
        try
        {
            using var statusSearcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM MSStorageDriver_FailurePredictStatus");
            foreach (ManagementObject mo in statusSearcher.Get())
            {
                int idx = ExtractDiskIndex(mo["InstanceName"]?.ToString());
                var data = Get(map, idx);
                data.PredictFailure = mo["PredictFailure"] is bool b && b;
            }
        }
        catch { }

        // Atributos crus (vendor specific) → parse dos atributos comuns.
        try
        {
            using var dataSearcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM MSStorageDriver_FailurePredictData");
            foreach (ManagementObject mo in dataSearcher.Get())
            {
                int idx = ExtractDiskIndex(mo["InstanceName"]?.ToString());
                if (mo["VendorSpecific"] is byte[] raw)
                    ParseSmartAttributes(raw, Get(map, idx));
            }
        }
        catch { }

        return map.Values.ToList();
    }

    private static SmartData Get(Dictionary<int, SmartData> map, int idx)
    {
        if (!map.TryGetValue(idx, out var d)) { d = new SmartData { Index = idx }; map[idx] = d; }
        return d;
    }

    // O InstanceName tende a terminar com "_0", "_1"... correspondendo ao índice do disco.
    private static int ExtractDiskIndex(string? instanceName)
    {
        if (string.IsNullOrEmpty(instanceName)) return 0;
        var trimmed = instanceName.TrimEnd('_', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        var suffix = instanceName.Substring(trimmed.Length).TrimStart('_');
        return int.TryParse(suffix, out var i) ? i : 0;
    }

    // VendorSpecific: 2 bytes de cabeçalho + 30 atributos de 12 bytes cada.
    // Atributo: [0]=id [1-2]=flags [3]=valor [4]=pior [5-10]=raw(48 bits) [11]=reservado.
    private static void ParseSmartAttributes(byte[] raw, SmartData data)
    {
        if (raw.Length < 2 + 12) return;
        for (int offset = 2; offset + 12 <= raw.Length; offset += 12)
        {
            int id = raw[offset];
            if (id == 0) continue;
            int value = raw[offset + 3];
            long rawValue = 0;
            for (int b = 0; b < 6; b++)
                rawValue |= (long)raw[offset + 5 + b] << (8 * b);

            switch (id)
            {
                case 0x05: // Reallocated Sectors Count
                    data.ReallocatedSectors = rawValue;
                    if (rawValue > 0) data.Warnings.Add(Loc.F("Health_Warn_ReallocatedSectors", rawValue));
                    break;
                case 0x09: // Power-On Hours
                    data.PowerOnHours = rawValue & 0xFFFFFFFF;
                    break;
                case 0xC2: // Temperature
                    int temp = (int)(rawValue & 0xFF);
                    if (temp is > 0 and < 125) data.Temperature = temp;
                    break;
                case 0xC5: // Current Pending Sector Count
                    data.PendingSectors = rawValue;
                    if (rawValue > 0) data.Warnings.Add(Loc.F("Health_Warn_PendingSectors", rawValue));
                    break;
                case 0xC6: // Uncorrectable Sector Count
                    data.UncorrectableErrors = rawValue;
                    if (rawValue > 0) data.Warnings.Add(Loc.F("Health_Warn_UncorrectableSectors", rawValue));
                    break;
                case 0xE7: // SSD Life Left / Remaining Life (value = %)
                case 0xE9: // Media Wearout Indicator
                    if (value is > 0 and <= 100) data.LifeRemainingPercent = value;
                    break;
                case 0xF1: // Total LBAs Written
                case 0xF2:
                    // 1 LBA = 512 bytes → TB = raw * 512 / 1e12
                    double tb = rawValue * 512.0 / 1_000_000_000_000.0;
                    if (tb is > 0 and < 100000) data.TerabytesWritten = tb;
                    break;
            }
        }
    }

    // ===================== Sistema =====================

    private ComponentHealth AnalyzeSystem(Context ctx)
    {
        var c = new ComponentHealth { Key = "system", Name = Loc.S("Dashboard_InfoSystem"), Icon = "🖥️" };
        int score = 100;

        int startupCount = ReadStartupProgramCount();
        int processCount = SafeProcessCount();
        int problemDevices = ReadProblemDeviceCount();
        bool rebootPending = IsRebootPending();
        var info = SafeStaticInfo();
        var drivers = ReadDriverFreshness();

        c.Metrics.Add(new(Loc.S("Dashboard_InfoWindows"), info?.WindowsVersion ?? "N/D"));
        c.Metrics.Add(new(Loc.S("Dashboard_InfoUptime"), _systemInfo.GetUptimeText()));
        c.Metrics.Add(new(Loc.S("Health_M_StartupPrograms"), startupCount >= 0 ? startupCount.ToString() : "N/D"));
        c.Metrics.Add(new(Loc.S("Health_M_BackgroundProcesses"), processCount > 0 ? processCount.ToString() : "N/D"));
        c.Metrics.Add(new(Loc.S("Health_M_PendingUpdates"), rebootPending ? Loc.S("Health_RebootPending") : Loc.S("Health_NoneDetected")));
        c.Metrics.Add(new(Loc.S("Health_M_WindowsIntegrity"), Loc.S("Health_NotCheckedRunSfc")));
        c.Metrics.Add(new(Loc.S("Health_M_ProblemDevices"), problemDevices >= 0 ? problemDevices.ToString() : "N/D"));
        c.Metrics.Add(new(Loc.S("Health_M_DriversStatus"), drivers.Checked > 0
            ? drivers.Outdated > 0
                ? Loc.F("Health_Drivers_SomeOutdated", drivers.Outdated, drivers.Checked, drivers.OldestName, drivers.OldestYears)
                : Loc.F("Health_Drivers_AllUpdated", drivers.Checked)
            : Loc.S("Health_Drivers_Unavailable")));

        if (startupCount > 12) { score -= 18; ctx.Problem(Loc.F("Health_P_StartupMany", startupCount), ProblemSeverity.Moderada); ctx.Recommend(Loc.S("Health_R_DisableStartup")); }
        else if (startupCount > 7) { score -= 8; ctx.Problem(Loc.F("Health_P_StartupMany", startupCount), ProblemSeverity.Baixa); ctx.Recommend(Loc.S("Health_R_ReviewStartup")); }

        if (problemDevices > 0) { score -= 12; ctx.Problem(Loc.F("Health_P_ProblemDevices", problemDevices), ProblemSeverity.Moderada); ctx.Recommend(Loc.S("Health_R_CheckDeviceManager")); }

        if (drivers.Outdated >= 3) { score -= 12; ctx.Problem(Loc.F("Health_P_DriversOutdatedMany", drivers.Outdated, drivers.OldestName, drivers.OldestYears), ProblemSeverity.Moderada); ctx.Recommend(Loc.S("Health_R_UpdateDrivers")); }
        else if (drivers.Outdated > 0) { score -= 5; ctx.Problem(Loc.F("Health_P_DriversOutdatedFew", drivers.Outdated), ProblemSeverity.Baixa); ctx.Recommend(Loc.S("Health_R_UpdateDrivers")); }

        if (rebootPending) { score -= 8; ctx.Problem(Loc.S("Health_P_RebootPending"), ProblemSeverity.Baixa); ctx.Recommend(Loc.S("Health_R_RebootSoon")); }

        // Tempo ligado muito longo costuma acumular vazamentos de memória / lentidão.
        if (Environment.TickCount64 > TimeSpan.FromDays(7).TotalMilliseconds) { score -= 6; ctx.Recommend(Loc.S("Health_R_RebootLongUptime")); }

        c.Score = Clamp(score);
        c.Summary = StateText(c.Score);
        return c;
    }

    private int ReadStartupProgramCount()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Caption FROM Win32_StartupCommand");
            return searcher.Get().Count;
        }
        catch { return -1; }
    }

    private int ReadProblemDeviceCount()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT ConfigManagerErrorCode FROM Win32_PnPEntity WHERE ConfigManagerErrorCode > 0");
            return searcher.Get().Count;
        }
        catch { return -1; }
    }

    private sealed record DriverFreshness(int Checked, int Outdated, string? OldestName, double? OldestYears);

    // Verifica a idade dos drivers das categorias de hardware que mais costumam exigir
    // atualização do fabricante (vídeo, rede, áudio, controladores de armazenamento) — driver
    // com mais de 2 anos é considerado desatualizado. Ignora classes genéricas/inbox do Windows
    // (mouse, teclado, impressora etc.) para não gerar ruído com drivers que o sistema já mantém.
    private DriverFreshness ReadDriverFreshness()
    {
        try
        {
            string[] classes = { "DISPLAY", "NET", "MEDIA", "HDC", "SCSIADAPTER" };
            string classFilter = string.Join(" OR ", classes.Select(cl => $"DeviceClass = '{cl}'"));

            using var searcher = new ManagementObjectSearcher(
                $"SELECT DeviceName, DriverDate FROM Win32_PnPSignedDriver WHERE {classFilter}");

            int checkedCount = 0, outdated = 0;
            string? oldestName = null;
            DateTime oldestDate = DateTime.MaxValue;

            foreach (ManagementObject mo in searcher.Get())
            {
                string name = mo["DeviceName"] as string ?? "";
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (mo["DriverDate"] is not string raw || raw.Length < 8) continue;
                if (!DateTime.TryParseExact(raw.Substring(0, 8), "yyyyMMdd",
                    null, System.Globalization.DateTimeStyles.None, out var date)) continue;

                checkedCount++;
                if (date < oldestDate) { oldestDate = date; oldestName = name; }
                if ((DateTime.Now - date).TotalDays > 730) outdated++;
            }

            double? oldestYears = checkedCount > 0 ? (DateTime.Now - oldestDate).TotalDays / 365.0 : null;
            return new DriverFreshness(checkedCount, outdated, oldestName, oldestYears);
        }
        catch
        {
            return new DriverFreshness(0, 0, null, null);
        }
    }

    private static bool IsRebootPending()
    {
        try
        {
            using var cbs = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
            if (cbs is not null) return true;
            using var wu = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired");
            if (wu is not null) return true;
        }
        catch { }
        return false;
    }

    private StaticSystemInfo? SafeStaticInfo()
    {
        try { return _systemInfo.GetStaticInfo(); }
        catch { return null; }
    }

    // ===================== Rede =====================

    private ComponentHealth AnalyzeNetwork(Context ctx)
    {
        var c = new ComponentHealth { Key = "network", Name = Loc.S("Dashboard_Network"), Icon = "🌐" };
        int score = 100;

        var active = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                        && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                        && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
            .ToList();

        var primary = active.FirstOrDefault(n =>
            n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork));

        c.Metrics.Add(new(Loc.S("Health_M_ActiveAdapters"), active.Count.ToString()));
        c.Metrics.Add(new(Loc.S("Health_M_MainAdapter"), primary?.Name ?? Loc.S("Health_None")));
        c.Metrics.Add(new(Loc.S("Health_M_LinkSpeed"), primary is not null ? $"{primary.Speed / 1_000_000} Mbps" : "N/D"));

        if (primary is not null)
        {
            var props = primary.GetIPProperties();
            var dns = props.DnsAddresses.FirstOrDefault(d => d.AddressFamily == AddressFamily.InterNetwork);
            c.Metrics.Add(new(Loc.S("Health_M_Configuration"), dns is not null ? $"DNS {dns}" : Loc.S("Health_Automatic")));
        }
        else c.Metrics.Add(new(Loc.S("Health_M_Configuration"), Loc.S("Health_NoConnection")));

        long? latency = MeasureLatency(primary);
        c.Metrics.Add(new(Loc.S("Health_M_Latency"), latency.HasValue ? $"{latency.Value} ms" : "N/D"));

        if (primary is null) { score -= 40; ctx.Problem(Loc.S("Health_P_NoNetwork"), ProblemSeverity.Moderada); }
        else
        {
            if (latency is { } latMs)
            {
                if (latMs > 150) { score -= 18; ctx.Problem(Loc.F("Health_P_HighLatency", latMs), ProblemSeverity.Moderada); ctx.Recommend(Loc.S("Health_R_GetCloserRouter")); }
                else if (latMs > 80) { score -= 8; }
            }
            if (primary.Speed is > 0 and < 100_000_000) { score -= 6; }
        }

        c.Score = Clamp(score);
        c.Summary = StateText(c.Score);
        return c;
    }

    private static long? MeasureLatency(NetworkInterface? primary)
    {
        try
        {
            using var ping = new Ping();
            var reply = ping.Send("8.8.8.8", 1500);
            if (reply.Status == IPStatus.Success) return reply.RoundtripTime;
        }
        catch { }
        return null;
    }

    // ===================== Temperaturas =====================

    private ComponentHealth AnalyzeTemperatures(Context ctx, SensorSample s)
    {
        var c = new ComponentHealth { Key = "temps", Name = Loc.S("Health_Temperatures"), Icon = "🌡️" };
        int score = 100;

        var readings = new List<(string Component, double Max, double? Avg)>();
        if (s.CpuTempMax is { } cm) readings.Add(("CPU", cm, s.CpuTempAvg));
        if (s.GpuTempMax is { } gm) readings.Add(("GPU", gm, s.GpuTempAvg));

        if (readings.Count == 0)
        {
            c.Metrics.Add(new(Loc.S("Health_M_Readings"), Loc.S("Health_TempSensorsUnavailable")));
            c.Score = 80;
            c.Summary = Loc.S("Health_Summary_CouldNotReadTemps");
            return c;
        }

        var hottest = readings.OrderByDescending(r => r.Max).First();
        double globalMax = hottest.Max;

        c.Metrics.Add(new(Loc.S("Health_M_CpuAvgMax"), s.CpuTempMax is null ? "N/D" : $"{s.CpuTempAvg:0}°C / {s.CpuTempMax:0}°C"));
        c.Metrics.Add(new(Loc.S("Health_M_GpuAvgMax"), s.GpuTempMax is null ? "N/D" : $"{s.GpuTempAvg:0}°C / {s.GpuTempMax:0}°C"));
        c.Metrics.Add(new(Loc.S("Health_M_HottestComponent"), $"{hottest.Component} ({hottest.Max:0}°C)"));
        c.Metrics.Add(new(Loc.S("Health_M_ThermalRisk"), ThermalRisk(globalMax)));

        if (globalMax >= 95) { score -= 35; }
        else if (globalMax >= 88) { score -= 18; }
        else if (globalMax >= 80) { score -= 7; }

        c.Score = Clamp(score);
        c.Summary = StateText(c.Score);
        return c;
    }

    private static string ThermalRisk(double maxTemp) => maxTemp switch
    {
        >= 95 => Loc.S("Health_RiskHigh"),
        >= 85 => Loc.S("Health_RiskModerate"),
        >= 75 => Loc.S("Health_RiskLow"),
        _ => Loc.S("Health_RiskVeryLow"),
    };

    // ===================== Segurança =====================

    private ComponentHealth AnalyzeSecurity(Context ctx)
    {
        var c = new ComponentHealth { Key = "security", Name = Loc.S("Health_Security"), Icon = "🛡️" };
        int score = 100;

        bool? firewall = ReadFirewallEnabled();
        var (defenderRealtime, defenderAntivirus, signatureAgeDays) = ReadDefenderStatus();
        bool rebootPending = IsRebootPending();

        c.Metrics.Add(new(Loc.S("Health_M_Firewall"), firewall is null ? "N/D" : firewall.Value ? Loc.S("Health_Enabled") : Loc.S("Health_Disabled")));
        c.Metrics.Add(new("Windows Defender", defenderAntivirus is null ? Loc.S("Health_NaOtherAntivirus") : defenderAntivirus.Value ? Loc.S("Health_Active") : Loc.S("Health_Inactive")));
        c.Metrics.Add(new(Loc.S("Health_M_RealtimeProtection"), defenderRealtime is null ? "N/D" : defenderRealtime.Value ? Loc.S("Health_On") : Loc.S("Health_Off")));
        c.Metrics.Add(new(Loc.S("Health_M_VirusSignatures"), signatureAgeDays is { } d ? (d == 0 ? Loc.S("Health_UpdatedToday") : Loc.F("Health_DaysAgo", d)) : "N/D"));
        c.Metrics.Add(new(Loc.S("Health_M_SecurityUpdates"), rebootPending ? Loc.S("Health_RebootPending") : Loc.S("Health_UpToDate")));

        if (firewall == false) { score -= 30; ctx.Problem(Loc.S("Health_P_FirewallOff"), ProblemSeverity.Alta); ctx.Recommend(Loc.S("Health_R_EnableFirewall")); }
        if (defenderRealtime == false && defenderAntivirus != null) { score -= 25; ctx.Problem(Loc.S("Health_P_RealtimeOff"), ProblemSeverity.Alta); ctx.Recommend(Loc.S("Health_R_EnableRealtime")); }
        if (signatureAgeDays is { } age && age > 7) { score -= 12; ctx.Problem(Loc.F("Health_P_SignaturesOutdated", age), ProblemSeverity.Moderada); ctx.Recommend(Loc.S("Health_R_UpdateAntivirus")); }
        if (rebootPending) { score -= 5; }

        c.Score = Clamp(score);
        c.Summary = StateText(c.Score);
        return c;
    }

    private static bool? ReadFirewallEnabled()
    {
        try
        {
            // Se qualquer perfil estiver ligado, consideramos o firewall ativo.
            string[] profiles =
            {
                @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\StandardProfile",
                @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\PublicProfile",
                @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\DomainProfile",
            };
            bool any = false, found = false;
            foreach (var p in profiles)
            {
                using var key = Registry.LocalMachine.OpenSubKey(p);
                if (key?.GetValue("EnableFirewall") is int v) { found = true; any |= v == 1; }
            }
            return found ? any : null;
        }
        catch { return null; }
    }

    private static (bool? realtime, bool? antivirus, int? signatureAgeDays) ReadDefenderStatus()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\Microsoft\Windows\Defender", "SELECT * FROM MSFT_MpComputerStatus");
            foreach (ManagementObject mo in searcher.Get())
            {
                bool? realtime = mo["RealTimeProtectionEnabled"] as bool?;
                bool? av = mo["AntivirusEnabled"] as bool?;
                int? age = null;
                try { if (mo["AntivirusSignatureAge"] is { } a) age = Convert.ToInt32(a); } catch { }
                return (realtime, av, age);
            }
        }
        catch { }
        return (null, null, null);
    }

    // ===================== Riscos gerais =====================

    private void BuildOverallRisks(Context ctx, SensorSample s)
    {
        // Superaquecimento.
        double maxTemp = Math.Max(s.CpuTempMax ?? 0, s.GpuTempMax ?? 0);
        ctx.Risk(Loc.S("Health_Risk_Overheating"), maxTemp switch
        {
            >= 95 => RiskLevel.Alto,
            >= 85 => RiskLevel.Moderado,
            >= 75 => RiskLevel.Baixo,
            > 0 => RiskLevel.MuitoBaixo,
            _ => RiskLevel.Baixo,
        });

        // Gargalo CPU↔GPU: combina a saturação de cada um com o desequilíbrio entre eles.
        double gap = Math.Abs(s.CpuUsageAvg - s.GpuUsageAvg);
        ctx.Risk(Loc.S("Health_Risk_Bottleneck"), (s.CpuUsageAvg, s.GpuUsageAvg, gap) switch
        {
            ( >= 90, _, _) or (_, >= 95, _) => RiskLevel.Alto,
            (_, _, >= 40) when s.GpuUsageAvg > 0 => RiskLevel.Moderado,
            ( >= 75, _, _) => RiskLevel.Moderado,
            _ => RiskLevel.Baixo,
        });

        // Falha de SSD: deriva dos problemas de disco já detectados.
        bool diskCritical = ctx.Report.Problems.Any(p => p.Description.Contains("SMART") || p.Description.Contains("setor"));
        bool diskLowLife = ctx.Report.Problems.Any(p => p.Description.Contains("Vida útil"));
        ctx.Risk(Loc.S("Health_Risk_DiskFailure"), diskCritical ? RiskLevel.Alto : diskLowLife ? RiskLevel.Moderado : RiskLevel.MuitoBaixo);

        // Instabilidade do sistema.
        bool sysIssues = ctx.Report.Problems.Any(p => p.Severity >= ProblemSeverity.Alta);
        ctx.Risk(Loc.S("Health_Risk_Instability"), sysIssues ? RiskLevel.Moderado : RiskLevel.Baixo);

        // Lentidão futura: muitos programas na inicialização / pouco espaço / RAM cheia.
        bool slowdown = ctx.Report.Problems.Any(p =>
            p.Description.Contains("inicialização") || p.Description.Contains("livres") || p.Description.Contains("RAM"));
        ctx.Risk(Loc.S("Health_Risk_FutureSlowdown"), slowdown ? RiskLevel.Moderado : RiskLevel.Baixo);
    }

    // ===================== Gargalo CPU↔GPU =====================

    // Heurística simples (sem um jogo/benchmark rodando): compara a utilização média de CPU
    // e GPU durante a amostragem. Uma diferença grande sugere qual dos dois tende a ser o
    // fator limitante quando o PC está sob carga — não substitui um teste em jogo real.
    private static string DescribeCpuGpuBottleneck(SensorSample s)
    {
        if (s.GpuName is null) return Loc.S("Health_Bottleneck_NoGpu");
        if (s.CpuUsageAvg <= 1 && s.GpuUsageAvg <= 1) return Loc.S("Health_Bottleneck_Idle");
        if (s.GpuUsageAvg <= 1) return Loc.F("Health_Bottleneck_GpuIdle", $"{s.CpuUsageAvg:0}");

        double diff = s.CpuUsageAvg - s.GpuUsageAvg;
        if (diff >= 30) return Loc.F("Health_Bottleneck_Cpu", $"{s.CpuUsageAvg:0}", $"{s.GpuUsageAvg:0}");
        if (diff <= -30) return Loc.F("Health_Bottleneck_Gpu", $"{s.CpuUsageAvg:0}", $"{s.GpuUsageAvg:0}");
        return Loc.F("Health_Bottleneck_Balanced", $"{s.CpuUsageAvg:0}", $"{s.GpuUsageAvg:0}");
    }

    // ===================== Utilitários =====================

    private static int SafeProcessCount()
    {
        try { return Process.GetProcesses().Length; }
        catch { return -1; }
    }

    private static string FormatTemp(double? celsius, bool approximate) =>
        celsius is { } t ? $"{t:0}°C{(approximate ? Loc.S("Health_ApproxSuffix") : "")}" : "N/D";

    private static string FormatGb(double gb) => gb >= 1024 ? $"{gb / 1024:0.0} TB" : $"{gb:0} GB";

    private static int Clamp(int score) => Math.Clamp(score, 0, 100);

    private static string StateText(int score) => HealthScale.RatingFromScore(score) switch
    {
        HealthRating.Excelente => Loc.S("Health_State_Excellent"),
        HealthRating.MuitoBom => Loc.S("Health_State_VeryGood"),
        HealthRating.Bom => Loc.S("Health_State_Good"),
        HealthRating.Atencao => Loc.S("Health_State_Warning"),
        _ => Loc.S("Health_State_Critical"),
    };
}
