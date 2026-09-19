using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Pulse1x.App.Services;

/// <summary>
/// Otimizações de CPU, GPU e RAM — a categoria "Hardware" das Otimizações Avançadas.
///
/// Todas seguem a mesma disciplina do resto do Pulse1x: só métodos oficialmente suportados pelo
/// Windows (Registro, powercfg, APIs públicas), o valor anterior sempre gravado no
/// <see cref="OptimizationChangeLog"/> antes de mudar qualquer coisa, e nenhuma delas faz
/// overclock, mexe em voltagem ou altera limites térmicos — o que o app ajusta é como o Windows
/// distribui trabalho ao hardware, nunca o hardware em si.
///
/// As quatro famílias:
///   • Agendamento de GPU por hardware (HAGS) — deixa a própria GPU gerenciar sua fila de
///     comandos, cortando um intermediário e um pouco de latência. Exige reinício.
///   • Estacionamento de núcleos e limitação de energia — impede o Windows de desligar núcleos
///     ociosos e de rebaixar processos para núcleos econômicos no meio de um jogo.
///   • MSI (interrupções sinalizadas por mensagem) na GPU — interrupções entregues pela via
///     moderna do PCI Express, sem disputar a linha compartilhada. Ajuda contra engasgos.
///   • Prioridade de jogos no agendador multimídia (MMCSS) — diz ao Windows que a tarefa "Games"
///     merece a CPU e a GPU antes do resto.
/// </summary>
public class HardwareOptimizationService
{
    private readonly OptimizationChangeLog _log;

    // Subgrupo do processador e as configurações de estacionamento de núcleos, nos GUIDs oficiais.
    private const string SubProcessor = "54533251-82be-4824-96c1-47b60b740d00";
    private const string SetCoreParkingMin = "0cc5b647-c1df-4637-891a-dec35c318583";
    private const string SetCoreParkingMax = "ea062031-0e34-4ff1-9b6d-eb1059334028";

    private const string GraphicsDrivers = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
    private const string PowerThrottling = @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling";
    private const string GamesProfile = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games";

    public HardwareOptimizationService(OptimizationChangeLog log) => _log = log;

    // =====================================================================================
    //  GPU — Agendamento por hardware (HAGS)
    // =====================================================================================

    /// <summary>
    /// Disponível a partir do Windows 10 2004 (build 19041) e apenas quando o driver da GPU
    /// declara suporte — a chave só existe nesse caso.
    /// </summary>
    public bool IsHagsAvailable()
    {
        if (Environment.OSVersion.Version.Build < 19041) return false;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(GraphicsDrivers);
            return key?.GetValue("HwSchMode") is not null;
        }
        catch { return false; }
    }

    public bool IsHagsEnabled() => GetDword(RegistryHive.LocalMachine, GraphicsDrivers, "HwSchMode") == 2;

    /// <summary>Liga o agendamento por hardware. Só vale após reiniciar o computador.</summary>
    public void EnableHags() =>
        SetDword("hw-gpu-scheduling", RegistryHive.LocalMachine, GraphicsDrivers, "HwSchMode", 2, fallbackOldValue: 1);

    // =====================================================================================
    //  CPU — Estacionamento de núcleos
    // =====================================================================================

    /// <summary>
    /// Com o estacionamento ativo, o Windows desliga núcleos ociosos para poupar energia e leva
    /// alguns milissegundos para acordá-los — o bastante para virar engasgo quando a carga de um
    /// jogo chega em rajadas. Mantendo o mínimo em 100%, todos ficam sempre disponíveis.
    /// </summary>
    public async Task<bool> IsCoreParkingDisabledAsync()
    {
        var (ac, _) = await ReadIndexAsync(SubProcessor, SetCoreParkingMin);
        return ac == 100;
    }

    public async Task DisableCoreParkingAsync()
    {
        await CaptureIndexAsync("core-parking", SubProcessor, SetCoreParkingMin);
        await WriteIndexAsync(SubProcessor, SetCoreParkingMin, 100);
        await WriteIndexAsync(SubProcessor, SetCoreParkingMax, 100);
    }

    /// <summary>
    /// As duas configurações de estacionamento vêm ocultas no powercfg; sem isto o valor é gravado
    /// mas o usuário não consegue conferi-lo pelo Painel de Controle. Reversível.
    /// </summary>
    public async Task UnhideCoreParkingAsync()
    {
        await RunAsync("powercfg", $"-attributes {SubProcessor} {SetCoreParkingMin} -ATTRIB_HIDE");
        await RunAsync("powercfg", $"-attributes {SubProcessor} {SetCoreParkingMax} -ATTRIB_HIDE");
    }

    // =====================================================================================
    //  CPU — Limitação de energia (Power Throttling / EcoQoS)
    // =====================================================================================

    /// <summary>
    /// O Windows rebaixa processos que julga secundários para núcleos econômicos e frequências
    /// menores. Num PC de mesa ligado à tomada isso só atrapalha, e em CPUs híbridas (P-cores e
    /// E-cores) pode mandar o jogo para os núcleos errados.
    /// </summary>
    public bool IsPowerThrottlingDisabled() =>
        GetDword(RegistryHive.LocalMachine, PowerThrottling, "PowerThrottlingOff") == 1;

    public void DisablePowerThrottling() =>
        SetDword("power-throttling", RegistryHive.LocalMachine, PowerThrottling, "PowerThrottlingOff", 1, fallbackOldValue: 0);

    // =====================================================================================
    //  GPU — MSI (interrupções sinalizadas por mensagem)
    // =====================================================================================

    /// <summary>A GPU encontrada no sistema, com o caminho onde o modo de interrupção é ajustado.</summary>
    public record GpuDevice(string Name, string PnpDeviceId)
    {
        /// <summary>Caminho, sob HKLM, das propriedades de interrupção deste dispositivo.</summary>
        public string MsiKeyPath =>
            $@"SYSTEM\CurrentControlSet\Enum\{PnpDeviceId}\Device Parameters\Interrupt Management\MessageSignaledInterruptProperties";
    }

    /// <summary>
    /// Localiza a GPU principal pelo WMI. Placas virtuais/remotas (Área de Trabalho Remota, Hyper-V,
    /// Parsec) são descartadas: elas não têm interrupções reais para ajustar.
    /// </summary>
    public GpuDevice? FindPrimaryGpu()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, PNPDeviceID, AdapterRAM FROM Win32_VideoController");

            GpuDevice? best = null;
            long bestRam = -1;

            foreach (ManagementObject mo in searcher.Get())
            {
                string name = mo["Name"]?.ToString() ?? "";
                string pnp = mo["PNPDeviceID"]?.ToString() ?? "";

                if (!pnp.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase)) continue;
                if (IsVirtualAdapter(name)) continue;

                long ram = mo["AdapterRAM"] is uint r ? r : 0;
                if (ram > bestRam)
                {
                    bestRam = ram;
                    best = new GpuDevice(name, pnp);
                }
            }

            return best;
        }
        catch { return null; }
    }

    private static bool IsVirtualAdapter(string name) =>
        name.Contains("Remote", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Parsec", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase);

    public bool IsMsiEnabled(GpuDevice gpu) =>
        GetDword(RegistryHive.LocalMachine, gpu.MsiKeyPath, "MSISupported") == 1;

    /// <summary>
    /// Só oferecemos o ajuste quando a chave de propriedades de interrupção já existe para este
    /// dispositivo — criá-la do zero num hardware que não anuncia suporte a MSI é justamente o
    /// caminho para uma tela preta no próximo boot.
    /// </summary>
    public bool IsMsiAvailable(GpuDevice gpu)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(gpu.MsiKeyPath);
            return key is not null;
        }
        catch { return false; }
    }

    public void EnableMsi(GpuDevice gpu) =>
        SetDword("gpu-msi", RegistryHive.LocalMachine, gpu.MsiKeyPath, "MSISupported", 1, fallbackOldValue: 0);

    // =====================================================================================
    //  CPU/GPU — Prioridade de jogos no agendador multimídia (MMCSS)
    // =====================================================================================

    /// <summary>
    /// O perfil "Games" do agendador multimídia é o que o Windows consulta para decidir quanta CPU
    /// e GPU dar a um jogo. Os valores aqui são os do próprio perfil de alto desempenho da
    /// Microsoft — nada exótico, apenas explicitados.
    /// </summary>
    public bool IsGamePriorityApplied() =>
        GetString(RegistryHive.LocalMachine, GamesProfile, "GPU Priority") == "8" &&
        GetString(RegistryHive.LocalMachine, GamesProfile, "Priority") == "6";

    public void ApplyGamePriority()
    {
        SetString("game-priority", GamesProfile, "GPU Priority", "8");
        SetString("game-priority", GamesProfile, "Priority", "6");
        SetString("game-priority", GamesProfile, "Scheduling Category", "High");
        SetString("game-priority", GamesProfile, "SFIO Priority", "High");
    }

    // =====================================================================================
    //  Restauração dos padrões do Windows
    // =====================================================================================

    /// <summary>
    /// Devolve uma otimização ao padrão de fábrica quando o estado "ligado" não veio do Pulse1x e,
    /// por isso, não há nada no log para desfazer. Mesmos alvos do Apply, no sentido inverso.
    /// </summary>
    public async Task RestoreDefaultAsync(string optimizationId)
    {
        switch (optimizationId)
        {
            case "hw-gpu-scheduling":
                SetDwordRaw(RegistryHive.LocalMachine, GraphicsDrivers, "HwSchMode", 1);
                break;

            case "core-parking":
                // 0% = o Windows volta a poder estacionar todos os núcleos que quiser.
                await WriteIndexAsync(SubProcessor, SetCoreParkingMin, 0);
                await WriteIndexAsync(SubProcessor, SetCoreParkingMax, 100);
                break;

            case "power-throttling":
                DeleteValue(RegistryHive.LocalMachine, PowerThrottling, "PowerThrottlingOff");
                break;

            case "gpu-msi":
                {
                    var gpu = FindPrimaryGpu();
                    if (gpu is not null) SetDwordRaw(RegistryHive.LocalMachine, gpu.MsiKeyPath, "MSISupported", 0);
                }
                break;

            case "game-priority":
                // Valores de fábrica do perfil Games do Windows.
                SetStringRaw(GamesProfile, "GPU Priority", "8");
                SetStringRaw(GamesProfile, "Priority", "2");
                SetStringRaw(GamesProfile, "Scheduling Category", "Medium");
                SetStringRaw(GamesProfile, "SFIO Priority", "Normal");
                break;
        }
    }

    // =====================================================================================
    //  Helpers de powercfg
    // =====================================================================================

    // Guarda os índices atuais (tomada e bateria) antes de mudá-los, no formato "ac|dc" que a
    // reversão de ChangeKind.PowerCfg sabe ler.
    private async Task CaptureIndexAsync(string optId, string subGroup, string setting)
    {
        var (ac, dc) = await ReadIndexAsync(subGroup, setting);
        if (ac is null && dc is null) return;

        _log.Record(new OptimizationChange
        {
            OptimizationId = optId,
            OptimizationTitle = optId,
            Kind = ChangeKind.PowerCfg,
            KeyPath = $"{subGroup} {setting}",
            ValueName = "ValueIndex",
            OldValue = $"{ac ?? 0}|{dc ?? 0}",
            NewValue = "100|100",
        });
    }

    private async Task WriteIndexAsync(string subGroup, string setting, int value)
    {
        await RunAsync("powercfg", $"/setacvalueindex SCHEME_CURRENT {subGroup} {setting} {value}");
        await RunAsync("powercfg", $"/setdcvalueindex SCHEME_CURRENT {subGroup} {setting} {value}");
        await RunAsync("powercfg", "/setactive SCHEME_CURRENT");
    }

    // Lê os índices de tomada (AC) e bateria (DC). O powercfg escreve na página de código do
    // console, então nenhuma palavra acentuada é usada para achar as linhas — vale a ordem em que
    // ele sempre as imprime: primeiro AC, depois DC.
    private async Task<(int? ac, int? dc)> ReadIndexAsync(string subGroup, string setting)
    {
        var (_, output) = await RunAsync("powercfg", $"/query SCHEME_CURRENT {subGroup} {setting}");

        int? ac = null, dc = null;
        var indexes = new List<int>();

        foreach (var line in output.Split('\n'))
        {
            bool isIndexLine = line.Contains("Setting Index", StringComparison.OrdinalIgnoreCase)
                               || line.Contains("ndice", StringComparison.OrdinalIgnoreCase);
            if (!isIndexLine) continue;

            var hex = Regex.Match(line, @"0x([0-9a-fA-F]+)");
            if (!hex.Success) continue;
            if (!int.TryParse(hex.Groups[1].Value, System.Globalization.NumberStyles.HexNumber, null, out int value))
                continue;

            if (line.Contains("AC Power Setting Index", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Alternadas", StringComparison.OrdinalIgnoreCase))
                ac = value;
            else if (line.Contains("DC Power Setting Index", StringComparison.OrdinalIgnoreCase))
                dc = value;
            else
                indexes.Add(value);
        }

        if (ac is null && indexes.Count > 0) ac = indexes[0];
        if (dc is null && indexes.Count > 1) dc = indexes[1];
        if (dc is null && ac is not null && indexes.Count == 1) dc = indexes[0];

        return (ac, dc);
    }

    // =====================================================================================
    //  Helpers de Registro
    // =====================================================================================

    // Mesma regra do AdvancedOptimizationService: mesmo quando o valor já está no alvo, registramos
    // a alteração usando o fallback como "valor original" — sem isso a reversão não teria o que
    // desfazer e o interruptor voltaria sozinho para ligado.
    private void SetDword(string optId, RegistryHive hive, string subKey, string name, int value, int? fallbackOldValue = null)
    {
        int? old = GetDword(hive, subKey, name);
        bool alreadyAtTarget = old == value;
        if (alreadyAtTarget && fallbackOldValue is null) return;

        try
        {
            var root = hive == RegistryHive.LocalMachine ? Registry.LocalMachine : Registry.CurrentUser;
            using var wk = root.CreateSubKey(subKey);
            wk.SetValue(name, value, RegistryValueKind.DWord);
        }
        catch { return; }

        _log.Record(new OptimizationChange
        {
            OptimizationId = optId,
            OptimizationTitle = optId,
            Kind = ChangeKind.Registry,
            Hive = hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU",
            KeyPath = subKey,
            ValueName = name,
            ValueKind = "DWord",
            OldValue = (alreadyAtTarget ? fallbackOldValue : old)?.ToString(),
            NewValue = value.ToString(),
        });
    }

    private void SetString(string optId, string subKey, string name, string value)
    {
        string? old = GetString(RegistryHive.LocalMachine, subKey, name);
        if (old == value) return;

        try
        {
            using var wk = Registry.LocalMachine.CreateSubKey(subKey);
            wk.SetValue(name, value, RegistryValueKind.String);
        }
        catch { return; }

        _log.Record(new OptimizationChange
        {
            OptimizationId = optId,
            OptimizationTitle = optId,
            Kind = ChangeKind.Registry,
            Hive = "HKLM",
            KeyPath = subKey,
            ValueName = name,
            ValueKind = "String",
            OldValue = old,
            NewValue = value,
        });
    }

    private static int? GetDword(RegistryHive hive, string subKey, string name)
    {
        try
        {
            var root = hive == RegistryHive.LocalMachine ? Registry.LocalMachine : Registry.CurrentUser;
            using var key = root.OpenSubKey(subKey);
            return key?.GetValue(name) is int i ? i : (int?)null;
        }
        catch { return null; }
    }

    private static string? GetString(RegistryHive hive, string subKey, string name)
    {
        try
        {
            var root = hive == RegistryHive.LocalMachine ? Registry.LocalMachine : Registry.CurrentUser;
            using var key = root.OpenSubKey(subKey);
            return key?.GetValue(name)?.ToString();
        }
        catch { return null; }
    }

    // Gravação direta, sem log — usada só na restauração ao padrão do Windows (o Apply registra
    // de novo se o usuário religar a otimização).
    private static void SetDwordRaw(RegistryHive hive, string subKey, string name, int value)
    {
        try
        {
            var root = hive == RegistryHive.LocalMachine ? Registry.LocalMachine : Registry.CurrentUser;
            using var wk = root.CreateSubKey(subKey);
            wk.SetValue(name, value, RegistryValueKind.DWord);
        }
        catch { }
    }

    private static void SetStringRaw(string subKey, string name, string value)
    {
        try
        {
            using var wk = Registry.LocalMachine.CreateSubKey(subKey);
            wk.SetValue(name, value, RegistryValueKind.String);
        }
        catch { }
    }

    private static void DeleteValue(RegistryHive hive, string subKey, string name)
    {
        try
        {
            var root = hive == RegistryHive.LocalMachine ? Registry.LocalMachine : Registry.CurrentUser;
            using var key = root.OpenSubKey(subKey, writable: true);
            if (key?.GetValue(name) is not null)
                key.DeleteValue(name, throwOnMissingValue: false);
        }
        catch { }
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
