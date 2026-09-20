using System.Diagnostics;
using System.Management;

namespace Pulse1x.App.Services.Profiles;

/// <summary>Um modo de desempenho oferecido pelo software do fabricante.</summary>
public record OemMode(string Id, string LabelKey);

/// <summary>
/// Contrato de integração com o software/firmware do fabricante do notebook. Cada fabricante tem
/// seu próprio mecanismo, então cada um vira um adaptador independente — adicionar suporte a um
/// novo modelo é escrever mais uma classe, sem tocar no resto do GameHub.
/// </summary>
public interface IOemVendorAdapter
{
    string VendorId { get; }
    string DisplayName { get; }

    /// <summary>Modos que este fabricante expõe (pode variar por modelo, via <see cref="Probe"/>).</summary>
    IReadOnlyList<OemMode> Modes { get; }

    /// <summary>
    /// Verifica, sem alterar nada, se esta máquina realmente responde ao mecanismo do adaptador.
    /// É obrigatório que uma leitura bem-sucedida aconteça aqui ANTES de qualquer escrita: se o
    /// modelo não usa a mesma codificação, o adaptador se declara indisponível em vez de escrever
    /// um valor desconhecido no firmware.
    /// </summary>
    bool Probe();

    /// <summary>Modo ativo agora, ou null se não for possível ler.</summary>
    string? GetCurrentMode();

    /// <summary>Aplica o modo e confirma relendo. Retorna false se não deu para confirmar.</summary>
    bool SetMode(string modeId);
}

/// <summary>
/// Detecta o fabricante desta máquina e expõe o adaptador correspondente para os perfis do GameHub.
/// Se nenhum adaptador nativo funcionar, o adaptador de comandos personalizados continua disponível
/// — assim qualquer máquina pode acionar o utilitário do próprio fabricante.
/// </summary>
public class OemVendorService
{
    private readonly List<IOemVendorAdapter> _adapters = new();
    private IOemVendorAdapter? _active;
    private bool _probed;

    public OemVendorService(OemCustomCommands? customCommands = null)
    {
        _adapters.Add(new AcerGamingAdapter());
        _adapters.Add(new LenovoGameZoneAdapter());
        _adapters.Add(new AsusArmouryAdapter());
        _adapters.Add(new OemCommandAdapter(customCommands ?? new OemCustomCommands()));
    }

    /// <summary>Fabricante informado pela placa-mãe (só para exibição e para priorizar adaptadores).</summary>
    public static string SystemManufacturer
    {
        get
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Manufacturer FROM Win32_ComputerSystem");
                foreach (ManagementObject mo in searcher.Get())
                    return (mo["Manufacturer"] as string ?? "").Trim();
            }
            catch { }
            return "";
        }
    }

    /// <summary>
    /// Adaptador que funciona nesta máquina (só sonda uma vez por sessão — a sondagem lê o firmware
    /// e não precisa ser repetida).
    /// </summary>
    public IOemVendorAdapter? Active
    {
        get
        {
            if (_probed) return _active;
            _probed = true;

            string manufacturer = SystemManufacturer;
            // Tenta primeiro o adaptador do fabricante desta máquina, depois os demais.
            var ordered = _adapters
                .OrderByDescending(a => manufacturer.Contains(a.VendorId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var adapter in ordered)
            {
                try
                {
                    if (adapter.Probe()) { _active = adapter; break; }
                }
                catch { /* um adaptador que falha nunca impede os outros de serem testados */ }
            }

            return _active;
        }
    }

    /// <summary>Força uma nova sondagem (usado pelo botão de diagnóstico do editor de perfil).</summary>
    public void Rescan()
    {
        _probed = false;
        _active = null;
    }

    public bool IsAvailable => Active is not null;
    public IReadOnlyList<OemMode> AvailableModes => Active?.Modes ?? Array.Empty<OemMode>();
    public string? VendorId => Active?.VendorId;
    public string? VendorName => Active?.DisplayName;

    public string? GetCurrentMode() => Active?.GetCurrentMode();

    public bool SetMode(string modeId)
    {
        var adapter = Active;
        if (adapter is null) return false;
        try { return adapter.SetMode(modeId); }
        catch { return false; }
    }
}

// =====================================================================================
//  Acer (NitroSense / PredatorSense)
// =====================================================================================

/// <summary>
/// Acer Nitro/Predator. Os modos do NitroSense ficam no firmware, acessíveis pela classe WMI
/// <c>AcerGamingFunction</c> (namespace root\WMI) — a mesma interface que o driver acer-wmi do
/// Linux usa. As leituras/escritas passam por "Misc Settings":
///
///   entrada  = índice | (valor &lt;&lt; 8)
///   saída    = status (bits 0-7, 0 = OK) | valor (bits 8-15)
///
/// Índice 0x0A lista os perfis suportados pelo modelo (bitmask) e 0x0B é o perfil ativo. A
/// sondagem só aceita o adaptador quando AMBAS as leituras retornam status 0 — assim, num modelo
/// com codificação diferente, nada é escrito.
/// </summary>
public class AcerGamingAdapter : IOemVendorAdapter
{
    private const string Namespace = @"root\WMI";
    private const string ClassName = "AcerGamingFunction";

    private const uint MiscSupportedProfiles = 0x0A;
    private const uint MiscPlatformProfile = 0x0B;

    // Valores de perfil térmico usados pelo firmware Acer. O 0x06 aparece nos Nitro recentes
    // (confirmado num ANV15-51, cujo bitmask de suportados é 0b1010011 — bits 0, 1, 4 e 6).
    private const int Quiet = 0x00, Balanced = 0x01, Performance = 0x02, Turbo = 0x03, Eco = 0x04,
                      PerformanceAlt = 0x06;

    private List<OemMode> _modes = new();

    /// <summary>Bitmask de perfis que ESTE firmware aceita, lido na sondagem.</summary>
    private int _supportedMask;

    public string VendorId => "acer";
    public string DisplayName => "Acer NitroSense / PredatorSense";
    public IReadOnlyList<OemMode> Modes => _modes;

    private static readonly (int Value, string Id, string LabelKey)[] AllModes =
    {
        (Eco, "eco", "GH_OemEco"),
        (Quiet, "quiet", "GH_OemQuiet"),
        (Balanced, "balanced", "GH_OemBalanced"),
        (Performance, "performance", "GH_OemPerformance"),
        (PerformanceAlt, "performance", "GH_OemPerformance"),
        (Turbo, "turbo", "GH_OemTurbo"),
    };

    public bool Probe()
    {
        // Lê os perfis suportados. Se o firmware não responder com status 0, o modelo não usa
        // esta codificação — desistimos sem nunca escrever nada.
        if (!TryGetMisc(MiscSupportedProfiles, out int supportedMask)) return false;
        if (!TryGetMisc(MiscPlatformProfile, out int current)) return false;

        // O perfil ativo precisa ser um valor plausível; caso contrário, desconfiamos da leitura.
        if (AllModes.All(m => m.Value != current)) return false;

        _supportedMask = supportedMask;

        // O bitmask diz quais perfis este modelo aceita (bit N = perfil N). Alguns firmwares
        // devolvem 0 aqui; nesse caso oferecemos o conjunto completo, que é o comportamento do
        // próprio NitroSense.
        //
        // Um mesmo modo pode ter mais de um valor conforme a geração (Desempenho é 0x02 ou 0x06),
        // então a lista é deduplicada pelo Id — senão "Desempenho" apareceria duas vezes.
        _modes = AllModes
            .Where(m => supportedMask == 0 || (supportedMask & (1 << m.Value)) != 0)
            .GroupBy(m => m.Id)
            .Select(g => new OemMode(g.Key, g.First().LabelKey))
            .ToList();

        return _modes.Count > 0;
    }

    public string? GetCurrentMode()
    {
        if (!TryGetMisc(MiscPlatformProfile, out int value)) return null;
        return AllModes.FirstOrDefault(m => m.Value == value).Id;
    }

    public bool SetMode(string modeId)
    {
        // Entre os valores possíveis para este modo, usa o que o firmware declarou suportar —
        // Desempenho é 0x02 numa geração e 0x06 noutra, e escrever o errado não faz nada.
        var candidates = AllModes
            .Where(m => m.Id == modeId)
            .Where(m => _supportedMask == 0 || (_supportedMask & (1 << m.Value)) != 0)
            .ToList();

        if (candidates.Count == 0) return false;

        foreach (var mode in candidates)
        {
            if (!TrySetMisc(MiscPlatformProfile, mode.Value)) continue;

            // Confirmação: relê e compara. Sem isso não temos como saber se o firmware aceitou.
            if (TryGetMisc(MiscPlatformProfile, out int readBack) && readBack == mode.Value)
                return true;
        }

        return false;
    }

    // ---- WMI ----

    private static bool TryGetMisc(uint index, out int value)
    {
        value = 0;
        try
        {
            ulong result = InvokeU64("GetGamingMiscSetting", index);
            if ((result & 0xFF) != 0) return false;       // status != 0 → falhou
            value = (int)((result >> 8) & 0xFF);
            return true;
        }
        catch { return false; }
    }

    private static bool TrySetMisc(uint index, int value)
    {
        try
        {
            ulong input = index | ((ulong)(uint)value << 8);
            InvokeU64("SetGamingMiscSetting", input);
            return true;
        }
        catch { return false; }
    }

    private static ulong InvokeU64(string method, ulong input)
    {
        var scope = new ManagementScope(Namespace);
        scope.Connect();

        using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery($"SELECT * FROM {ClassName}"));
        foreach (ManagementObject instance in searcher.Get())
        {
            using (instance)
            {
                var inParams = instance.GetMethodParameters(method);
                inParams["gmInput"] = input;
                using var outParams = instance.InvokeMethod(method, inParams, null);
                return Convert.ToUInt64(outParams["gmOutput"]);
            }
        }
        throw new InvalidOperationException($"{ClassName}: nenhuma instância disponível.");
    }
}

// =====================================================================================
//  Lenovo (Vantage / Legion)
// =====================================================================================

/// <summary>
/// Lenovo Legion/IdeaPad. O modo térmico do Vantage é o "Smart Fan Mode" exposto em
/// <c>LENOVO_GAMEZONE_DATA</c> (root\WMI): 1 = Silencioso, 2 = Balanceado, 3 = Desempenho.
/// </summary>
public class LenovoGameZoneAdapter : IOemVendorAdapter
{
    private const string Namespace = @"root\WMI";
    private const string ClassName = "LENOVO_GAMEZONE_DATA";

    public string VendorId => "lenovo";
    public string DisplayName => "Lenovo Vantage / Legion";

    public IReadOnlyList<OemMode> Modes { get; } = new[]
    {
        new OemMode("quiet", "GH_OemQuiet"),
        new OemMode("balanced", "GH_OemBalanced"),
        new OemMode("performance", "GH_OemPerformance"),
    };

    private static int ToValue(string id) => id switch
    {
        "quiet" or "eco" => 1,
        "balanced" => 2,
        "performance" or "turbo" => 3,
        _ => 0,
    };

    private static string? ToId(int value) => value switch
    {
        1 => "quiet",
        2 => "balanced",
        3 => "performance",
        _ => null,
    };

    public bool Probe() => GetCurrentMode() is not null;

    public string? GetCurrentMode()
    {
        try
        {
            var result = Invoke("GetSmartFanMode", null);
            return result is null ? null : ToId(Convert.ToInt32(result["Data"]));
        }
        catch { return null; }
    }

    public bool SetMode(string modeId)
    {
        int value = ToValue(modeId);
        if (value == 0) return false;
        try
        {
            Invoke("SetSmartFanMode", new Dictionary<string, object> { ["Data"] = value });
            return GetCurrentMode() == ToId(value);
        }
        catch { return false; }
    }

    private static ManagementBaseObject? Invoke(string method, Dictionary<string, object>? args)
    {
        var scope = new ManagementScope(Namespace);
        scope.Connect();
        using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery($"SELECT * FROM {ClassName}"));
        foreach (ManagementObject instance in searcher.Get())
        {
            using (instance)
            {
                var inParams = instance.GetMethodParameters(method);
                if (args is not null)
                    foreach (var (k, v) in args) inParams[k] = v;
                return instance.InvokeMethod(method, inParams, null);
            }
        }
        return null;
    }
}

// =====================================================================================
//  ASUS (Armoury Crate)
// =====================================================================================

/// <summary>
/// ASUS ROG/TUF. O "Throttle Thermal Policy" do Armoury Crate fica no ATK WMI
/// (<c>AsusAtkWmi_WMNB</c>, root\WMI), device id 0x00120075: 0 = Balanceado, 1 = Turbo,
/// 2 = Silencioso. Escrita por DEVS, leitura por DSTS.
/// </summary>
public class AsusArmouryAdapter : IOemVendorAdapter
{
    private const string Namespace = @"root\WMI";
    private const string ClassName = "AsusAtkWmi_WMNB";
    private const uint ThrottleThermalPolicy = 0x00120075;

    public string VendorId => "asus";
    public string DisplayName => "ASUS Armoury Crate";

    public IReadOnlyList<OemMode> Modes { get; } = new[]
    {
        new OemMode("quiet", "GH_OemQuiet"),
        new OemMode("balanced", "GH_OemBalanced"),
        new OemMode("turbo", "GH_OemTurbo"),
    };

    private static int ToValue(string id) => id switch
    {
        "balanced" => 0,
        "turbo" or "performance" => 1,
        "quiet" or "eco" => 2,
        _ => -1,
    };

    private static string? ToId(int value) => value switch
    {
        0 => "balanced",
        1 => "turbo",
        2 => "quiet",
        _ => null,
    };

    public bool Probe() => GetCurrentMode() is not null;

    public string? GetCurrentMode()
    {
        try
        {
            var result = Invoke("DSTS", new Dictionary<string, object> { ["Device_ID"] = ThrottleThermalPolicy });
            if (result is null) return null;
            // O retorno traz um bit de "suportado" (0x00010000) somado ao valor.
            int raw = Convert.ToInt32(result["result"]);
            if ((raw & 0x00010000) == 0) return null;
            return ToId(raw & 0xFF);
        }
        catch { return null; }
    }

    public bool SetMode(string modeId)
    {
        int value = ToValue(modeId);
        if (value < 0) return false;
        try
        {
            Invoke("DEVS", new Dictionary<string, object>
            {
                ["Device_ID"] = ThrottleThermalPolicy,
                ["Control_status"] = (uint)value,
            });
            return GetCurrentMode() == ToId(value);
        }
        catch { return false; }
    }

    private static ManagementBaseObject? Invoke(string method, Dictionary<string, object> args)
    {
        var scope = new ManagementScope(Namespace);
        scope.Connect();
        using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery($"SELECT * FROM {ClassName}"));
        foreach (ManagementObject instance in searcher.Get())
        {
            using (instance)
            {
                var inParams = instance.GetMethodParameters(method);
                foreach (var (k, v) in args) inParams[k] = v;
                return instance.InvokeMethod(method, inParams, null);
            }
        }
        return null;
    }
}

// =====================================================================================
//  Comandos personalizados (qualquer fabricante)
// =====================================================================================

/// <summary>Comando executado para cada modo, definido pelo usuário nas Configurações.</summary>
public class OemCustomCommands
{
    public string VendorName { get; set; } = "";
    /// <summary>modo → linha de comando (ex.: "msicenter.exe /profile turbo").</summary>
    public Dictionary<string, string> Commands { get; set; } = new();
}

/// <summary>
/// Adaptador universal: aciona o utilitário do fabricante por linha de comando. Existe para os
/// casos em que não há interface documentada (MSI Center, Alienware Command Center e afins) —
/// o usuário informa o comando de cada modo uma vez e os perfis passam a usá-lo normalmente.
/// </summary>
public class OemCommandAdapter : IOemVendorAdapter
{
    private readonly OemCustomCommands _config;
    private string? _lastApplied;

    public OemCommandAdapter(OemCustomCommands config) => _config = config;

    public string VendorId => "custom";
    public string DisplayName => string.IsNullOrWhiteSpace(_config.VendorName)
        ? Localization.Loc.S("GH_OemCustomVendor")
        : _config.VendorName;

    public IReadOnlyList<OemMode> Modes => _config.Commands.Keys
        .Select(k => new OemMode(k, k switch
        {
            "eco" => "GH_OemEco",
            "quiet" => "GH_OemQuiet",
            "balanced" => "GH_OemBalanced",
            "performance" => "GH_OemPerformance",
            "turbo" => "GH_OemTurbo",
            _ => k,
        }))
        .ToList();

    public bool Probe() => _config.Commands.Count > 0;

    /// <summary>Um comando externo não tem leitura; lembramos o último modo aplicado nesta sessão.</summary>
    public string? GetCurrentMode() => _lastApplied;

    public bool SetMode(string modeId)
    {
        if (!_config.Commands.TryGetValue(modeId, out var command) || string.IsNullOrWhiteSpace(command))
            return false;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {command}",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            _lastApplied = modeId;
            return true;
        }
        catch { return false; }
    }
}
