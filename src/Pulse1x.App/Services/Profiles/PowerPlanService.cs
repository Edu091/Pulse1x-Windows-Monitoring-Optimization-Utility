using System.Diagnostics;
using System.Text.RegularExpressions;
using Pulse1x.App.Models.GameHub;

namespace Pulse1x.App.Services.Profiles;

/// <summary>Um plano de energia do Windows, como listado pelo powercfg.</summary>
public record PowerPlanInfo(string Guid, string Name, bool IsActive);

/// <summary>
/// Uma configuração avançada de plano de energia que o editor sabe mostrar de forma amigável.
/// <see cref="Options"/> preenchido = lista de escolhas; vazio = valor numérico (com unidade).
/// </summary>
public record PowerSettingDefinition(
    string Key,
    string SubGroupGuid,
    string SettingGuid,
    string LabelKey,
    string Unit,
    int Min,
    int Max,
    (int Value, string LabelKey)[] Options);

/// <summary>
/// Leitura e escrita de planos de energia do Windows, incluindo a edição avançada (estado mínimo/
/// máximo da CPU, boost, política de resfriamento, PCI Express, suspensão seletiva de USB,
/// tempos de suspensão) — o equivalente conceitual ao Quick CPU pedido pelos perfis do GameHub.
///
/// Tudo é feito pelo powercfg, a mesma ferramenta oficial que o resto do Pulse1x já usa
/// (SpecialCommandsService, NetworkOptimizationService), então não há nada proprietário nem
/// irreversível: todo valor lido antes de mudar volta pelo snapshot na restauração.
/// </summary>
public class PowerPlanService
{
    // GUIDs oficiais dos planos padrão do Windows.
    public const string BalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";
    public const string HighPerformanceGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    public const string PowerSaverGuid = "a1841308-3541-4fab-bc81-f71556f20b4a";
    public const string UltimateTemplateGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";

    // ---- Subgrupos ----
    public const string SubProcessor = "54533251-82be-4824-96c1-47b60b740d00";
    public const string SubPciExpress = "501a4d13-42af-4429-9fd1-a8218c268e20";
    public const string SubUsb = "2a737441-1930-4402-8d77-b2bebba308a3";
    public const string SubSleep = "238c9fa8-0aad-41ed-83f4-97be242c8f20";
    public const string SubVideo = "7516b95f-f776-4464-8c53-06167f40cc99";
    public const string SubDisk = "0012ee47-9041-4b5d-9b77-535fba8b1442";

    // ---- Configurações ----
    public const string SetCpuMin = "893dee8e-2bef-41e0-89c6-b55d0929964c";
    public const string SetCpuMax = "bc5038f7-23e0-4960-96da-33abaf5935ec";
    public const string SetBoostMode = "be337238-0d82-4146-a960-4f3749d470c7";
    public const string SetCoolingPolicy = "94d3a615-a899-4ac5-ae2b-e4d8f634367f";
    public const string SetPciAspm = "ee12f906-d277-404b-b6da-e5fa1a576df5";
    public const string SetUsbSuspend = "48e6b7a6-50f5-4782-a5d4-53bb8f07e226";
    public const string SetStandbyIdle = "29f6c1db-86da-48c5-9fdb-f2b67b1f44da";
    public const string SetVideoIdle = "3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e";
    public const string SetDiskIdle = "6738e2c4-e8a5-4a42-b16a-e040e769756e";

    /// <summary>
    /// Catálogo das configurações avançadas expostas no editor de perfil. A ordem aqui é a ordem
    /// mostrada na interface.
    /// </summary>
    public static readonly PowerSettingDefinition[] AdvancedSettings =
    {
        new("CpuMin", SubProcessor, SetCpuMin, "GH_PwCpuMin", "%", 0, 100, Array.Empty<(int, string)>()),
        new("CpuMax", SubProcessor, SetCpuMax, "GH_PwCpuMax", "%", 0, 100, Array.Empty<(int, string)>()),
        new("Boost", SubProcessor, SetBoostMode, "GH_PwBoost", "", 0, 6, new[]
        {
            (0, "GH_PwBoostOff"), (1, "GH_PwBoostOn"), (2, "GH_PwBoostAggressive"),
            (3, "GH_PwBoostEfficient"), (4, "GH_PwBoostEfficientAggressive"),
            (5, "GH_PwBoostAggressiveGuaranteed"), (6, "GH_PwBoostEfficientAggressiveGuaranteed"),
        }),
        new("Cooling", SubProcessor, SetCoolingPolicy, "GH_PwCooling", "", 0, 1, new[]
        {
            (0, "GH_PwCoolingPassive"), (1, "GH_PwCoolingActive"),
        }),
        new("Pcie", SubPciExpress, SetPciAspm, "GH_PwPcie", "", 0, 2, new[]
        {
            (0, "GH_PwPcieOff"), (1, "GH_PwPcieModerate"), (2, "GH_PwPcieMax"),
        }),
        new("UsbSuspend", SubUsb, SetUsbSuspend, "GH_PwUsbSuspend", "", 0, 1, new[]
        {
            (0, "GH_PwDisabled"), (1, "GH_PwEnabled"),
        }),
        new("Sleep", SubSleep, SetStandbyIdle, "GH_PwSleep", "min", 0, 300, Array.Empty<(int, string)>()),
        new("Video", SubVideo, SetVideoIdle, "GH_PwVideo", "min", 0, 300, Array.Empty<(int, string)>()),
        new("Disk", SubDisk, SetDiskIdle, "GH_PwDisk", "min", 0, 300, Array.Empty<(int, string)>()),
    };

    /// <summary>Configurações cujo valor é dado em segundos pelo powercfg mas exibido em minutos.</summary>
    private static bool IsMinuteSetting(string settingGuid) =>
        settingGuid is SetStandbyIdle or SetVideoIdle or SetDiskIdle;

    // =====================================================================================
    //  Planos
    // =====================================================================================

    /// <summary>Lista todos os planos disponíveis (inclusive os personalizados do usuário).</summary>
    public async Task<IReadOnlyList<PowerPlanInfo>> ListPlansAsync()
    {
        var result = new List<PowerPlanInfo>();
        var (_, output) = await RunAsync("powercfg", "/list");

        foreach (var line in output.Split('\n'))
        {
            var guidMatch = Regex.Match(line, @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
            if (!guidMatch.Success) continue;

            // O nome vem entre parênteses; o asterisco no fim marca o plano ativo.
            var nameMatch = Regex.Match(line, @"\(([^)]*)\)");
            string name = nameMatch.Success ? nameMatch.Groups[1].Value.Trim() : guidMatch.Value;
            bool active = line.TrimEnd().EndsWith("*");

            result.Add(new PowerPlanInfo(guidMatch.Value.ToLowerInvariant(), name, active));
        }

        return result;
    }

    /// <summary>GUID do plano ativo agora (consultado no Windows, nunca guardado pelo app).</summary>
    public async Task<PowerPlanInfo?> GetActivePlanAsync()
    {
        var (_, output) = await RunAsync("powercfg", "/getactivescheme");
        var guid = Regex.Match(output, @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
        if (!guid.Success) return null;
        var name = Regex.Match(output, @"\(([^)]*)\)");
        return new PowerPlanInfo(guid.Value.ToLowerInvariant(), name.Success ? name.Groups[1].Value.Trim() : guid.Value, true);
    }

    /// <summary>Ativa um plano pelo GUID.</summary>
    public async Task<bool> SetActivePlanAsync(string guid)
    {
        var (code, _) = await RunAsync("powercfg", $"/setactive {guid}");
        return code == 0;
    }

    /// <summary>
    /// Garante que o plano "Ultimate Performance" exista (ele vem oculto no Windows) e devolve o
    /// GUID. O nome varia por idioma, então procuramos por vários fragmentos antes de duplicar o
    /// template — evitando criar um plano novo a cada chamada.
    /// </summary>
    public async Task<string?> EnsureUltimatePlanAsync()
    {
        var plans = await ListPlansAsync();
        string[] fragments = { "Ultimate", "Máximo", "Maximo", "Maximum" };
        var existing = plans.FirstOrDefault(p => fragments.Any(f => p.Name.Contains(f, StringComparison.OrdinalIgnoreCase)));
        if (existing is not null) return existing.Guid;

        var (_, output) = await RunAsync("powercfg", $"/duplicatescheme {UltimateTemplateGuid}");
        var guid = Regex.Match(output, @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
        return guid.Success ? guid.Value.ToLowerInvariant() : null;
    }

    // =====================================================================================
    //  Configurações avançadas
    // =====================================================================================

    /// <summary>
    /// Lê o índice atual (AC e DC) de uma configuração avançada no plano informado
    /// (null = plano ativo). Devolve null quando a configuração não existe nesta máquina.
    /// </summary>
    public async Task<(int? ac, int? dc)> ReadSettingAsync(string subGroup, string setting, string? planGuid = null)
    {
        string scheme = planGuid ?? "SCHEME_CURRENT";
        var (_, output) = await RunAsync("powercfg", $"/query {scheme} {subGroup} {setting}");

        // O powercfg escreve na página de código do console (CP850 em português), não em UTF-8,
        // então as palavras acentuadas do relatório chegam corrompidas. Por isso NÃO dependemos de
        // acento nenhum para achar as linhas: identificamos as duas linhas de índice por um trecho
        // sem acento ("Setting Index" em inglês, "ndice" em português) e usamos a ordem em que o
        // powercfg sempre as imprime — primeiro a da tomada (AC), depois a da bateria (DC).
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

            // Quando o rótulo é legível, ele manda; senão, vale a ordem.
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
        // Com o rótulo de AC reconhecido, a linha restante sem rótulo legível só pode ser a de DC.
        if (dc is null && ac is not null && indexes.Count == 1) dc = indexes[0];

        if (ac is not null && IsMinuteSetting(setting)) ac /= 60;
        if (dc is not null && IsMinuteSetting(setting)) dc /= 60;
        return (ac, dc);
    }

    /// <summary>
    /// Grava uma configuração avançada. Quando <paramref name="dcValue"/> é null, o mesmo valor é
    /// usado na bateria — para um jogo, faz sentido o desempenho não cair ao desconectar a tomada.
    /// O plano precisa ser reativado para o Windows aplicar de fato (é o que o /setactive faz).
    /// </summary>
    public async Task<bool> WriteSettingAsync(string subGroup, string setting, int acValue, int? dcValue = null, string? planGuid = null)
    {
        string scheme = planGuid ?? "SCHEME_CURRENT";
        int ac = IsMinuteSetting(setting) ? acValue * 60 : acValue;
        int dc = IsMinuteSetting(setting) ? (dcValue ?? acValue) * 60 : (dcValue ?? acValue);

        var (c1, _) = await RunAsync("powercfg", $"/setacvalueindex {scheme} {subGroup} {setting} {ac}");
        var (c2, _) = await RunAsync("powercfg", $"/setdcvalueindex {scheme} {subGroup} {setting} {dc}");
        await RunAsync("powercfg", $"/setactive {scheme}");
        return c1 == 0 && c2 == 0;
    }

    /// <summary>
    /// Torna visíveis, no Painel de Controle e no powercfg, configurações que o Windows esconde por
    /// padrão (CPU Boost e política de resfriamento são as mais comuns). Sem isto, gravar o valor
    /// funciona mas o usuário não consegue conferir pelo Windows. É reversível (ATTRIB_HIDE).
    /// </summary>
    public async Task UnhideAdvancedSettingsAsync()
    {
        foreach (var def in AdvancedSettings)
            await RunAsync("powercfg", $"-attributes {def.SubGroupGuid} {def.SettingGuid} -ATTRIB_HIDE");
    }

    /// <summary>
    /// Lê, do plano ativo, todos os valores que o perfil informado pretende mudar — é o que alimenta
    /// o snapshot antes de aplicar qualquer coisa.
    /// </summary>
    public async Task<List<PowerSettingValue>> CaptureAsync(IEnumerable<(string sub, string setting, string label)> wanted)
    {
        var list = new List<PowerSettingValue>();
        foreach (var (sub, setting, label) in wanted)
        {
            var (ac, dc) = await ReadSettingAsync(sub, setting);
            if (ac is null && dc is null) continue;
            list.Add(new PowerSettingValue
            {
                SubGroupGuid = sub,
                SettingGuid = setting,
                Label = label,
                AcValue = ac,
                DcValue = dc,
            });
        }
        return list;
    }

    /// <summary>Devolve ao plano informado os valores guardados no snapshot.</summary>
    public async Task RestoreAsync(IEnumerable<PowerSettingValue> values, string? planGuid = null)
    {
        foreach (var v in values)
        {
            if (v.AcValue is null && v.DcValue is null) continue;
            await WriteSettingAsync(v.SubGroupGuid, v.SettingGuid,
                v.AcValue ?? v.DcValue!.Value, v.DcValue, planGuid);
        }
    }

    // =====================================================================================

    internal static async Task<(int code, string output)> RunAsync(string exe, string args)
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
                // A saída vem na página de código do console (CP850 em português), não em UTF-8.
                StandardOutputEncoding = ConsoleEncoding.Oem,
                StandardErrorEncoding = ConsoleEncoding.Oem,
            };
            using var proc = Process.Start(psi)!;
            string output = await proc.StandardOutput.ReadToEndAsync();
            output += await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();
            return (proc.ExitCode, output);
        }
        catch (Exception ex)
        {
            return (1, ex.Message);
        }
    }
}
