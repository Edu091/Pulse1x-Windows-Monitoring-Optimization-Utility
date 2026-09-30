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
    private const string MultimediaProfile = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
    private const string PriorityControl = @"SYSTEM\CurrentControlSet\Control\PriorityControl";
    private const string DirectXUserPrefs = @"Software\Microsoft\DirectX\UserGpuPreferences";
    private const string DirectXGlobalSettings = "DirectXUserGlobalSettings";

    /// <summary>GUID oficial do modelo do plano "Desempenho Máximo" (Ultimate Performance).</summary>
    private const string UltimatePlanTemplate = "e9a42b02-d5df-448d-aa00-03f14749eb61";
    private const string BalancedPlan = "381b4222-f694-41f0-9685-ff5bb260df2e";

    // Título traduzido para o Histórico e Reversão (antes as entradas mostravam o id cru, ex.:
    // "core-parking"). Vem do AdvancedOptimizationService, dono da lista de otimizações.
    private readonly Func<string, string> _title;

    public HardwareOptimizationService(OptimizationChangeLog log, Func<string, string>? title = null)
    {
        _log = log;
        _title = title ?? (id => id);
    }

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
        GetDword(RegistryHive.LocalMachine, GamesProfile, "GPU Priority") == 8 &&
        GetDword(RegistryHive.LocalMachine, GamesProfile, "Priority") == 6;

    // "GPU Priority" e "Priority" são REG_DWORD no perfil original do Windows. Versões anteriores
    // do Pulse1x os gravavam como texto (REG_SZ), que o MMCSS não lê — a otimização aparecia
    // ligada sem surtir efeito. SetDwordRepairing reescreve no tipo certo e guarda o valor antigo.
    public void ApplyGamePriority()
    {
        SetDwordRepairing("game-priority", GamesProfile, "GPU Priority", 8, defaultValue: 8);
        SetDwordRepairing("game-priority", GamesProfile, "Priority", 6, defaultValue: 2);
        SetString("game-priority", GamesProfile, "Scheduling Category", "High");
        SetString("game-priority", GamesProfile, "SFIO Priority", "High");
    }

    /// <summary>Nomes do perfil Games que são DWORD — usados para corrigir reversões antigas gravadas como texto.</summary>
    public static bool IsGamesProfileDword(string keyPath, string valueName) =>
        keyPath.Equals(GamesProfile, StringComparison.OrdinalIgnoreCase) &&
        (valueName == "GPU Priority" || valueName == "Priority");

    // =====================================================================================
    //  CPU — Reserva do agendador multimídia para tarefas em segundo plano
    // =====================================================================================

    /// <summary>
    /// SystemResponsiveness é a fatia da CPU que o MMCSS reserva para tarefas de baixa prioridade
    /// enquanto um app multimídia (jogo, áudio, vídeo) está ativo. O padrão é 20%; 10% é o mínimo
    /// que o Windows aceita (valores menores são tratados como 20).
    /// </summary>
    public bool IsSystemResponsivenessApplied() =>
        GetDword(RegistryHive.LocalMachine, MultimediaProfile, "SystemResponsiveness") == 10;

    public void ApplySystemResponsiveness() =>
        SetDword("mmcss-responsiveness", RegistryHive.LocalMachine, MultimediaProfile, "SystemResponsiveness", 10, fallbackOldValue: 20);

    // =====================================================================================
    //  CPU — Prioridade do programa em primeiro plano
    // =====================================================================================

    /// <summary>
    /// Win32PrioritySeparation define o tamanho da fatia de tempo e o reforço dado ao programa em
    /// primeiro plano. 0x26 (38) = fatias curtas e variáveis, com reforço máximo (3×) para a janela
    /// ativa — o jogo recebe CPU com mais frequência que o que roda atrás dele. Padrão: 2.
    /// </summary>
    public bool IsForegroundPriorityApplied() =>
        GetDword(RegistryHive.LocalMachine, PriorityControl, "Win32PrioritySeparation") == 0x26;

    public void ApplyForegroundPriority() =>
        SetDword("foreground-priority", RegistryHive.LocalMachine, PriorityControl, "Win32PrioritySeparation", 0x26, fallbackOldValue: 2);

    // =====================================================================================
    //  GPU — Otimizações para jogos em janela (Windows 11 22H2+)
    // =====================================================================================

    /// <summary>
    /// A opção "Otimizações para jogos em janela" das Configurações de Gráficos. Troca o modelo de
    /// apresentação antigo (blt) de jogos DirectX 10/11 em janela ou em tela cheia sem bordas pelo
    /// modelo flip, o mesmo da tela cheia exclusiva: menos latência e acesso a VRR/Auto HDR.
    /// A chave é uma lista "nome=valor;" compartilhada com outras opções — só o nosso item muda.
    /// </summary>
    public bool IsWindowedGameOptAvailable() => Environment.OSVersion.Version.Build >= 22621;

    public bool IsWindowedGameOptApplied() =>
        ReadDirectXSetting("SwapEffectUpgradeEnable") == "1";

    public void ApplyWindowedGameOpt()
    {
        string? old = GetString(RegistryHive.CurrentUser, DirectXUserPrefs, DirectXGlobalSettings);
        string updated = WithDirectXSetting(old, "SwapEffectUpgradeEnable", "1");
        if (updated == old) return;

        try
        {
            using var wk = InteractiveUser.Registry.CreateSubKey(DirectXUserPrefs);
            wk.SetValue(DirectXGlobalSettings, updated, RegistryValueKind.String);
        }
        catch { return; }

        // Guarda só o NOSSO item ("DxItem"), não a lista inteira: a mesma string guarda Auto HDR e
        // VRR, e desfazer regravando a lista antiga apagava o que o usuário ligasse depois.
        _log.Record(new OptimizationChange
        {
            OptimizationId = "windowed-game-opt",
            OptimizationTitle = _title("windowed-game-opt"),
            Kind = ChangeKind.Registry,
            Hive = "HKCU",
            KeyPath = DirectXUserPrefs,
            ValueName = DirectXGlobalSettings,
            ValueKind = DirectXItemKind,
            OldValue = ReadDirectXItem(old, "SwapEffectUpgradeEnable"),
            NewValue = "1",
        });
    }

    /// <summary>ValueKind das alterações de um único item da lista do DirectX.</summary>
    public const string DirectXItemKind = "DxItem";

    /// <summary>Desfaz um item da lista do DirectX preservando os demais (usado pela reversão genérica).</summary>
    public static void RevertDirectXItem(OptimizationChange change)
    {
        using var wk = InteractiveUser.Registry.CreateSubKey(change.KeyPath);
        string? raw = wk.GetValue(change.ValueName) as string;
        string updated = WithDirectXSetting(raw, "SwapEffectUpgradeEnable", change.OldValue);
        if (updated.Length == 0) wk.DeleteValue(change.ValueName, throwOnMissingValue: false);
        else wk.SetValue(change.ValueName, updated, RegistryValueKind.String);
    }

    private static string? ReadDirectXItem(string? raw, string name)
    {
        foreach (var part in (raw ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && kv[0].Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                return kv[1].Trim();
        }
        return null;
    }

    private string? ReadDirectXSetting(string name)
    {
        string? raw = GetString(RegistryHive.CurrentUser, DirectXUserPrefs, DirectXGlobalSettings);
        if (raw is null) return null;
        foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && kv[0].Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                return kv[1].Trim();
        }
        return null;
    }

    /// <summary>Define (ou substitui) um item "nome=valor;" preservando os demais. Público para teste.</summary>
    public static string WithDirectXSetting(string? raw, string name, string? value)
    {
        var parts = (raw ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !p.Split('=', 2)[0].Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (value is not null) parts.Add($"{name}={value}");
        return parts.Count == 0 ? "" : string.Join(";", parts) + ";";
    }

    // =====================================================================================
    //  CPU — Plano de energia "Desempenho Máximo"
    // =====================================================================================

    /// <summary>
    /// O plano oculto "Desempenho Máximo" (Ultimate Performance) da Microsoft elimina as
    /// microlatências de gerenciamento de energia: a CPU não reduz frequência entre rajadas de
    /// carga. O Windows não o mostra por padrão; o Pulse1x cria uma cópia dele uma única vez e a
    /// reaproveita. O plano anterior é guardado no log e volta ao desfazer.
    /// </summary>
    public async Task<bool> IsUltimatePowerActiveAsync()
    {
        var (code, output) = await RunAsync("powercfg", "/getactivescheme");
        if (code != 0) return false;
        string? active = ExtractGuid(output);
        if (active is null) return false;
        // Reconhece também pelo nome: o plano pode ter sido ativado pelos Comandos Especiais ou
        // por um perfil do GameHub, cada um com a sua cópia do modelo.
        return active.Equals(UltimatePlanTemplate, StringComparison.OrdinalIgnoreCase)
               || active.Equals(FindKnownUltimatePlan(), StringComparison.OrdinalIgnoreCase)
               || IsUltimateName(output);
    }

    public async Task ApplyUltimatePowerAsync()
    {
        string? previous = ExtractGuid((await RunAsync("powercfg", "/getactivescheme")).output);

        // Reaproveita um plano "Desempenho Máximo" que já exista — o do log ou qualquer outro com
        // esse nome. Só procurar no log fazia cada ativação sem histórico criar mais uma cópia (uma
        // máquina acumulou dez planos idênticos).
        string? plan = FindKnownUltimatePlan();
        if (plan is null || (await RunAsync("powercfg", $"/query {plan}")).code != 0)
            plan = await FindUltimatePlanByNameAsync();
        if (plan is null)
        {
            var (code, output) = await RunAsync("powercfg", $"/duplicatescheme {UltimatePlanTemplate}");
            plan = ExtractGuid(output);
            if (code != 0 || plan is null)
                throw new InvalidOperationException(output.Trim());
        }

        var (setCode, setOutput) = await RunAsync("powercfg", $"/setactive {plan}");
        if (setCode != 0) throw new InvalidOperationException(setOutput.Trim());

        _log.Record(new OptimizationChange
        {
            OptimizationId = "ultimate-power",
            OptimizationTitle = _title("ultimate-power"),
            Kind = ChangeKind.PowerCfg,
            KeyPath = "powercfg /setactive",
            ValueName = "PowerPlan",
            OldValue = previous,
            NewValue = plan,
        });
    }

    // Nome do plano por idioma: "Ultimate Performance", "Desempenho Máximo", "Maximum Performance".
    private static readonly string[] UltimateNameFragments = { "Ultimate", "Máximo", "Maximo", "Maximum" };

    private static bool IsUltimateName(string powercfgLine)
    {
        var name = Regex.Match(powercfgLine, @"\(([^)]*)\)");
        return name.Success && UltimateNameFragments.Any(f =>
            name.Groups[1].Value.Contains(f, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<string?> FindUltimatePlanByNameAsync()
    {
        var (_, output) = await RunAsync("powercfg", "/list");
        foreach (var line in output.Split('\n'))
            if (IsUltimateName(line) && ExtractGuid(line) is { } guid)
                return guid;
        return null;
    }

    private static string? ExtractGuid(string text)
    {
        var match = Regex.Match(text, @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
        return match.Success ? match.Value : null;
    }

    // A cópia criada pelo Pulse1x fica registrada no log (mesmo depois de revertida), o que evita
    // duplicar o plano a cada vez que o interruptor é ligado.
    private string? FindKnownUltimatePlan() =>
        _log.GetAll()
            .Where(c => c.OptimizationId == "ultimate-power" && c.ValueName == "PowerPlan")
            .Select(c => c.NewValue)
            .LastOrDefault(v => !string.IsNullOrEmpty(v));

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
                // Valores de fábrica do perfil Games do Windows (os dois primeiros são DWORD).
                SetDwordRaw(RegistryHive.LocalMachine, GamesProfile, "GPU Priority", 8);
                SetDwordRaw(RegistryHive.LocalMachine, GamesProfile, "Priority", 2);
                SetStringRaw(GamesProfile, "Scheduling Category", "Medium");
                SetStringRaw(GamesProfile, "SFIO Priority", "Normal");
                break;

            case "mmcss-responsiveness":
                SetDwordRaw(RegistryHive.LocalMachine, MultimediaProfile, "SystemResponsiveness", 20);
                break;

            case "foreground-priority":
                SetDwordRaw(RegistryHive.LocalMachine, PriorityControl, "Win32PrioritySeparation", 2);
                break;

            case "windowed-game-opt":
                try
                {
                    string? raw = GetString(RegistryHive.CurrentUser, DirectXUserPrefs, DirectXGlobalSettings);
                    using var wk = InteractiveUser.Registry.CreateSubKey(DirectXUserPrefs);
                    wk.SetValue(DirectXGlobalSettings, WithDirectXSetting(raw, "SwapEffectUpgradeEnable", "0"), RegistryValueKind.String);
                }
                catch { }
                break;

            case "ultimate-power":
                await RunAsync("powercfg", $"/setactive {BalancedPlan}");
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
        string? scheme = ExtractGuid((await RunAsync("powercfg", "/getactivescheme")).output);

        _log.Record(new OptimizationChange
        {
            OptimizationId = optId,
            OptimizationTitle = _title(optId),
            Kind = ChangeKind.PowerCfg,
            // O plano também vai no KeyPath: desfazer depois de trocar de plano (ex.: ligar o
            // Desempenho Máximo) gravava os valores antigos no plano errado.
            KeyPath = scheme is null ? $"{subGroup} {setting}" : $"{subGroup} {setting} {scheme}",
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
            var root = hive == RegistryHive.LocalMachine ? Registry.LocalMachine : InteractiveUser.Registry;
            using var wk = root.CreateSubKey(subKey);
            wk.SetValue(name, value, RegistryValueKind.DWord);
        }
        catch { return; }

        _log.Record(new OptimizationChange
        {
            OptimizationId = optId,
            OptimizationTitle = _title(optId),
            Kind = ChangeKind.Registry,
            Hive = hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU",
            KeyPath = subKey,
            ValueName = name,
            ValueKind = "DWord",
            OldValue = (alreadyAtTarget ? fallbackOldValue : old)?.ToString(),
            NewValue = value.ToString(),
        });
    }

    // Como SetDword, mas aceita um valor atual gravado como texto (REG_SZ "6") e o converte: o
    // valor antigo é registrado como DWORD, então desfazer devolve também o tipo correto.
    private void SetDwordRepairing(string optId, string subKey, string name, int value, int defaultValue)
    {
        object? raw;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(subKey);
            raw = key?.GetValue(name);
        }
        catch { raw = null; }

        if (raw is int current && current == value) return;
        int? old = raw switch
        {
            int i => i,
            string s when int.TryParse(s, out int parsed) => parsed,
            _ => null,
        };

        try
        {
            using var wk = Registry.LocalMachine.CreateSubKey(subKey);
            wk.SetValue(name, value, RegistryValueKind.DWord);
        }
        catch { return; }

        _log.Record(new OptimizationChange
        {
            OptimizationId = optId,
            OptimizationTitle = _title(optId),
            Kind = ChangeKind.Registry,
            Hive = "HKLM",
            KeyPath = subKey,
            ValueName = name,
            ValueKind = "DWord",
            // Se o valor já era o alvo (só no tipo errado), desfazer volta ao padrão do Windows.
            OldValue = (old is null || old == value ? defaultValue : old.Value).ToString(),
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
            OptimizationTitle = _title(optId),
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
            var root = hive == RegistryHive.LocalMachine ? Registry.LocalMachine : InteractiveUser.Registry;
            using var key = root.OpenSubKey(subKey);
            return key?.GetValue(name) is int i ? i : (int?)null;
        }
        catch { return null; }
    }

    private static string? GetString(RegistryHive hive, string subKey, string name)
    {
        try
        {
            var root = hive == RegistryHive.LocalMachine ? Registry.LocalMachine : InteractiveUser.Registry;
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
            var root = hive == RegistryHive.LocalMachine ? Registry.LocalMachine : InteractiveUser.Registry;
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
            var root = hive == RegistryHive.LocalMachine ? Registry.LocalMachine : InteractiveUser.Registry;
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
