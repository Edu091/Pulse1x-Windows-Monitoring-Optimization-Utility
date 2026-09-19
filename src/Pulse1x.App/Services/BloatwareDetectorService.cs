using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace Pulse1x.App.Services;

/// <summary>Natureza do item detectado — define como ele é analisado e qual ação (se houver) é oferecida.</summary>
public enum BloatKind
{
    App,            // aplicativo instalado (Win32 ou UWP/pré-instalado)
    Service,        // serviço do Windows ou de terceiros
    ScheduledTask,  // tarefa agendada
    StartupItem,    // item de inicialização (chave Run do Registro)
}

/// <summary>Impacto estimado do item no sistema (consumo de RAM/CPU/disco/rede/inicialização).</summary>
public enum ImpactLevel { VeryLow, Low, Medium, High, VeryHigh }

/// <summary>Risco de desativar/remover o item.</summary>
public enum BloatRiskLevel { Safe, Caution, NotRecommended }

/// <summary>Recomendação final do Pulse1x para o item.</summary>
public enum RecommendationLevel { Remove, Optional, Keep }

/// <summary>
/// Um item detectado pelo Detector de Bloatware (aplicativo, serviço, tarefa agendada ou item de
/// inicialização) já enriquecido com a análise educativa: o que é, quem instalou, impacto, risco e
/// recomendação. Nenhuma alteração é feita ao detectar — o item apenas descreve a si mesmo.
/// </summary>
public class BloatwareItem
{
    /// <summary>Identificador estável (ex.: "service:DiagTrack") usado em preferências e no log.</summary>
    public required string Key { get; init; }
    public required BloatKind Kind { get; init; }

    public string Name { get; set; } = "";
    public string Vendor { get; set; } = "Desconhecido";
    public string CategoryLabel { get; set; } = "";
    public string Description { get; set; } = "";

    public ImpactLevel Impact { get; set; } = ImpactLevel.Low;
    public BloatRiskLevel Risk { get; set; } = BloatRiskLevel.Caution;
    public RecommendationLevel Recommendation { get; set; } = RecommendationLevel.Keep;

    public long SizeBytes { get; set; }            // 0 = desconhecido
    public bool RunsInBackground { get; set; }
    public string StartupImpact { get; set; } = "Nenhum";

    /// <summary>Itens críticos nunca exibem ações perigosas (apenas informação).</summary>
    public bool IsCritical { get; set; }

    /// <summary>Estado atual no sistema (ativo/instalado). Atualizado após uma ação.</summary>
    public bool CurrentlyEnabled { get; set; } = true;

    // ---- Identificadores técnicos para as ações ----
    public string? ServiceName { get; set; }
    public string? TaskPath { get; set; }
    public bool IsUwp { get; set; }
    public string? PackageFullName { get; set; }   // UWP
    public string? UninstallString { get; set; }   // Win32
    /// <summary>Comando silencioso oficial, quando o próprio programa registra um.</summary>
    public string? QuietUninstallString { get; set; }
    public string? StartupHive { get; set; }        // "HKLM" | "HKCU"
    public string? StartupKeyPath { get; set; }
    public string? StartupValueName { get; set; }
    public string? StartupValueData { get; set; }

    // ---- "Mais Informações" (educativo) ----
    public string WhatItIs { get; set; } = "";
    public string WhoInstalled { get; set; } = "";
    public string WhenUsed { get; set; } = "";
    public string BenefitsKeeping { get; set; } = "";
    public string BenefitsRemoving { get; set; } = "";
    public string Consequences { get; set; } = "";

    /// <summary>Pode ser desativado de forma reversível (serviço/tarefa/inicialização).</summary>
    public bool CanDisable => Kind is BloatKind.Service or BloatKind.ScheduledTask or BloatKind.StartupItem && !IsCritical;

    /// <summary>Pode ser desinstalado (aplicativo) — abre o desinstalador oficial ou remove o pacote UWP.</summary>
    public bool CanUninstall => Kind is BloatKind.App && !IsCritical;
}

/// <summary>Preferências por item (ignorados), persistidas entre sessões.</summary>
public class BloatwarePrefs
{
    public HashSet<string> Ignored { get; set; } = new();
}

/// <summary>
/// Detector de Bloatware. Identifica aplicativos pré-instalados/OEM, serviços ocultos, tarefas
/// agendadas e itens de inicialização desnecessários, explicando para cada um o que faz, seu impacto
/// e o risco de removê-lo.
///
/// PRIORIDADE ABSOLUTA: SEGURANÇA. A detecção é somente leitura. Nenhuma alteração é feita
/// automaticamente — toda ação parte de um clique explícito do usuário, é confirmada, registrada no
/// histórico (<see cref="OptimizationChangeLog"/>) e, quando possível, reversível. Drivers, serviços
/// críticos e componentes essenciais do Windows nunca recebem ações destrutivas (marcados
/// <see cref="BloatwareItem.IsCritical"/>); serviços e tarefas só são listados quando reconhecidos
/// pela base curada, evitando que o usuário desative algo essencial por engano.
/// </summary>
public class BloatwareDetectorService
{
    private readonly AdvancedOptimizationService _advanced;
    private readonly OptimizationChangeLog _log;
    private readonly SilentUninstallService _uninstaller = new();
    private readonly LeftoverCleanupService _leftovers = new();
    private readonly string _prefsPath;
    private BloatwarePrefs _prefs = new();

    private const string ServicesPath = @"SYSTEM\CurrentControlSet\Services";

    public BloatwareDetectorService(AdvancedOptimizationService advanced)
    {
        _advanced = advanced;
        _log = advanced.ChangeLog;

        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pulse1x");
        Directory.CreateDirectory(dir);
        _prefsPath = Path.Combine(dir, "bloatware-prefs.json");
        LoadPrefs();
    }

    // ===================== Verredura =====================

    /// <summary>
    /// Varre o sistema (somente leitura) e devolve todos os itens detectados, já analisados.
    /// Itens não reconhecidos pela base curada são classificados de forma conservadora ("Manter").
    /// </summary>
    public async Task<IReadOnlyList<BloatwareItem>> ScanAsync()
    {
        var items = new List<BloatwareItem>();

        items.AddRange(await ScanUwpAppsAsync());
        items.AddRange(ScanWin32Apps());
        items.AddRange(ScanServices());
        items.AddRange(await ScanScheduledTasksAsync());
        items.AddRange(ScanStartupItems());

        // Remove duplicados pelo Key (um app pode aparecer em escopos de usuário e máquina).
        var unique = items
            .GroupBy(i => i.Key)
            .Select(g => g.First())
            .OrderByDescending(i => (int)i.Recommendation == 0) // "Remover" primeiro
            .ThenByDescending(i => i.Impact)
            .ToList();

        return unique;
    }

    public bool IsIgnored(string key) => _prefs.Ignored.Contains(key);

    public void SetIgnored(string key, bool ignored)
    {
        if (ignored) _prefs.Ignored.Add(key); else _prefs.Ignored.Remove(key);
        SavePrefs();
    }

    // ===================== Aplicativos UWP / pré-instalados =====================

    private async Task<List<BloatwareItem>> ScanUwpAppsAsync()
    {
        var result = new List<BloatwareItem>();
        var (code, output) = await RunAsync("powershell",
            "-NoProfile -ExecutionPolicy Bypass -Command " +
            "\"Get-AppxPackage | Where-Object { -not $_.IsFramework -and -not $_.NonRemovable } | " +
            "Select-Object Name,PackageFullName,Publisher | ConvertTo-Csv -NoTypeInformation\"");
        if (code != 0) return result;

        foreach (var line in output.Split('\n'))
        {
            var cells = ParseCsvLine(line);
            if (cells.Count < 2) continue;
            string name = cells[0];
            string pfn = cells[1];
            string? publisher = cells.Count > 2 ? cells[2] : null;
            if (name.Equals("Name", StringComparison.OrdinalIgnoreCase) || name.Length == 0) continue;

            // Mesma lógica de duas vias do scan Win32: base curada primeiro, heurística OEM como
            // reconhecimento adicional (nunca para termos de driver/firmware).
            var sig = MatchApp(name) ?? HeuristicOemSig(FriendlyAppName(name), publisher);
            if (sig is null) continue;

            result.Add(BuildItem($"app:{name}", BloatKind.App, FriendlyAppName(name), sig, item =>
            {
                item.IsUwp = true;
                item.PackageFullName = pfn;
            }));
        }
        return result;
    }

    // ===================== Aplicativos Win32 (Registro de Desinstalação) =====================

    private static readonly (RegistryHive hive, string path)[] UninstallRoots =
    {
        (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
        (RegistryHive.CurrentUser,  @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
    };

    private List<BloatwareItem> ScanWin32Apps()
    {
        var result = new List<BloatwareItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (hive, path) in UninstallRoots)
        {
            try
            {
                var root = hive == RegistryHive.LocalMachine ? Registry.LocalMachine : Registry.CurrentUser;
                using var key = root.OpenSubKey(path);
                if (key is null) continue;

                foreach (var subName in key.GetSubKeyNames())
                {
                    using var sub = key.OpenSubKey(subName);
                    if (sub is null) continue;

                    string? name = sub.GetValue("DisplayName") as string;
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    if ((sub.GetValue("SystemComponent") as int?) == 1) continue; // componente de sistema, não exibir
                    if (sub.GetValue("ParentKeyName") is not null) continue;       // atualização/patch
                    string? uninstall = sub.GetValue("UninstallString") as string;
                    if (string.IsNullOrWhiteSpace(uninstall)) continue;
                    string? quietUninstall = sub.GetValue("QuietUninstallString") as string;

                    string? publisher = sub.GetValue("Publisher") as string;

                    // Segurança: priorizamos a base curada (descrição completa e confiável). Quando o
                    // app não está catalogado, tentamos um reconhecimento heurístico — só dispara para
                    // publicadores de fabricantes de PC conhecidos E nomes com termos típicos de
                    // utilitário promocional/redundante, nunca para termos de driver/firmware (ver
                    // HeuristicOemSig) — programas instalados pelo próprio usuário não batem nenhuma
                    // das duas vias e, por isso, nunca aparecem aqui.
                    var sig = MatchApp(name) ?? HeuristicOemSig(name, publisher);
                    if (sig is null) continue;
                    if (!seen.Add(name)) continue;

                    long size = (sub.GetValue("EstimatedSize") as int? ?? 0) * 1024L;

                    var item = BuildItem($"app:{name}", BloatKind.App, name, sig, it =>
                    {
                        it.IsUwp = false;
                        it.UninstallString = uninstall;
                        it.QuietUninstallString = quietUninstall;
                        it.SizeBytes = size;
                        if (!string.IsNullOrWhiteSpace(publisher)) it.Vendor = publisher!;
                    });
                    result.Add(item);
                }
            }
            catch { /* chave inacessível: ignora esse escopo */ }
        }
        return result;
    }

    // ===================== Serviços (somente reconhecidos) =====================

    private List<BloatwareItem> ScanServices()
    {
        var result = new List<BloatwareItem>();
        try
        {
            using var services = Registry.LocalMachine.OpenSubKey(ServicesPath);
            if (services is null) return result;

            foreach (var name in services.GetSubKeyNames())
            {
                if (!ServiceSigs.TryGetValue(name.ToLowerInvariant(), out var sig)) continue;

                using var svc = services.OpenSubKey(name);
                if (svc is null) continue;
                int start = (svc.GetValue("Start") as int?) ?? 3;
                string display = svc.GetValue("DisplayName") as string ?? name;

                var item = BuildItem($"service:{name}", BloatKind.Service, display, sig, it =>
                {
                    it.ServiceName = name;
                    it.CurrentlyEnabled = start != 4; // 4 = Desativado
                });
                result.Add(item);
            }
        }
        catch { }
        return result;
    }

    // ===================== Tarefas Agendadas (somente reconhecidas) =====================

    private async Task<List<BloatwareItem>> ScanScheduledTasksAsync()
    {
        var result = new List<BloatwareItem>();
        var (code, output) = await RunAsync("schtasks", "/Query /FO CSV /NH");
        if (code != 0) return result;

        foreach (var line in output.Split('\n'))
        {
            var cells = ParseCsvLine(line);
            if (cells.Count < 2) continue;
            string taskPath = cells[0];
            if (string.IsNullOrWhiteSpace(taskPath) || taskPath == "TaskName") continue;
            string status = cells.Count > 2 ? cells[2] : "";

            var match = TaskSigs.FirstOrDefault(t => taskPath.Contains(t.fragment, StringComparison.OrdinalIgnoreCase));
            // Heurística: tarefas em pastas com nome do fabricante (ex.: "\Lenovo\Lenovo Now") e termo
            // típico de utilitário promocional — desativar uma tarefa agendada é totalmente reversível
            // (Reativar), por isso a barra de segurança aqui pode ser um pouco mais permissiva que para apps.
            var sig = match.sig ?? HeuristicOemTaskSig(taskPath);
            if (sig is null) continue;
            string key = $"task:{taskPath}";
            if (result.Any(r => r.Key == key)) continue;

            var item = BuildItem(key, BloatKind.ScheduledTask, TaskLeafName(taskPath), sig, it =>
            {
                it.TaskPath = taskPath;
                it.CurrentlyEnabled = !status.StartsWith("Desab", StringComparison.OrdinalIgnoreCase)
                                      && !status.StartsWith("Disab", StringComparison.OrdinalIgnoreCase);
            });
            result.Add(item);
        }
        return result;
    }

    // ===================== Itens de Inicialização (chaves Run) =====================

    private static readonly (RegistryHive hive, string path)[] RunRoots =
    {
        (RegistryHive.CurrentUser,  @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
        (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
        (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"),
    };

    private List<BloatwareItem> ScanStartupItems()
    {
        var result = new List<BloatwareItem>();
        foreach (var (hive, path) in RunRoots)
        {
            try
            {
                var root = hive == RegistryHive.LocalMachine ? Registry.LocalMachine : Registry.CurrentUser;
                using var key = root.OpenSubKey(path);
                if (key is null) continue;
                string hiveLabel = hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU";

                foreach (var valueName in key.GetValueNames())
                {
                    if (string.IsNullOrEmpty(valueName)) continue;
                    string data = key.GetValue(valueName)?.ToString() ?? "";

                    // Segurança: só exibimos itens de inicialização reconhecidos pela base curada;
                    // itens não reconhecidos (muitas vezes de programas seus) são ignorados.
                    var sig = MatchStartup(valueName, data);
                    if (sig is null) continue;
                    string itemKey = $"startup:{hiveLabel}:{valueName}";
                    if (result.Any(r => r.Key == itemKey)) continue;

                    var item = BuildItem(itemKey, BloatKind.StartupItem, valueName, sig, it =>
                    {
                        it.StartupHive = hiveLabel;
                        it.StartupKeyPath = path;
                        it.StartupValueName = valueName;
                        it.StartupValueData = data;
                    });
                    result.Add(item);
                }
            }
            catch { }
        }
        return result;
    }

    // ===================== Ações (sempre por clique explícito) =====================

    /// <summary>
    /// Desativa de forma reversível um serviço, tarefa agendada ou item de inicialização. A alteração
    /// é registrada no histórico para que possa ser desfeita (Reativar). Não remove nada.
    /// </summary>
    public async Task DisableAsync(BloatwareItem item)
    {
        if (item.IsCritical) return;

        switch (item.Kind)
        {
            case BloatKind.Service when item.ServiceName is not null:
                {
                    int? current = GetDword(RegistryHive.LocalMachine, $@"{ServicesPath}\{item.ServiceName}", "Start");
                    _log.Record(new OptimizationChange
                    {
                        OptimizationId = item.Key,
                        OptimizationTitle = item.Name,
                        Kind = ChangeKind.Registry,
                        Hive = "HKLM",
                        KeyPath = $@"{ServicesPath}\{item.ServiceName}",
                        ValueName = "Start",
                        ValueKind = "DWord",
                        OldValue = (current ?? 3).ToString(),
                        NewValue = "4",
                    });
                    SetDwordRaw(RegistryHive.LocalMachine, $@"{ServicesPath}\{item.ServiceName}", "Start", 4);
                    await RunAsync("sc", $"stop {item.ServiceName}");
                    item.CurrentlyEnabled = false;
                }
                break;

            case BloatKind.ScheduledTask when item.TaskPath is not null:
                {
                    var r = await RunAsync("schtasks", $"/Change /TN \"{item.TaskPath}\" /Disable");
                    if (r.code != 0) throw new InvalidOperationException("Não foi possível desativar a tarefa. Execute o Pulse1x como Administrador.");
                    _log.Record(new OptimizationChange
                    {
                        OptimizationId = item.Key,
                        OptimizationTitle = item.Name,
                        Kind = ChangeKind.Task,
                        KeyPath = item.TaskPath,
                        OldValue = "Habilitada",
                        NewValue = "Desabilitada",
                    });
                    item.CurrentlyEnabled = false;
                }
                break;

            case BloatKind.StartupItem when item.StartupKeyPath is not null && item.StartupValueName is not null:
                {
                    _log.Record(new OptimizationChange
                    {
                        OptimizationId = item.Key,
                        OptimizationTitle = item.Name,
                        Kind = ChangeKind.Registry,
                        Hive = item.StartupHive ?? "HKCU",
                        KeyPath = item.StartupKeyPath,
                        ValueName = item.StartupValueName,
                        ValueKind = "String",
                        OldValue = item.StartupValueData,
                        NewValue = null, // removido
                    });
                    DeleteValue(item.StartupHive == "HKLM" ? RegistryHive.LocalMachine : RegistryHive.CurrentUser,
                        item.StartupKeyPath, item.StartupValueName);
                    item.CurrentlyEnabled = false;
                }
                break;
        }
    }

    /// <summary>Reativa um item antes desativado, desfazendo as alterações registradas para ele.</summary>
    public async Task EnableAsync(BloatwareItem item)
    {
        foreach (var change in _log.GetActive(item.Key).Reverse())
            await _advanced.RevertChangeAsync(change);
        item.CurrentlyEnabled = true;
    }

    /// <summary>
    /// Desinstala um aplicativo. UWP sai pelo Remove-AppxPackage; Win32 vai pelo desinstalador que o
    /// próprio programa registrou, em modo silencioso quando o formato dele é reconhecido (MSI, NSIS,
    /// Inno Setup, InstallShield) e de forma interativa quando não é — o Pulse1x nunca apaga os
    /// arquivos de um programa por conta própria para "desinstalá-lo".
    ///
    /// A desinstalação não é revertida pelo Pulse1x: o app pode ser reinstalado pela loja ou pelo
    /// site do fabricante.
    /// </summary>
    public async Task<UninstallResult> UninstallAsync(BloatwareItem item)
    {
        if (item.IsCritical) return new UninstallResult(UninstallOutcome.Failed);

        if (item.IsUwp && item.PackageFullName is not null)
        {
            var (code, output) = await RunAsync("powershell",
                $"-NoProfile -ExecutionPolicy Bypass -Command \"Remove-AppxPackage -Package '{item.PackageFullName}'\"");
            if (code != 0) return new UninstallResult(UninstallOutcome.Failed, output.Trim());
            item.CurrentlyEnabled = false;
            return new UninstallResult(UninstallOutcome.SilentSuccess);
        }

        if (string.IsNullOrWhiteSpace(item.UninstallString))
            return new UninstallResult(UninstallOutcome.Failed);

        var result = await _uninstaller.UninstallAsync(item.UninstallString!, item.QuietUninstallString);
        if (result.Outcome == UninstallOutcome.SilentSuccess) item.CurrentlyEnabled = false;
        return result;
    }

    /// <summary>Procura o que um programa desinstalado deixou para trás (pastas de instalação e dados).</summary>
    public Task<IReadOnlyList<LeftoverItem>> ScanLeftoversAsync(BloatwareItem item) =>
        _leftovers.ScanAsync(item.Name, item.Vendor);

    /// <summary>Remove os restos escolhidos pelo usuário. Devolve quantos saíram e o espaço liberado.</summary>
    public Task<(int removed, long freedBytes)> RemoveLeftoversAsync(IEnumerable<LeftoverItem> items) =>
        _leftovers.RemoveAsync(items);

    /// <summary>Cria um ponto de restauração do sistema antes de uma alteração. Melhor esforço.</summary>
    public async Task<bool> CreateRestorePointAsync()
    {
        var (code, _) = await RunAsync("powershell",
            "-NoProfile -ExecutionPolicy Bypass -Command " +
            "\"Enable-ComputerRestore -Drive $env:SystemDrive; " +
            "Checkpoint-Computer -Description 'Pulse1x - Detector de Bloatware' -RestorePointType 'MODIFY_SETTINGS'\"");
        return code == 0;
    }

    // ===================== Base de conhecimento (curada) =====================

    private record Sig(
        string Vendor, string CategoryLabel, string Description,
        ImpactLevel Impact, BloatRiskLevel Risk, RecommendationLevel Rec,
        bool Background, string StartupImpact,
        string WhatItIs, string WhoInstalled, string WhenUsed,
        string BenefitsKeeping, string BenefitsRemoving, string Consequences,
        bool Critical = false,
        string? vendorEn = null, string? descriptionEn = null,
        string? whatItIsEn = null, string? whoInstalledEn = null, string? whenUsedEn = null,
        string? benefitsKeepingEn = null, string? benefitsRemovingEn = null, string? consequencesEn = null);

    private static readonly Dictionary<string, string> CategoryLabelEn = new()
    {
        ["Aplicativo"] = "Application",
        ["Aplicativo (OEM)"] = "Application (OEM)",
        ["Aplicativo (OEM, não catalogado)"] = "Application (OEM, not catalogued)",
        ["Serviço"] = "Service",
        ["Tarefa agendada"] = "Scheduled task",
        ["Tarefa agendada (OEM, não catalogada)"] = "Scheduled task (OEM, not catalogued)",
        ["Inicialização"] = "Startup",
    };
    private static readonly Dictionary<string, string> StartupImpactEn = new()
    {
        ["Nenhum"] = "None",
        ["Baixo"] = "Low",
        ["Médio"] = "Medium",
        ["Alto"] = "High",
        ["Muito Alto"] = "Very High",
    };

    private BloatwareItem BuildItem(string key, BloatKind kind, string name, Sig sig, Action<BloatwareItem> configure)
    {
        bool en = Pulse1x.App.Localization.Loc.Instance.Language == Pulse1x.App.Localization.AppLanguage.English;
        var item = new BloatwareItem
        {
            Key = key,
            Kind = kind,
            Name = name,
            Vendor = en ? (sig.vendorEn ?? sig.Vendor) : sig.Vendor,
            CategoryLabel = en ? (CategoryLabelEn.TryGetValue(sig.CategoryLabel, out var cl) ? cl : sig.CategoryLabel) : sig.CategoryLabel,
            Description = en ? (sig.descriptionEn ?? sig.Description) : sig.Description,
            Impact = sig.Impact,
            Risk = sig.Risk,
            Recommendation = sig.Rec,
            RunsInBackground = sig.Background,
            StartupImpact = en ? (StartupImpactEn.TryGetValue(sig.StartupImpact, out var si) ? si : sig.StartupImpact) : sig.StartupImpact,
            IsCritical = sig.Critical,
            WhatItIs = en ? (sig.whatItIsEn ?? sig.WhatItIs) : sig.WhatItIs,
            WhoInstalled = en ? (sig.whoInstalledEn ?? sig.WhoInstalled) : sig.WhoInstalled,
            WhenUsed = en ? (sig.whenUsedEn ?? sig.WhenUsed) : sig.WhenUsed,
            BenefitsKeeping = en ? (sig.benefitsKeepingEn ?? sig.BenefitsKeeping) : sig.BenefitsKeeping,
            BenefitsRemoving = en ? (sig.benefitsRemovingEn ?? sig.BenefitsRemoving) : sig.BenefitsRemoving,
            Consequences = en ? (sig.consequencesEn ?? sig.Consequences) : sig.Consequences,
        };
        configure(item);
        return item;
    }

    private Sig? MatchApp(string name)
    {
        foreach (var (fragment, sig) in AppSigs)
            if (name.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                return sig;
        foreach (var (fragment, sig) in OemAppSigs)
            if (name.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                return sig;
        return null;
    }

    // ---- Reconhecimento heurístico de utilitários OEM não catalogados individualmente ----
    // Só dispara quando o PUBLICADOR é um fabricante de PC conhecido E o nome contém um termo típico
    // de utilitário promocional/redundante — nunca para termos de driver/firmware (lista de exclusão
    // abaixo), para não sugerir a remoção de algo que o hardware realmente precisa.
    private static readonly string[] OemVendorKeywords =
    {
        "Dell", "Hewlett-Packard", "HP Inc", "Lenovo", "ASUSTeK", "ASUS", "Acer Incorporated", "Acer",
        "Micro-Star", "MSI", "Samsung Electronics", "Toshiba", "Gigabyte", "Razer Inc", "LG Electronics",
        "Huawei", "Xiaomi", "Vaio",
        "Positivo", "Avell", "Multilaser", "Compal", "Clevo", "Framework", "Dynabook", "Fujitsu",
        "Panasonic", "Alienware", "Medion",
    };

    private static readonly string[] OemEssentialKeywords =
    {
        "Driver", "BIOS", "Firmware", "Chipset", "Audio Driver", "Graphics Driver", "Touchpad",
        "Fingerprint", "Camera Driver", "Bluetooth Driver", "Wireless Driver", "WLAN Driver",
        "Trusted Platform", "Modem", "Realtek", "Synaptics", "Precision Touchpad",
        "Thunderbolt", "Intel Management", "Card Reader", "Power Manager Driver", "Hotkey Driver",
        "ACPI", "Serial IO", "Display Driver", "Network Driver",
    };

    private static readonly string[] OemBloatKeywords =
    {
        "Assist", "Companion", "Center", "Hub", "Suite", "Connect", "Link", "Promotion", "Welcome",
        "Experience", "Service Agent", "Registration", "Optimizer", "Advisor", "Now", "Vantage", "Care",
        "Gift", "Live Update", "Quick Access", "JumpStart", "Customer", "Premium",
        "Assistant", "Tips", "Recovery Media", "Trial", "Offer", "Recommended", "Partner",
        "Store", "Cloud Backup",
    };

    private static Sig? HeuristicOemSig(string name, string? publisher)
    {
        if (string.IsNullOrWhiteSpace(publisher)) return null;
        string? vendor = OemVendorKeywords.FirstOrDefault(v => publisher.Contains(v, StringComparison.OrdinalIgnoreCase));
        if (vendor is null) return null;
        if (OemEssentialKeywords.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase))) return null;
        if (!OemBloatKeywords.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase))) return null;

        return new Sig(vendor, "Aplicativo (OEM, não catalogado)",
            "Software pré-instalado pelo fabricante do computador, reconhecido pelo nome e publicador — não está na nossa base detalhada, então revise a descrição abaixo com atenção antes de remover.",
            ImpactLevel.Low, BloatRiskLevel.Caution, RecommendationLevel.Optional, true, "Baixo",
            $"Utilitário do fabricante ({vendor}), ainda não catalogado individualmente pelo Pulse1x.",
            "Fabricante do computador (OEM).", "Varia conforme o utilitário — confira o nome antes de remover.",
            "Pode oferecer recursos específicos do fabricante (suporte, atualizações, recursos extras de teclado/tela).",
            "Libera RAM e processos em segundo plano se você não usa esses recursos.",
            "Como não está na base detalhada, pesquise o nome do programa antes de remover em caso de dúvida.",
            descriptionEn: "Software pre-installed by the computer manufacturer, recognized by name and publisher — it isn't in our detailed catalog, so review the description below carefully before removing it.",
            whatItIsEn: $"Manufacturer utility ({vendor}), not yet catalogued individually by Pulse1x.",
            whoInstalledEn: "Computer manufacturer (OEM).", whenUsedEn: "Varies by utility — check the name before removing.",
            benefitsKeepingEn: "May offer manufacturer-specific features (support, updates, extra keyboard/display features).",
            benefitsRemovingEn: "Frees RAM and background processes if you don't use these features.",
            consequencesEn: "Since it isn't in the detailed catalog, research the program name before removing if in doubt.");
    }

    // Mesma heurística de vendor+keyword, aplicada ao CAMINHO da tarefa agendada (que normalmente
    // inclui o nome do fabricante como pasta, ex.: "\Lenovo\Lenovo Now").
    private static Sig? HeuristicOemTaskSig(string taskPath)
    {
        string? vendor = OemVendorKeywords.FirstOrDefault(v => taskPath.Contains(v, StringComparison.OrdinalIgnoreCase));
        if (vendor is null) return null;
        if (OemEssentialKeywords.Any(k => taskPath.Contains(k, StringComparison.OrdinalIgnoreCase))) return null;
        if (!OemBloatKeywords.Any(k => taskPath.Contains(k, StringComparison.OrdinalIgnoreCase))) return null;

        return new Sig(vendor, "Tarefa agendada (OEM, não catalogada)",
            "Tarefa agendada pelo utilitário do fabricante do computador, reconhecida pelo caminho — não está na nossa base detalhada.",
            ImpactLevel.Low, BloatRiskLevel.Caution, RecommendationLevel.Optional, false, "Baixo",
            $"Tarefa agendada de um utilitário do fabricante ({vendor}), ainda não catalogada individualmente.",
            "Fabricante do computador (OEM).", "Varia conforme o utilitário — confira o nome antes de desativar.",
            "Pode disparar verificações/atualizações periódicas específicas do fabricante.",
            "Menos uma tarefa em segundo plano se você não usa esse recurso.",
            "Desativar é reversível pelo botão Reativar; pesquise o nome em caso de dúvida.",
            descriptionEn: "Scheduled task from the computer manufacturer's utility, recognized by its path — it isn't in our detailed catalog.",
            whatItIsEn: $"Scheduled task from a manufacturer utility ({vendor}), not yet catalogued individually.",
            whoInstalledEn: "Computer manufacturer (OEM).", whenUsedEn: "Varies by utility — check the name before disabling.",
            benefitsKeepingEn: "May trigger periodic manufacturer-specific checks/updates.",
            benefitsRemovingEn: "One less background task if you don't use this feature.",
            consequencesEn: "Disabling is reversible via the Re-enable button; research the name if in doubt.");
    }

    private Sig? MatchStartup(string valueName, string data)
    {
        foreach (var (fragment, sig) in StartupSigs)
            if (valueName.Contains(fragment, StringComparison.OrdinalIgnoreCase) ||
                data.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                return sig;
        return null;
    }

    // ---- Aplicativos conhecidos (UWP e Win32), por trecho do nome ----
    private static readonly (string fragment, Sig sig)[] AppSigs =
    {
        ("XboxGamingOverlay", new Sig("Microsoft", "Aplicativo", "Barra de jogos Xbox: captura, gravação e sobreposição durante jogos.",
            ImpactLevel.Medium, BloatRiskLevel.Safe, RecommendationLevel.Optional, true, "Médio",
            "Sobreposição da Xbox Game Bar para gravar a tela e ver desempenho em jogos.",
            "Pré-instalado pelo Windows.", "Ao jogar, com a tecla Win+G.",
            "Captura e gravação rápida de jogos sem outro programa.",
            "Menos processos em segundo plano para quem não usa recursos Xbox.",
            "Atalhos de gravação do Windows deixam de funcionar. Reversível pela loja.",
            descriptionEn: "Xbox Game Bar: capture, recording and overlay during games.",
            whatItIsEn: "Xbox Game Bar overlay for screen recording and viewing game performance.",
            whoInstalledEn: "Pre-installed by Windows.", whenUsedEn: "While gaming, with the Win+G key.",
            benefitsKeepingEn: "Quick game capture and recording without another program.",
            benefitsRemovingEn: "Fewer background processes for those who don't use Xbox features.",
            consequencesEn: "Windows recording shortcuts stop working. Reversible via the Store.")),
        ("XboxSpeechToText", new Sig("Microsoft", "Aplicativo", "Componente de voz do Xbox.",
            ImpactLevel.VeryLow, BloatRiskLevel.Safe, RecommendationLevel.Optional, false, "Nenhum",
            "Suporte de fala para recursos Xbox.", "Pré-instalado pelo Windows.", "Em recursos Xbox.",
            "Recursos de voz do Xbox.", "Remove um componente raramente usado.", "Reversível pela loja.",
            descriptionEn: "Xbox voice component.",
            whatItIsEn: "Speech support for Xbox features.", whoInstalledEn: "Pre-installed by Windows.", whenUsedEn: "In Xbox features.",
            benefitsKeepingEn: "Xbox voice features.", benefitsRemovingEn: "Removes a rarely used component.",
            consequencesEn: "Reversible via the Store.")),
        ("Xbox", new Sig("Microsoft", "Aplicativo", "Aplicativos do ecossistema Xbox (companheiro, jogos, conta).",
            ImpactLevel.Low, BloatRiskLevel.Safe, RecommendationLevel.Optional, true, "Baixo",
            "Aplicativos para conta, biblioteca e social do Xbox.", "Pré-instalado pelo Windows.", "Para jogar e usar o Xbox no PC.",
            "Integração com o Game Pass e amigos do Xbox.", "Menos serviços de jogo em segundo plano.",
            "Recursos sociais/Game Pass deixam de funcionar. Reversível pela loja.",
            descriptionEn: "Xbox ecosystem apps (companion, games, account).",
            whatItIsEn: "Apps for Xbox account, library and social features.", whoInstalledEn: "Pre-installed by Windows.",
            whenUsedEn: "To play and use Xbox on the PC.",
            benefitsKeepingEn: "Integration with Game Pass and Xbox friends.", benefitsRemovingEn: "Fewer gaming services running in the background.",
            consequencesEn: "Social/Game Pass features stop working. Reversible via the Store.")),
        ("BingNews", new Sig("Microsoft", "Aplicativo", "Aplicativo de notícias da Microsoft (MSN).",
            ImpactLevel.Low, BloatRiskLevel.Safe, RecommendationLevel.Remove, true, "Baixo",
            "Agregador de notícias com notificações e widgets.", "Pré-instalado pelo Windows.", "Ao abrir notícias/widgets.",
            "Notícias rápidas na tela.", "Menos notificações e processos em segundo plano.",
            "Widget de notícias fica vazio. Reversível pela loja.",
            descriptionEn: "Microsoft's news app (MSN).",
            whatItIsEn: "News aggregator with notifications and widgets.", whoInstalledEn: "Pre-installed by Windows.",
            whenUsedEn: "When opening news/widgets.",
            benefitsKeepingEn: "Quick news on screen.", benefitsRemovingEn: "Fewer notifications and background processes.",
            consequencesEn: "The news widget becomes empty. Reversible via the Store.")),
        ("BingWeather", new Sig("Microsoft", "Aplicativo", "Aplicativo de clima da Microsoft.",
            ImpactLevel.VeryLow, BloatRiskLevel.Safe, RecommendationLevel.Remove, false, "Baixo",
            "Previsão do tempo com widget.", "Pré-instalado pelo Windows.", "Ao consultar o clima.",
            "Clima no widget e na barra.", "Remove app raramente aberto.", "Widget de clima deixa de funcionar. Reversível.",
            descriptionEn: "Microsoft's weather app.",
            whatItIsEn: "Weather forecast with widget.", whoInstalledEn: "Pre-installed by Windows.", whenUsedEn: "When checking the weather.",
            benefitsKeepingEn: "Weather in the widget and taskbar.", benefitsRemovingEn: "Removes a rarely opened app.",
            consequencesEn: "The weather widget stops working. Reversible.")),
        ("ZuneMusic", new Sig("Microsoft", "Aplicativo", "Groove/Media Player (música).",
            ImpactLevel.VeryLow, BloatRiskLevel.Safe, RecommendationLevel.Optional, false, "Nenhum",
            "Reprodutor de música padrão do Windows.", "Pré-instalado pelo Windows.", "Ao tocar música.",
            "Player de mídia integrado.", "Remove se você usa outro player.", "Arquivos de áudio abrem em outro app. Reversível.",
            descriptionEn: "Groove/Media Player (music).",
            whatItIsEn: "Windows' default music player.", whoInstalledEn: "Pre-installed by Windows.", whenUsedEn: "When playing music.",
            benefitsKeepingEn: "Integrated media player.", benefitsRemovingEn: "Remove it if you use another player.",
            consequencesEn: "Audio files open in another app. Reversible.")),
        ("ZuneVideo", new Sig("Microsoft", "Aplicativo", "Filmes e TV (vídeo).",
            ImpactLevel.VeryLow, BloatRiskLevel.Safe, RecommendationLevel.Optional, false, "Nenhum",
            "Reprodutor de vídeo e loja de filmes.", "Pré-instalado pelo Windows.", "Ao assistir vídeos.",
            "Player de vídeo integrado.", "Remove se você usa outro player.", "Vídeos abrem em outro app. Reversível.",
            descriptionEn: "Movies & TV (video).",
            whatItIsEn: "Video player and movie store.", whoInstalledEn: "Pre-installed by Windows.", whenUsedEn: "When watching videos.",
            benefitsKeepingEn: "Integrated video player.", benefitsRemovingEn: "Remove it if you use another player.",
            consequencesEn: "Videos open in another app. Reversible.")),
        ("SolitaireCollection", new Sig("Microsoft", "Aplicativo", "Coleção de jogos de paciência (com anúncios).",
            ImpactLevel.Low, BloatRiskLevel.Safe, RecommendationLevel.Remove, false, "Nenhum",
            "Pacote de jogos de cartas com anúncios.", "Pré-instalado pelo Windows.", "Ao jogar paciência.",
            "Jogos casuais.", "Remove app promocional com anúncios.", "Os jogos somem. Reversível pela loja.",
            descriptionEn: "Solitaire game collection (with ads).",
            whatItIsEn: "Card game pack with ads.", whoInstalledEn: "Pre-installed by Windows.", whenUsedEn: "When playing solitaire.",
            benefitsKeepingEn: "Casual games.", benefitsRemovingEn: "Removes a promotional app with ads.",
            consequencesEn: "The games disappear. Reversible via the Store.")),
        ("GetHelp", new Sig("Microsoft", "Aplicativo", "Obter Ajuda (suporte da Microsoft).",
            ImpactLevel.VeryLow, BloatRiskLevel.Caution, RecommendationLevel.Optional, false, "Nenhum",
            "Assistente de suporte e ajuda do Windows.", "Pré-instalado pelo Windows.", "Ao buscar ajuda.",
            "Atalho para suporte oficial.", "Remove app raramente usado.", "Links de 'ajuda' do Windows podem falhar. Reversível.",
            descriptionEn: "Get Help (Microsoft support).",
            whatItIsEn: "Windows help and support assistant.", whoInstalledEn: "Pre-installed by Windows.", whenUsedEn: "When seeking help.",
            benefitsKeepingEn: "Shortcut to official support.", benefitsRemovingEn: "Removes a rarely used app.",
            consequencesEn: "Windows 'help' links may fail. Reversible.")),
        ("Getstarted", new Sig("Microsoft", "Aplicativo", "Dicas (introdução ao Windows).",
            ImpactLevel.VeryLow, BloatRiskLevel.Safe, RecommendationLevel.Remove, false, "Nenhum",
            "App de dicas e novidades do Windows.", "Pré-instalado pelo Windows.", "Notificações de dicas.",
            "Dicas de uso.", "Menos notificações promocionais.", "Sem efeito prático. Reversível.",
            descriptionEn: "Tips (Windows introduction).",
            whatItIsEn: "App with Windows tips and news.", whoInstalledEn: "Pre-installed by Windows.", whenUsedEn: "Tip notifications.",
            benefitsKeepingEn: "Usage tips.", benefitsRemovingEn: "Fewer promotional notifications.",
            consequencesEn: "No practical effect. Reversible.")),
        ("MicrosoftOfficeHub", new Sig("Microsoft", "Aplicativo", "Atalho promocional do Office/Microsoft 365.",
            ImpactLevel.VeryLow, BloatRiskLevel.Safe, RecommendationLevel.Optional, false, "Nenhum",
            "Atalho para instalar/abrir o Microsoft 365.", "Pré-instalado pelo Windows.", "Ao gerenciar o Office.",
            "Acesso rápido ao Office.", "Remove atalho promocional.", "Some o atalho; o Office instalado não é afetado. Reversível.",
            descriptionEn: "Promotional shortcut for Office/Microsoft 365.",
            whatItIsEn: "Shortcut to install/open Microsoft 365.", whoInstalledEn: "Pre-installed by Windows.", whenUsedEn: "When managing Office.",
            benefitsKeepingEn: "Quick access to Office.", benefitsRemovingEn: "Removes a promotional shortcut.",
            consequencesEn: "The shortcut disappears; an installed Office is not affected. Reversible.")),
        ("YourPhone", new Sig("Microsoft", "Aplicativo", "Vincular ao Telefone (Phone Link).",
            ImpactLevel.Low, BloatRiskLevel.Safe, RecommendationLevel.Optional, true, "Baixo",
            "Integra mensagens, fotos e chamadas do celular ao PC.", "Pré-instalado pelo Windows.", "Ao conectar um celular.",
            "Espelhar notificações e fotos do celular.", "Menos serviços em segundo plano para quem não usa.",
            "A integração com o celular para de funcionar. Reversível pela loja.",
            descriptionEn: "Phone Link.",
            whatItIsEn: "Integrates phone messages, photos and calls with the PC.", whoInstalledEn: "Pre-installed by Windows.",
            whenUsedEn: "When connecting a phone.",
            benefitsKeepingEn: "Mirror phone notifications and photos.", benefitsRemovingEn: "Fewer background services for those who don't use it.",
            consequencesEn: "Phone integration stops working. Reversible via the Store.")),
        ("Microsoft.People", new Sig("Microsoft", "Aplicativo", "Pessoas (contatos).",
            ImpactLevel.VeryLow, BloatRiskLevel.Safe, RecommendationLevel.Remove, false, "Nenhum",
            "Agenda de contatos integrada.", "Pré-instalado pelo Windows.", "Raramente.",
            "Contatos centralizados.", "Remove app raramente usado.", "Sem efeito prático na maioria dos casos. Reversível.",
            descriptionEn: "People (contacts).",
            whatItIsEn: "Integrated contact list.", whoInstalledEn: "Pre-installed by Windows.", whenUsedEn: "Rarely.",
            benefitsKeepingEn: "Centralized contacts.", benefitsRemovingEn: "Removes a rarely used app.",
            consequencesEn: "No practical effect in most cases. Reversible.")),
        ("WindowsFeedbackHub", new Sig("Microsoft", "Aplicativo", "Hub de Comentários (feedback à Microsoft).",
            ImpactLevel.VeryLow, BloatRiskLevel.Safe, RecommendationLevel.Remove, false, "Nenhum",
            "Canal para enviar feedback e diagnósticos à Microsoft.", "Pré-instalado pelo Windows.", "Ao enviar feedback.",
            "Enviar sugestões à Microsoft.", "Remove app de telemetria/feedback.", "Sem efeito prático. Reversível.",
            descriptionEn: "Feedback Hub (feedback to Microsoft).",
            whatItIsEn: "Channel for sending feedback and diagnostics to Microsoft.", whoInstalledEn: "Pre-installed by Windows.",
            whenUsedEn: "When sending feedback.",
            benefitsKeepingEn: "Send suggestions to Microsoft.", benefitsRemovingEn: "Removes a telemetry/feedback app.",
            consequencesEn: "No practical effect. Reversible.")),
        ("MixedReality.Portal", new Sig("Microsoft", "Aplicativo", "Portal de Realidade Mista.",
            ImpactLevel.Low, BloatRiskLevel.Safe, RecommendationLevel.Remove, false, "Nenhum",
            "Plataforma para óculos de realidade mista.", "Pré-instalado pelo Windows.", "Só com hardware VR/MR.",
            "Suporte a headsets de realidade mista.", "Remove plataforma sem uso para a maioria.", "Sem efeito sem hardware VR. Reversível.",
            descriptionEn: "Mixed Reality Portal.",
            whatItIsEn: "Platform for mixed reality headsets.", whoInstalledEn: "Pre-installed by Windows.", whenUsedEn: "Only with VR/MR hardware.",
            benefitsKeepingEn: "Support for mixed reality headsets.", benefitsRemovingEn: "Removes an unused platform for most users.",
            consequencesEn: "No effect without VR hardware. Reversible.")),
        ("Clipchamp", new Sig("Microsoft", "Aplicativo", "Editor de vídeo Clipchamp.",
            ImpactLevel.Low, BloatRiskLevel.Safe, RecommendationLevel.Optional, false, "Nenhum",
            "Editor de vídeo online integrado.", "Pré-instalado pelo Windows.", "Ao editar vídeos.",
            "Edição de vídeo simples.", "Remove se você usa outro editor.", "App de edição some. Reversível pela loja.",
            descriptionEn: "Clipchamp video editor.",
            whatItIsEn: "Integrated online video editor.", whoInstalledEn: "Pre-installed by Windows.", whenUsedEn: "When editing videos.",
            benefitsKeepingEn: "Simple video editing.", benefitsRemovingEn: "Remove it if you use another editor.",
            consequencesEn: "The editing app disappears. Reversible via the Store.")),
        ("Microsoft.Todos", new Sig("Microsoft", "Aplicativo", "Microsoft To Do (tarefas).",
            ImpactLevel.VeryLow, BloatRiskLevel.Safe, RecommendationLevel.Optional, false, "Nenhum",
            "Lista de tarefas sincronizada.", "Pré-instalado pelo Windows.", "Ao gerenciar tarefas.",
            "Tarefas na nuvem.", "Remove se você não usa.", "Suas listas continuam na nuvem. Reversível.",
            descriptionEn: "Microsoft To Do (tasks).",
            whatItIsEn: "Synced task list.", whoInstalledEn: "Pre-installed by Windows.", whenUsedEn: "When managing tasks.",
            benefitsKeepingEn: "Tasks in the cloud.", benefitsRemovingEn: "Remove it if you don't use it.",
            consequencesEn: "Your lists remain in the cloud. Reversible.")),
        ("MicrosoftTeams", new Sig("Microsoft", "Aplicativo", "Microsoft Teams (versão pessoal/Chat).",
            ImpactLevel.Medium, BloatRiskLevel.Safe, RecommendationLevel.Optional, true, "Médio",
            "Chat e chamadas integrados ao Windows.", "Pré-instalado pelo Windows.", "Ao conversar pelo Teams.",
            "Chat pessoal integrado.", "Menos processos em segundo plano.", "O Teams pessoal para de iniciar. A versão corporativa instalada à parte não é afetada. Reversível.",
            descriptionEn: "Microsoft Teams (personal/Chat version).",
            whatItIsEn: "Chat and calls integrated with Windows.", whoInstalledEn: "Pre-installed by Windows.", whenUsedEn: "When chatting via Teams.",
            benefitsKeepingEn: "Integrated personal chat.", benefitsRemovingEn: "Fewer background processes.",
            consequencesEn: "Personal Teams stops launching. A separately installed corporate version is not affected. Reversible.")),
        ("DisneyMagicKingdoms", new Sig("Terceiros", "Aplicativo", "Jogo promocional pré-instalado.",
            ImpactLevel.Low, BloatRiskLevel.Safe, RecommendationLevel.Remove, false, "Nenhum",
            "Jogo instalado para promoção.", "Pré-instalado/promocional.", "Raramente.",
            "Entretenimento casual.", "Remove app promocional.", "O jogo some. Reversível pela loja.",
            descriptionEn: "Pre-installed promotional game.",
            whatItIsEn: "Game installed for promotional purposes.", whoInstalledEn: "Pre-installed/promotional.", whenUsedEn: "Rarely.",
            benefitsKeepingEn: "Casual entertainment.", benefitsRemovingEn: "Removes a promotional app.",
            consequencesEn: "The game disappears. Reversible via the Store.")),
        ("Spotify", new Sig("Spotify AB", "Aplicativo", "Player de música Spotify (stub promocional quando pré-instalado).",
            ImpactLevel.Low, BloatRiskLevel.Safe, RecommendationLevel.Optional, true, "Baixo",
            "Streaming de música.", "Pré-instalado ou instalado por você.", "Ao ouvir música.",
            "Acesso ao Spotify.", "Remove se você não usa.", "O app some; sua conta permanece. Reversível.",
            descriptionEn: "Spotify music player (promotional stub when pre-installed).",
            whatItIsEn: "Music streaming.", whoInstalledEn: "Pre-installed or installed by you.", whenUsedEn: "When listening to music.",
            benefitsKeepingEn: "Access to Spotify.", benefitsRemovingEn: "Remove it if you don't use it.",
            consequencesEn: "The app disappears; your account remains. Reversible.")),
        ("McAfee", new Sig("McAfee", "Aplicativo", "Antivírus/segurança McAfee (frequentemente pré-instalado por OEM, com avisos).",
            ImpactLevel.High, BloatRiskLevel.Caution, RecommendationLevel.Optional, true, "Alto",
            "Suíte de antivírus de terceiros, comum em PCs novos.", "Fabricante do computador (OEM).", "Em segundo plano, continuamente.",
            "Proteção antivírus de terceiros.", "Reduz alertas e consumo; o Windows Defender assume a proteção.",
            "Sem outro antivírus, o Microsoft Defender protege automaticamente. Desinstale pelo painel oficial.",
            descriptionEn: "McAfee antivirus/security (often pre-installed by OEMs, with prompts).",
            whatItIsEn: "Third-party antivirus suite, common on new PCs.", whoInstalledEn: "Computer manufacturer (OEM).",
            whenUsedEn: "In the background, continuously.",
            benefitsKeepingEn: "Third-party antivirus protection.", benefitsRemovingEn: "Fewer alerts and resource usage; Windows Defender takes over protection.",
            consequencesEn: "Without another antivirus, Microsoft Defender protects automatically. Uninstall via the official panel.")),
        ("Norton", new Sig("NortonLifeLock", "Aplicativo", "Antivírus/segurança Norton (frequentemente pré-instalado por OEM).",
            ImpactLevel.High, BloatRiskLevel.Caution, RecommendationLevel.Optional, true, "Alto",
            "Suíte de antivírus de terceiros, comum em PCs novos.", "Fabricante do computador (OEM).", "Em segundo plano, continuamente.",
            "Proteção antivírus de terceiros.", "Reduz alertas e consumo; o Windows Defender assume a proteção.",
            "Sem outro antivírus, o Microsoft Defender protege automaticamente. Desinstale pelo painel oficial.",
            descriptionEn: "Norton antivirus/security (often pre-installed by OEMs).",
            whatItIsEn: "Third-party antivirus suite, common on new PCs.", whoInstalledEn: "Computer manufacturer (OEM).",
            whenUsedEn: "In the background, continuously.",
            benefitsKeepingEn: "Third-party antivirus protection.", benefitsRemovingEn: "Fewer alerts and resource usage; Windows Defender takes over protection.",
            consequencesEn: "Without another antivirus, Microsoft Defender protects automatically. Uninstall via the official panel.")),

        // ---- Mais aplicativos pré-instalados do Windows (UWP) ----
        ("Microsoft.SkypeApp", new Sig("Microsoft", "Aplicativo", "Skype (chamadas e mensagens).",
            ImpactLevel.Low, BloatRiskLevel.Safe, RecommendationLevel.Optional, true, "Baixo",
            "Aplicativo de chamadas de vídeo e mensagens.", "Pré-instalado pelo Windows.", "Ao fazer chamadas pelo Skype.",
            "Chamadas e chat sem instalar nada.", "Remove se você usa outro app de chamadas.",
            "O Skype some; sua conta continua. Reversível pela loja.",
            descriptionEn: "Skype (calls and messaging).",
            whatItIsEn: "Video calling and messaging app.", whoInstalledEn: "Pre-installed by Windows.", whenUsedEn: "When making calls via Skype.",
            benefitsKeepingEn: "Calls and chat without installing anything.", benefitsRemovingEn: "Remove it if you use another calling app.",
            consequencesEn: "Skype disappears; your account remains. Reversible via the Store.")),
        ("Microsoft.WindowsMaps", new Sig("Microsoft", "Aplicativo", "Mapas (Windows Maps).",
            ImpactLevel.Low, BloatRiskLevel.Safe, RecommendationLevel.Remove, false, "Nenhum",
            "Aplicativo de mapas e navegação.", "Pré-instalado pelo Windows.", "Ao consultar rotas/mapas.",
            "Mapas offline e rotas no PC.", "Remove app raramente usado em desktops.",
            "Sem efeito prático para quem usa mapas no celular. Reversível.",
            descriptionEn: "Maps (Windows Maps).",
            whatItIsEn: "Maps and navigation app.", whoInstalledEn: "Pre-installed by Windows.", whenUsedEn: "When checking routes/maps.",
            benefitsKeepingEn: "Offline maps and routes on the PC.", benefitsRemovingEn: "Removes a rarely used app on desktops.",
            consequencesEn: "No practical effect for those who use maps on a phone. Reversible.")),
        ("Microsoft.MicrosoftStickyNotes", new Sig("Microsoft", "Aplicativo", "Notas Autoadesivas (bloco de notas rápidas).",
            ImpactLevel.VeryLow, BloatRiskLevel.Caution, RecommendationLevel.Optional, false, "Nenhum",
            "Post-its digitais na área de trabalho.", "Pré-instalado pelo Windows.", "Ao anotar lembretes rápidos.",
            "Notas rápidas sempre visíveis.", "Remove se você não usa notas na área de trabalho.",
            "Notas existentes podem ficar inacessíveis até reinstalar. Reversível pela loja.",
            descriptionEn: "Sticky Notes (quick notepad).",
            whatItIsEn: "Digital post-its on the desktop.", whoInstalledEn: "Pre-installed by Windows.", whenUsedEn: "When jotting down quick reminders.",
            benefitsKeepingEn: "Quick notes always visible.", benefitsRemovingEn: "Remove it if you don't use desktop notes.",
            consequencesEn: "Existing notes may become inaccessible until reinstalled. Reversible via the Store.")),
        ("Microsoft.WindowsSoundRecorder", new Sig("Microsoft", "Aplicativo", "Gravador de Voz.",
            ImpactLevel.VeryLow, BloatRiskLevel.Safe, RecommendationLevel.Remove, false, "Nenhum",
            "Gravador de áudio simples.", "Pré-instalado pelo Windows.", "Ao gravar áudio.",
            "Gravação rápida de áudio sem outro programa.", "Remove app raramente usado.",
            "Sem efeito prático. Reversível pela loja.",
            descriptionEn: "Voice Recorder.",
            whatItIsEn: "Simple audio recorder.", whoInstalledEn: "Pre-installed by Windows.", whenUsedEn: "When recording audio.",
            benefitsKeepingEn: "Quick audio recording without another program.", benefitsRemovingEn: "Removes a rarely used app.",
            consequencesEn: "No practical effect. Reversible via the Store.")),
        ("Microsoft.MSPaint", new Sig("Microsoft", "Aplicativo", "Paint 3D (edição de imagens e modelos 3D).",
            ImpactLevel.Low, BloatRiskLevel.Safe, RecommendationLevel.Remove, false, "Nenhum",
            "Editor de imagens com recursos 3D, diferente do Paint clássico.", "Pré-instalado pelo Windows.", "Ao editar imagens/3D.",
            "Edição de imagens e objetos 3D.", "Remove se você usa o Paint clássico ou outro editor.",
            "O Paint clássico continua funcionando normalmente. Reversível pela loja.",
            descriptionEn: "Paint 3D (image and 3D model editing).",
            whatItIsEn: "Image editor with 3D features, different from classic Paint.", whoInstalledEn: "Pre-installed by Windows.",
            whenUsedEn: "When editing images/3D.",
            benefitsKeepingEn: "Editing of images and 3D objects.", benefitsRemovingEn: "Remove it if you use classic Paint or another editor.",
            consequencesEn: "Classic Paint keeps working normally. Reversible via the Store.")),
        ("Microsoft.Print3D", new Sig("Microsoft", "Aplicativo", "Impressão 3D.",
            ImpactLevel.VeryLow, BloatRiskLevel.Safe, RecommendationLevel.Remove, false, "Nenhum",
            "Envia modelos 3D para impressoras 3D.", "Pré-instalado pelo Windows.", "Só com impressora 3D.",
            "Fluxo de impressão 3D integrado.", "Remove plataforma sem uso para quem não tem impressora 3D.",
            "Sem efeito sem hardware de impressão 3D. Reversível.",
            descriptionEn: "3D Print.",
            whatItIsEn: "Sends 3D models to 3D printers.", whoInstalledEn: "Pre-installed by Windows.", whenUsedEn: "Only with a 3D printer.",
            benefitsKeepingEn: "Integrated 3D printing workflow.", benefitsRemovingEn: "Removes an unused platform for those without a 3D printer.",
            consequencesEn: "No effect without 3D printing hardware. Reversible.")),
        ("Microsoft.3DBuilder", new Sig("Microsoft", "Aplicativo", "Construtor 3D (criação de modelos 3D).",
            ImpactLevel.VeryLow, BloatRiskLevel.Safe, RecommendationLevel.Remove, false, "Nenhum",
            "Editor simples de modelos 3D.", "Pré-instalado pelo Windows.", "Raramente.",
            "Criação básica de objetos 3D.", "Remove app raramente usado.", "Sem efeito prático. Reversível.",
            descriptionEn: "3D Builder (3D model creation).",
            whatItIsEn: "Simple 3D model editor.", whoInstalledEn: "Pre-installed by Windows.", whenUsedEn: "Rarely.",
            benefitsKeepingEn: "Basic 3D object creation.", benefitsRemovingEn: "Removes a rarely used app.",
            consequencesEn: "No practical effect. Reversible.")),
        ("Microsoft.Microsoft3DViewer", new Sig("Microsoft", "Aplicativo", "Visualizador 3D.",
            ImpactLevel.VeryLow, BloatRiskLevel.Safe, RecommendationLevel.Remove, false, "Nenhum",
            "Visualiza arquivos de modelos 3D.", "Pré-instalado pelo Windows.", "Raramente.",
            "Visualização rápida de modelos 3D.", "Remove app raramente usado.", "Sem efeito prático. Reversível.",
            descriptionEn: "3D Viewer.",
            whatItIsEn: "Views 3D model files.", whoInstalledEn: "Pre-installed by Windows.", whenUsedEn: "Rarely.",
            benefitsKeepingEn: "Quick viewing of 3D models.", benefitsRemovingEn: "Removes a rarely used app.",
            consequencesEn: "No practical effect. Reversible.")),
        ("Microsoft.WindowsAlarms", new Sig("Microsoft", "Aplicativo", "Alarmes e Relógio.",
            ImpactLevel.VeryLow, BloatRiskLevel.Safe, RecommendationLevel.Optional, false, "Nenhum",
            "Alarmes, cronômetro e timer.", "Pré-instalado pelo Windows.", "Ao usar alarmes/timer.",
            "Alarmes e cronômetro sem outro app.", "Remove se você usa o celular para isso.",
            "Alarmes configurados deixam de funcionar. Reversível pela loja.",
            descriptionEn: "Alarms & Clock.",
            whatItIsEn: "Alarms, stopwatch and timer.", whoInstalledEn: "Pre-installed by Windows.", whenUsedEn: "When using alarms/timer.",
            benefitsKeepingEn: "Alarms and stopwatch without another app.", benefitsRemovingEn: "Remove it if you use your phone for this.",
            consequencesEn: "Configured alarms stop working. Reversible via the Store.")),
        ("Microsoft.WindowsCommunicationsApps", new Sig("Microsoft", "Aplicativo", "Email e Calendário (Mail and Calendar).",
            ImpactLevel.Low, BloatRiskLevel.Caution, RecommendationLevel.Optional, true, "Baixo",
            "Cliente de e-mail e calendário integrado.", "Pré-instalado pelo Windows.", "Ao checar e-mails/agenda no app nativo.",
            "E-mail e agenda sem instalar outro programa.", "Remove se você usa Outlook completo ou webmail.",
            "Contas configuradas no app deixam de sincronizar ali; o e-mail em si não é afetado. Reversível.",
            descriptionEn: "Mail and Calendar.",
            whatItIsEn: "Integrated email and calendar client.", whoInstalledEn: "Pre-installed by Windows.",
            whenUsedEn: "When checking email/schedule in the native app.",
            benefitsKeepingEn: "Email and calendar without installing another program.", benefitsRemovingEn: "Remove it if you use full Outlook or webmail.",
            consequencesEn: "Accounts configured in the app stop syncing there; email itself is not affected. Reversible.")),
        ("Microsoft.Whiteboard", new Sig("Microsoft", "Aplicativo", "Quadro Branco (Whiteboard) colaborativo.",
            ImpactLevel.Low, BloatRiskLevel.Safe, RecommendationLevel.Remove, false, "Nenhum",
            "Quadro branco digital para anotações colaborativas.", "Pré-instalado pelo Windows.", "Em reuniões/aulas.",
            "Colaboração visual em tempo real.", "Remove app raramente usado fora de reuniões.",
            "Sem efeito prático para quem não usa. Reversível pela loja.",
            descriptionEn: "Whiteboard (collaborative).",
            whatItIsEn: "Digital whiteboard for collaborative notes.", whoInstalledEn: "Pre-installed by Windows.", whenUsedEn: "In meetings/classes.",
            benefitsKeepingEn: "Real-time visual collaboration.", benefitsRemovingEn: "Removes an app rarely used outside meetings.",
            consequencesEn: "No practical effect for those who don't use it. Reversible via the Store.")),
        ("Microsoft.Wallet", new Sig("Microsoft", "Aplicativo", "Carteira (Microsoft Wallet).",
            ImpactLevel.VeryLow, BloatRiskLevel.Safe, RecommendationLevel.Remove, false, "Nenhum",
            "Carteira digital para cartões e ingressos.", "Pré-instalado pelo Windows.", "Raramente, fora de algumas regiões.",
            "Cartões e ingressos digitais.", "Remove app pouco usado na maioria das regiões.",
            "Sem efeito prático. Reversível pela loja.",
            descriptionEn: "Wallet (Microsoft Wallet).",
            whatItIsEn: "Digital wallet for cards and tickets.", whoInstalledEn: "Pre-installed by Windows.", whenUsedEn: "Rarely, outside a few regions.",
            benefitsKeepingEn: "Digital cards and tickets.", benefitsRemovingEn: "Removes an app little used in most regions.",
            consequencesEn: "No practical effect. Reversible via the Store.")),
        ("Microsoft.549981C3F5F10", new Sig("Microsoft", "Aplicativo", "Cortana (assistente virtual).",
            ImpactLevel.Medium, BloatRiskLevel.Caution, RecommendationLevel.Optional, true, "Médio",
            "Assistente virtual por voz, hoje desligado da busca do Windows.", "Pré-instalado pelo Windows.", "Ao acionar por voz/comando.",
            "Comandos de voz e automações pessoais.", "Menos um processo em segundo plano para quem não usa.",
            "Comandos de voz da Cortana deixam de funcionar; a busca do Windows não é afetada. Reversível pela loja.",
            descriptionEn: "Cortana (virtual assistant).",
            whatItIsEn: "Voice virtual assistant, now decoupled from Windows search.", whoInstalledEn: "Pre-installed by Windows.",
            whenUsedEn: "When triggered by voice/command.",
            benefitsKeepingEn: "Voice commands and personal automations.", benefitsRemovingEn: "One fewer background process for those who don't use it.",
            consequencesEn: "Cortana voice commands stop working; Windows search is not affected. Reversible via the Store.")),
        ("MicrosoftFamily", new Sig("Microsoft", "Aplicativo", "Microsoft Family Safety (controle parental).",
            ImpactLevel.Low, BloatRiskLevel.Caution, RecommendationLevel.Optional, true, "Baixo",
            "Controle parental e localização familiar.", "Pré-instalado pelo Windows.", "Para quem gerencia contas de família.",
            "Controle parental integrado.", "Remove se você não usa contas de família/controle parental.",
            "Recursos de controle parental deixam de aparecer neste PC. Reversível pela loja.",
            descriptionEn: "Microsoft Family Safety (parental controls).",
            whatItIsEn: "Parental controls and family location.", whoInstalledEn: "Pre-installed by Windows.",
            whenUsedEn: "For those who manage family accounts.",
            benefitsKeepingEn: "Integrated parental controls.", benefitsRemovingEn: "Remove it if you don't use family accounts/parental controls.",
            consequencesEn: "Parental control features stop appearing on this PC. Reversible via the Store.")),
    };

    // ---- Aplicativos OEM (fabricante do notebook), frequentemente pré-instalados e seguros de remover ----
    private static readonly (string fragment, Sig sig)[] OemAppSigs =
    {
        ("Dell SupportAssist", new Sig("Dell", "Aplicativo (OEM)", "Diagnóstico e suporte da Dell pré-instalado.",
            ImpactLevel.Medium, BloatRiskLevel.Caution, RecommendationLevel.Optional, true, "Médio",
            "Verifica hardware, drivers e abre chamados de suporte Dell.", "Fabricante do computador (Dell).", "Em segundo plano e ao checar o PC.",
            "Diagnóstico de hardware e drivers pela Dell.", "Menos processos em segundo plano; o Windows Update ainda traz drivers básicos.",
            "Diagnósticos e atualizações automáticas da Dell deixam de rodar; drivers específicos podem precisar ser baixados manualmente do site da Dell. Desinstale pelo painel oficial.",
            descriptionEn: "Pre-installed Dell diagnostics and support.",
            whatItIsEn: "Checks hardware, drivers and opens Dell support tickets.", whoInstalledEn: "Computer manufacturer (Dell).",
            whenUsedEn: "In the background and when checking the PC.",
            benefitsKeepingEn: "Hardware and driver diagnostics by Dell.", benefitsRemovingEn: "Fewer background processes; Windows Update still provides basic drivers.",
            consequencesEn: "Dell's automatic diagnostics and updates stop running; specific drivers may need to be downloaded manually from Dell's site. Uninstall via the official panel.")),
        ("Dell Optimizer", new Sig("Dell", "Aplicativo (OEM)", "Otimização de desempenho/bateria da Dell.",
            ImpactLevel.Medium, BloatRiskLevel.Caution, RecommendationLevel.Optional, true, "Médio",
            "Ajusta desempenho, bateria e áudio conforme o uso.", "Fabricante do computador (Dell).", "Continuamente em segundo plano.",
            "Otimizações automáticas específicas do hardware Dell.", "Menos consumo de RAM/CPU em segundo plano.",
            "Os ajustes automáticos da Dell deixam de ocorrer; o controle manual de energia do Windows continua funcionando. Desinstale pelo painel oficial.",
            descriptionEn: "Dell performance/battery optimization.",
            whatItIsEn: "Adjusts performance, battery and audio based on usage.", whoInstalledEn: "Computer manufacturer (Dell).",
            whenUsedEn: "Continuously in the background.",
            benefitsKeepingEn: "Automatic optimizations specific to Dell hardware.", benefitsRemovingEn: "Less RAM/CPU usage in the background.",
            consequencesEn: "Dell's automatic adjustments stop; Windows' manual power control keeps working. Uninstall via the official panel.")),
        ("Dell Digital Delivery", new Sig("Dell", "Aplicativo (OEM)", "Entrega de softwares comprados junto com o PC Dell.",
            ImpactLevel.Low, BloatRiskLevel.Safe, RecommendationLevel.Remove, false, "Baixo",
            "Baixa softwares adquiridos na compra do PC.", "Fabricante do computador (Dell).", "Apenas na primeira configuração.",
            "Entrega automática de softwares comprados.", "Remove app que normalmente só é útil uma vez.",
            "Se ainda não recebeu algum software comprado, baixe pelo site da Dell. Desinstale pelo painel oficial.",
            descriptionEn: "Delivery of software purchased with the Dell PC.",
            whatItIsEn: "Downloads software purchased with the PC.", whoInstalledEn: "Computer manufacturer (Dell).",
            whenUsedEn: "Only during initial setup.",
            benefitsKeepingEn: "Automatic delivery of purchased software.", benefitsRemovingEn: "Removes an app that's normally useful only once.",
            consequencesEn: "If you haven't received some purchased software yet, download it from Dell's site. Uninstall via the official panel.")),
        ("Dell Update", new Sig("Dell", "Aplicativo (OEM)", "Atualizador de drivers e BIOS da Dell.",
            ImpactLevel.Low, BloatRiskLevel.Caution, RecommendationLevel.Optional, true, "Baixo",
            "Verifica atualizações de driver/BIOS da Dell.", "Fabricante do computador (Dell).", "Periodicamente.",
            "Drivers e BIOS sempre atualizados.", "Menos uma tarefa em segundo plano.",
            "Atualize drivers/BIOS manualmente pelo site da Dell de vez em quando. Desinstale pelo painel oficial.",
            descriptionEn: "Dell driver and BIOS updater.",
            whatItIsEn: "Checks for Dell driver/BIOS updates.", whoInstalledEn: "Computer manufacturer (Dell).", whenUsedEn: "Periodically.",
            benefitsKeepingEn: "Drivers and BIOS always up to date.", benefitsRemovingEn: "One fewer background task.",
            consequencesEn: "Update drivers/BIOS manually from Dell's site occasionally. Uninstall via the official panel.")),
        ("Dell Customer Connect", new Sig("Dell", "Aplicativo (OEM)", "Pesquisas de satisfação da Dell.",
            ImpactLevel.VeryLow, BloatRiskLevel.Safe, RecommendationLevel.Remove, false, "Nenhum",
            "Envia pesquisas de satisfação periódicas.", "Fabricante do computador (Dell).", "Ocasionalmente.",
            "Canal de feedback com a Dell.", "Remove notificações promocionais de pesquisa.", "Sem efeito prático. Desinstale pelo painel oficial.",
            descriptionEn: "Dell customer satisfaction surveys.",
            whatItIsEn: "Sends periodic satisfaction surveys.", whoInstalledEn: "Computer manufacturer (Dell).", whenUsedEn: "Occasionally.",
            benefitsKeepingEn: "Feedback channel with Dell.", benefitsRemovingEn: "Removes promotional survey notifications.",
            consequencesEn: "No practical effect. Uninstall via the official panel.")),
        ("MyDell", new Sig("Dell", "Aplicativo (OEM)", "Central de informações e suporte do PC Dell.",
            ImpactLevel.Low, BloatRiskLevel.Safe, RecommendationLevel.Optional, false, "Baixo",
            "Mostra informações de garantia, suporte e dicas do PC.", "Fabricante do computador (Dell).", "Ao consultar garantia/suporte.",
            "Acesso rápido a garantia e suporte Dell.", "Remove app raramente usado após a configuração inicial.",
            "Consulte garantia/suporte pelo site da Dell. Desinstale pelo painel oficial.",
            descriptionEn: "Information and support hub for the Dell PC.",
            whatItIsEn: "Shows warranty, support and PC tips information.", whoInstalledEn: "Computer manufacturer (Dell).",
            whenUsedEn: "When checking warranty/support.",
            benefitsKeepingEn: "Quick access to Dell warranty and support.", benefitsRemovingEn: "Removes an app rarely used after initial setup.",
            consequencesEn: "Check warranty/support via Dell's site. Uninstall via the official panel.")),
        ("HP Support Assistant", new Sig("HP", "Aplicativo (OEM)", "Diagnóstico e suporte da HP pré-instalado.",
            ImpactLevel.Medium, BloatRiskLevel.Caution, RecommendationLevel.Optional, true, "Médio",
            "Verifica hardware, drivers e abre chamados de suporte HP.", "Fabricante do computador (HP).", "Em segundo plano e ao checar o PC.",
            "Diagnóstico de hardware e drivers pela HP.", "Menos processos em segundo plano.",
            "Diagnósticos e atualizações automáticas da HP deixam de rodar; drivers podem ser baixados manualmente do site da HP. Desinstale pelo painel oficial.",
            descriptionEn: "Pre-installed HP diagnostics and support.",
            whatItIsEn: "Checks hardware, drivers and opens HP support tickets.", whoInstalledEn: "Computer manufacturer (HP).",
            whenUsedEn: "In the background and when checking the PC.",
            benefitsKeepingEn: "Hardware and driver diagnostics by HP.", benefitsRemovingEn: "Fewer background processes.",
            consequencesEn: "HP's automatic diagnostics and updates stop running; drivers can be downloaded manually from HP's site. Uninstall via the official panel.")),
        ("HP JumpStart", new Sig("HP", "Aplicativo (OEM)", "Assistente de configuração inicial da HP.",
            ImpactLevel.Low, BloatRiskLevel.Safe, RecommendationLevel.Remove, false, "Baixo",
            "Guia a configuração inicial do PC e sugere apps HP.", "Fabricante do computador (HP).", "Apenas na primeira configuração.",
            "Configuração guiada na primeira vez.", "Remove app útil só na configuração inicial.",
            "Sem efeito prático após a configuração inicial. Desinstale pelo painel oficial.",
            descriptionEn: "HP initial setup assistant.",
            whatItIsEn: "Guides the PC's initial setup and suggests HP apps.", whoInstalledEn: "Computer manufacturer (HP).",
            whenUsedEn: "Only during initial setup.",
            benefitsKeepingEn: "Guided setup the first time.", benefitsRemovingEn: "Removes an app useful only during initial setup.",
            consequencesEn: "No practical effect after initial setup. Uninstall via the official panel.")),
        ("HP Documentation", new Sig("HP", "Aplicativo (OEM)", "Manuais e documentação do PC HP.",
            ImpactLevel.VeryLow, BloatRiskLevel.Safe, RecommendationLevel.Remove, false, "Nenhum",
            "Manuais e guias do fabricante.", "Fabricante do computador (HP).", "Raramente.",
            "Documentação offline do PC.", "Remove app raramente aberto.", "Consulte manuais pelo site da HP. Desinstale pelo painel oficial.",
            descriptionEn: "Manuals and documentation for the HP PC.",
            whatItIsEn: "Manufacturer manuals and guides.", whoInstalledEn: "Computer manufacturer (HP).", whenUsedEn: "Rarely.",
            benefitsKeepingEn: "Offline PC documentation.", benefitsRemovingEn: "Removes a rarely opened app.",
            consequencesEn: "Check manuals via HP's site. Uninstall via the official panel.")),
        ("myHP", new Sig("HP", "Aplicativo (OEM)", "Central de informações e suporte do PC HP.",
            ImpactLevel.Low, BloatRiskLevel.Safe, RecommendationLevel.Optional, false, "Baixo",
            "Mostra informações de garantia, suporte e dicas do PC.", "Fabricante do computador (HP).", "Ao consultar garantia/suporte.",
            "Acesso rápido a garantia e suporte HP.", "Remove app raramente usado após a configuração inicial.",
            "Consulte garantia/suporte pelo site da HP. Desinstale pelo painel oficial.",
            descriptionEn: "Information and support hub for the HP PC.",
            whatItIsEn: "Shows warranty, support and PC tips information.", whoInstalledEn: "Computer manufacturer (HP).",
            whenUsedEn: "When checking warranty/support.",
            benefitsKeepingEn: "Quick access to HP warranty and support.", benefitsRemovingEn: "Removes an app rarely used after initial setup.",
            consequencesEn: "Check warranty/support via HP's site. Uninstall via the official panel.")),
        ("Lenovo Vantage", new Sig("Lenovo", "Aplicativo (OEM)", "Central de controle e atualizações da Lenovo.",
            ImpactLevel.Medium, BloatRiskLevel.Caution, RecommendationLevel.Optional, true, "Médio",
            "Atualiza drivers/BIOS e ajusta recursos específicos do hardware Lenovo.", "Fabricante do computador (Lenovo).", "Em segundo plano e ao abrir o app.",
            "Drivers e recursos de hardware sempre atualizados/configuráveis.", "Menos processos em segundo plano.",
            "Atualizações automáticas da Lenovo e alguns atalhos de tecla especiais podem parar; drivers seguem disponíveis no site da Lenovo. Desinstale pelo painel oficial.",
            descriptionEn: "Lenovo control center and updates.",
            whatItIsEn: "Updates drivers/BIOS and adjusts Lenovo hardware-specific features.", whoInstalledEn: "Computer manufacturer (Lenovo).",
            whenUsedEn: "In the background and when opening the app.",
            benefitsKeepingEn: "Hardware drivers and features always up to date/configurable.", benefitsRemovingEn: "Fewer background processes.",
            consequencesEn: "Lenovo's automatic updates and some special key shortcuts may stop; drivers remain available on Lenovo's site. Uninstall via the official panel.")),
        ("Lenovo Now", new Sig("Lenovo", "Aplicativo (OEM)", "Recomendações e ofertas da Lenovo.",
            ImpactLevel.Low, BloatRiskLevel.Safe, RecommendationLevel.Remove, true, "Baixo",
            "Mostra recomendações de produtos e serviços Lenovo.", "Fabricante do computador (Lenovo).", "Em segundo plano.",
            "Ofertas e recomendações personalizadas.", "Menos notificações promocionais e processos em segundo plano.",
            "Sem efeito prático no funcionamento do PC. Desinstale pelo painel oficial.",
            descriptionEn: "Lenovo recommendations and offers.",
            whatItIsEn: "Shows recommendations for Lenovo products and services.", whoInstalledEn: "Computer manufacturer (Lenovo).",
            whenUsedEn: "In the background.",
            benefitsKeepingEn: "Personalized offers and recommendations.", benefitsRemovingEn: "Fewer promotional notifications and background processes.",
            consequencesEn: "No practical effect on the PC's operation. Uninstall via the official panel.")),
        ("Lenovo Welcome", new Sig("Lenovo", "Aplicativo (OEM)", "Assistente de boas-vindas/configuração da Lenovo.",
            ImpactLevel.Low, BloatRiskLevel.Safe, RecommendationLevel.Remove, false, "Baixo",
            "Guia a configuração inicial do PC.", "Fabricante do computador (Lenovo).", "Apenas na primeira configuração.",
            "Configuração guiada na primeira vez.", "Remove app útil só na configuração inicial.",
            "Sem efeito prático após a configuração inicial. Desinstale pelo painel oficial.",
            descriptionEn: "Lenovo welcome/setup assistant.",
            whatItIsEn: "Guides the PC's initial setup.", whoInstalledEn: "Computer manufacturer (Lenovo).", whenUsedEn: "Only during initial setup.",
            benefitsKeepingEn: "Guided setup the first time.", benefitsRemovingEn: "Removes an app useful only during initial setup.",
            consequencesEn: "No practical effect after initial setup. Uninstall via the official panel.")),
        ("ASUS GiftBox", new Sig("ASUS", "Aplicativo (OEM)", "Loja de aplicativos e promoções da ASUS.",
            ImpactLevel.Low, BloatRiskLevel.Safe, RecommendationLevel.Remove, true, "Baixo",
            "Vitrine de apps e promoções parceiras da ASUS.", "Fabricante do computador (ASUS).", "Em segundo plano.",
            "Acesso a apps/promoções selecionadas pela ASUS.", "Menos notificações promocionais e processos em segundo plano.",
            "Sem efeito prático no funcionamento do PC. Desinstale pelo painel oficial.",
            descriptionEn: "ASUS app store and promotions.",
            whatItIsEn: "Showcase of apps and partner promotions from ASUS.", whoInstalledEn: "Computer manufacturer (ASUS).",
            whenUsedEn: "In the background.",
            benefitsKeepingEn: "Access to apps/promotions selected by ASUS.", benefitsRemovingEn: "Fewer promotional notifications and background processes.",
            consequencesEn: "No practical effect on the PC's operation. Uninstall via the official panel.")),
        ("Acer Care Center", new Sig("Acer", "Aplicativo (OEM)", "Diagnóstico, atualizações e suporte da Acer.",
            ImpactLevel.Medium, BloatRiskLevel.Caution, RecommendationLevel.Optional, true, "Médio",
            "Verifica hardware, drivers e oferece suporte Acer.", "Fabricante do computador (Acer).", "Em segundo plano e ao checar o PC.",
            "Diagnóstico e atualizações específicas da Acer.", "Menos processos em segundo plano.",
            "Atualizações automáticas da Acer deixam de rodar; drivers podem ser baixados manualmente do site da Acer. Desinstale pelo painel oficial.",
            descriptionEn: "Acer diagnostics, updates and support.",
            whatItIsEn: "Checks hardware, drivers and offers Acer support.", whoInstalledEn: "Computer manufacturer (Acer).",
            whenUsedEn: "In the background and when checking the PC.",
            benefitsKeepingEn: "Acer-specific diagnostics and updates.", benefitsRemovingEn: "Fewer background processes.",
            consequencesEn: "Acer's automatic updates stop running; drivers can be downloaded manually from Acer's site. Uninstall via the official panel.")),
        ("Acer Quick Access", new Sig("Acer", "Aplicativo (OEM)", "Atalhos de energia e recursos rápidos da Acer.",
            ImpactLevel.Low, BloatRiskLevel.Caution, RecommendationLevel.Optional, true, "Baixo",
            "Atalhos para modos de energia e recursos do teclado Acer.", "Fabricante do computador (Acer).", "Em segundo plano.",
            "Acesso rápido a modos de energia/recursos do teclado.", "Menos um processo em segundo plano.",
            "Algumas teclas de função especiais da Acer podem parar de funcionar. Desinstale pelo painel oficial.",
            descriptionEn: "Acer power shortcuts and quick features.",
            whatItIsEn: "Shortcuts for power modes and Acer keyboard features.", whoInstalledEn: "Computer manufacturer (Acer).",
            whenUsedEn: "In the background.",
            benefitsKeepingEn: "Quick access to power modes/keyboard features.", benefitsRemovingEn: "One fewer background process.",
            consequencesEn: "Some special Acer function keys may stop working. Uninstall via the official panel.")),
        ("MSI Center", new Sig("MSI", "Aplicativo (OEM)", "Central de controle de hardware e RGB da MSI.",
            ImpactLevel.Medium, BloatRiskLevel.Caution, RecommendationLevel.Optional, true, "Médio",
            "Ajusta desempenho, ventoinhas e iluminação RGB do hardware MSI.", "Fabricante do computador (MSI).", "Continuamente em segundo plano.",
            "Controle de desempenho/RGB específico da MSI.", "Menos consumo de RAM/CPU em segundo plano.",
            "Perfis de desempenho e iluminação RGB customizados deixam de ser ajustáveis pelo app. Desinstale pelo painel oficial.",
            descriptionEn: "MSI hardware and RGB control center.",
            whatItIsEn: "Adjusts performance, fans and RGB lighting on MSI hardware.", whoInstalledEn: "Computer manufacturer (MSI).",
            whenUsedEn: "Continuously in the background.",
            benefitsKeepingEn: "MSI-specific performance/RGB control.", benefitsRemovingEn: "Less RAM/CPU usage in the background.",
            consequencesEn: "Custom performance profiles and RGB lighting become unadjustable via the app. Uninstall via the official panel.")),
        ("Dell Mobile Connect", new Sig("Dell", "Aplicativo (OEM)", "Espelhamento de celular no PC Dell (descontinuado).",
            ImpactLevel.Low, BloatRiskLevel.Safe, RecommendationLevel.Remove, true, "Baixo",
            "Espelha chamadas, mensagens e tela do celular no PC.", "Fabricante do computador (Dell).", "Em segundo plano, se pareado a um celular.",
            "Integração com o celular sem usar o app da Microsoft.", "Serviço descontinuado pela Dell; o Vínculo com o Telefone do Windows faz o mesmo.",
            "Perde o espelhamento do celular por esse app; use o Vínculo com o Telefone do Windows. Desinstale pelo painel oficial.",
            descriptionEn: "Phone mirroring on the Dell PC (discontinued).",
            whatItIsEn: "Mirrors phone calls, messages and screen on the PC.", whoInstalledEn: "Computer manufacturer (Dell).",
            whenUsedEn: "In the background, if paired with a phone.",
            benefitsKeepingEn: "Phone integration without Microsoft's app.", benefitsRemovingEn: "Discontinued by Dell; Windows Phone Link does the same.",
            consequencesEn: "You lose phone mirroring through this app; use Windows Phone Link. Uninstall via the official panel.")),
        ("Dell Power Manager", new Sig("Dell", "Aplicativo (OEM)", "Controle de bateria, térmica e ventoinhas da Dell.",
            ImpactLevel.Low, BloatRiskLevel.NotRecommended, RecommendationLevel.Keep, true, "Baixo",
            "Define limites de carga da bateria, perfis térmicos e curva de ventoinha do hardware Dell.", "Fabricante do computador (Dell).", "Continuamente em segundo plano.",
            "Controla hardware real: saúde da bateria, temperatura e ruído das ventoinhas.", "Praticamente nenhum — o ganho de desempenho é desprezível.",
            "Limites de carga da bateria e perfis térmicos personalizados voltam ao padrão da BIOS; o notebook pode ficar mais quente ou mais barulhento. Recomendamos manter.",
            descriptionEn: "Dell battery, thermal and fan control.",
            whatItIsEn: "Sets battery charge limits, thermal profiles and fan curves on Dell hardware.", whoInstalledEn: "Computer manufacturer (Dell).",
            whenUsedEn: "Continuously in the background.",
            benefitsKeepingEn: "Controls real hardware: battery health, temperature and fan noise.", benefitsRemovingEn: "Practically none — the performance gain is negligible.",
            consequencesEn: "Battery charge limits and custom thermal profiles revert to BIOS defaults; the laptop may run hotter or louder. We recommend keeping it.")),
        ("SupportAssist Remediation", new Sig("Dell", "Aplicativo (OEM)", "Componente de correção automática do Dell SupportAssist.",
            ImpactLevel.Low, BloatRiskLevel.Safe, RecommendationLevel.Optional, true, "Baixo",
            "Roda limpezas e correções automáticas agendadas pelo SupportAssist.", "Fabricante do computador (Dell).", "Periodicamente em segundo plano.",
            "Manutenção automática agendada pela Dell.", "Menos uma tarefa de manutenção em segundo plano.",
            "As limpezas automáticas da Dell param; a Limpeza de Disco do Windows continua disponível. Desinstale pelo painel oficial.",
            descriptionEn: "Dell SupportAssist automatic remediation component.",
            whatItIsEn: "Runs cleanups and automatic fixes scheduled by SupportAssist.", whoInstalledEn: "Computer manufacturer (Dell).",
            whenUsedEn: "Periodically in the background.",
            benefitsKeepingEn: "Automatic maintenance scheduled by Dell.", benefitsRemovingEn: "One fewer background maintenance task.",
            consequencesEn: "Dell's automatic cleanups stop; Windows Disk Cleanup remains available. Uninstall via the official panel.")),
        ("Dell Command", new Sig("Dell", "Aplicativo (OEM)", "Dell Command Update: atualizador corporativo de drivers e BIOS.",
            ImpactLevel.Low, BloatRiskLevel.Caution, RecommendationLevel.Optional, true, "Baixo",
            "Versão corporativa do atualizador de drivers/BIOS da Dell.", "Fabricante do computador ou TI da empresa (Dell).", "Periodicamente.",
            "Drivers e BIOS atualizados de forma controlada.", "Menos uma tarefa em segundo plano em PCs de uso pessoal.",
            "Em PC corporativo, a TI pode depender dele para distribuir BIOS e drivers — confirme antes. Desinstale pelo painel oficial.",
            descriptionEn: "Dell Command Update: enterprise driver and BIOS updater.",
            whatItIsEn: "Enterprise version of Dell's driver/BIOS updater.", whoInstalledEn: "Computer manufacturer or company IT (Dell).",
            whenUsedEn: "Periodically.",
            benefitsKeepingEn: "Drivers and BIOS updated in a controlled way.", benefitsRemovingEn: "One fewer background task on personal PCs.",
            consequencesEn: "On a corporate PC, IT may rely on it to deploy BIOS and drivers — confirm first. Uninstall via the official panel.")),
        ("HP Connection Optimizer", new Sig("HP", "Aplicativo (OEM)", "Troca automática entre Wi-Fi e rede móvel da HP.",
            ImpactLevel.Low, BloatRiskLevel.Safe, RecommendationLevel.Optional, true, "Baixo",
            "Escolhe automaticamente a melhor conexão entre Wi-Fi e banda larga móvel.", "Fabricante do computador (HP).", "Continuamente em segundo plano.",
            "Troca automática de rede em notebooks com chip de celular.", "Menos um processo em segundo plano se o PC só usa Wi-Fi.",
            "A troca automática de rede deixa de ocorrer; você escolhe a rede manualmente. Desinstale pelo painel oficial.",
            descriptionEn: "HP automatic switching between Wi-Fi and mobile network.",
            whatItIsEn: "Automatically picks the best connection between Wi-Fi and mobile broadband.", whoInstalledEn: "Computer manufacturer (HP).",
            whenUsedEn: "Continuously in the background.",
            benefitsKeepingEn: "Automatic network switching on laptops with a cellular modem.", benefitsRemovingEn: "One fewer background process if the PC only uses Wi-Fi.",
            consequencesEn: "Automatic network switching stops; you pick the network manually. Uninstall via the official panel.")),
        ("HP Sure Connect", new Sig("HP", "Aplicativo (OEM)", "Recuperação de conexão de rede da HP.",
            ImpactLevel.VeryLow, BloatRiskLevel.Safe, RecommendationLevel.Optional, true, "Baixo",
            "Tenta restaurar a conexão de rede automaticamente para o suporte remoto da HP.", "Fabricante do computador (HP).", "Em segundo plano.",
            "Ajuda o suporte HP a reconectar o PC remotamente.", "Menos um processo em segundo plano.",
            "O suporte remoto automático da HP deixa de funcionar; a rede normal não é afetada. Desinstale pelo painel oficial.",
            descriptionEn: "HP network connection recovery.",
            whatItIsEn: "Tries to restore the network connection automatically for HP remote support.", whoInstalledEn: "Computer manufacturer (HP).",
            whenUsedEn: "In the background.",
            benefitsKeepingEn: "Helps HP support reconnect the PC remotely.", benefitsRemovingEn: "One fewer background process.",
            consequencesEn: "HP's automatic remote support stops working; normal networking is unaffected. Uninstall via the official panel.")),
        ("HP Audio Switch", new Sig("HP", "Aplicativo (OEM)", "Seletor de saída de áudio da HP.",
            ImpactLevel.Low, BloatRiskLevel.Caution, RecommendationLevel.Optional, true, "Baixo",
            "Detecta fone/headset e ajusta a saída de áudio do hardware HP.", "Fabricante do computador (HP).", "Ao conectar fones ou alterar o áudio.",
            "Detecção automática correta de fone e microfone no conector combo.", "Menos um processo de áudio em segundo plano.",
            "Em alguns modelos HP, o conector combo pode parar de detectar o microfone do headset corretamente; a saída de áudio passa a ser escolhida pelo Windows. Desinstale pelo painel oficial.",
            descriptionEn: "HP audio output selector.",
            whatItIsEn: "Detects headphones/headsets and adjusts audio output on HP hardware.", whoInstalledEn: "Computer manufacturer (HP).",
            whenUsedEn: "When plugging headphones or changing audio.",
            benefitsKeepingEn: "Correct automatic headphone and microphone detection on the combo jack.", benefitsRemovingEn: "One fewer background audio process.",
            consequencesEn: "On some HP models the combo jack may stop detecting the headset microphone correctly; audio output is then chosen by Windows. Uninstall via the official panel.")),
        ("HP Smart", new Sig("HP", "Aplicativo (OEM)", "Aplicativo de impressoras e scanners HP.",
            ImpactLevel.Low, BloatRiskLevel.Safe, RecommendationLevel.Optional, true, "Baixo",
            "Gerencia impressoras e scanners HP, níveis de tinta e digitalização.", "Fabricante do computador (HP) ou instalado com a impressora.", "Ao imprimir ou digitalizar.",
            "Digitalização e monitoramento de tinta de impressoras HP.", "Remove um app grande se você não tem impressora HP.",
            "Se você usa impressora HP, perde a digitalização e o aviso de tinta pelo app; a impressão comum pelo driver do Windows continua. Desinstale pelo painel oficial.",
            descriptionEn: "HP printer and scanner application.",
            whatItIsEn: "Manages HP printers and scanners, ink levels and scanning.", whoInstalledEn: "Computer manufacturer (HP) or installed with the printer.",
            whenUsedEn: "When printing or scanning.",
            benefitsKeepingEn: "Scanning and ink monitoring for HP printers.", benefitsRemovingEn: "Removes a large app if you don't own an HP printer.",
            consequencesEn: "If you use an HP printer, you lose scanning and ink alerts through the app; ordinary printing via the Windows driver still works. Uninstall via the official panel.")),
        ("HP Wolf Security", new Sig("HP", "Aplicativo (OEM)", "Suíte de segurança da HP pré-instalada.",
            ImpactLevel.High, BloatRiskLevel.NotRecommended, RecommendationLevel.Keep, true, "Alto",
            "Antivírus e isolamento de ameaças da HP, com proteção adicional de firmware.", "Fabricante do computador (HP).", "Continuamente em segundo plano.",
            "Camada extra de segurança integrada ao firmware HP.", "Reduz bastante o uso de CPU/RAM — é conhecido por pesar no sistema.",
            "Remover deixa o PC sem essa camada de proteção; confirme que o Microsoft Defender assumiu antes de reiniciar. A desinstalação exige remover vários componentes na ordem certa pelo painel oficial e reiniciar.",
            descriptionEn: "Pre-installed HP security suite.",
            whatItIsEn: "HP antivirus and threat isolation, with additional firmware protection.", whoInstalledEn: "Computer manufacturer (HP).",
            whenUsedEn: "Continuously in the background.",
            benefitsKeepingEn: "Extra security layer integrated with HP firmware.", benefitsRemovingEn: "Significantly reduces CPU/RAM usage — it's known to weigh on the system.",
            consequencesEn: "Removing leaves the PC without that protection layer; confirm Microsoft Defender has taken over before rebooting. Uninstalling requires removing several components in the right order via the official panel and restarting.")),
        ("HP Programmable Key", new Sig("HP", "Aplicativo (OEM)", "Configuração da tecla programável do teclado HP.",
            ImpactLevel.VeryLow, BloatRiskLevel.NotRecommended, RecommendationLevel.Keep, true, "Baixo",
            "Define a ação da tecla programável dos teclados HP.", "Fabricante do computador (HP).", "Em segundo plano, aguardando a tecla.",
            "Mantém a tecla programável do teclado funcionando.", "Praticamente nenhum — o consumo é mínimo.",
            "A tecla programável do teclado deixa de executar qualquer ação. Recomendamos manter em notebooks HP que tenham essa tecla.",
            descriptionEn: "HP keyboard programmable key configuration.",
            whatItIsEn: "Sets the action of the programmable key on HP keyboards.", whoInstalledEn: "Computer manufacturer (HP).",
            whenUsedEn: "In the background, waiting for the key.",
            benefitsKeepingEn: "Keeps the keyboard's programmable key working.", benefitsRemovingEn: "Practically none — consumption is minimal.",
            consequencesEn: "The programmable key stops performing any action. We recommend keeping it on HP laptops that have this key.")),
        ("HP QuickDrop", new Sig("HP", "Aplicativo (OEM)", "Transferência de arquivos entre PC e celular da HP.",
            ImpactLevel.Low, BloatRiskLevel.Safe, RecommendationLevel.Remove, true, "Baixo",
            "Envia arquivos entre o PC HP e o celular.", "Fabricante do computador (HP).", "Em segundo plano, se pareado a um celular.",
            "Transferência rápida de arquivos com o celular.", "Menos um processo em segundo plano se você nunca pareou um celular.",
            "A transferência por esse app deixa de funcionar; use o Vínculo com o Telefone ou a nuvem. Desinstale pelo painel oficial.",
            descriptionEn: "HP file transfer between PC and phone.",
            whatItIsEn: "Sends files between the HP PC and your phone.", whoInstalledEn: "Computer manufacturer (HP).",
            whenUsedEn: "In the background, if paired with a phone.",
            benefitsKeepingEn: "Quick file transfer with your phone.", benefitsRemovingEn: "One fewer background process if you never paired a phone.",
            consequencesEn: "Transfers through this app stop working; use Phone Link or the cloud. Uninstall via the official panel.")),
        ("Lenovo Smart Appearance", new Sig("Lenovo", "Aplicativo (OEM)", "Efeitos de câmera e vídeo da Lenovo.",
            ImpactLevel.Low, BloatRiskLevel.Safe, RecommendationLevel.Optional, true, "Baixo",
            "Aplica desfoque de fundo e ajustes de imagem na webcam.", "Fabricante do computador (Lenovo).", "Durante videochamadas.",
            "Efeitos de webcam sem depender do app de videochamada.", "Menos um processo usando a câmera em segundo plano.",
            "Os efeitos de webcam da Lenovo deixam de estar disponíveis; a câmera continua funcionando normalmente. Desinstale pelo painel oficial.",
            descriptionEn: "Lenovo camera and video effects.",
            whatItIsEn: "Applies background blur and image adjustments to the webcam.", whoInstalledEn: "Computer manufacturer (Lenovo).",
            whenUsedEn: "During video calls.",
            benefitsKeepingEn: "Webcam effects without relying on the video call app.", benefitsRemovingEn: "One fewer process using the camera in the background.",
            consequencesEn: "Lenovo's webcam effects become unavailable; the camera keeps working normally. Uninstall via the official panel.")),
        ("Lenovo Voice", new Sig("Lenovo", "Aplicativo (OEM)", "Transcrição e comandos de voz da Lenovo.",
            ImpactLevel.Low, BloatRiskLevel.Safe, RecommendationLevel.Remove, true, "Baixo",
            "Transcreve fala em texto e traduz áudio.", "Fabricante do computador (Lenovo).", "Quando aberto pelo usuário.",
            "Transcrição e tradução integradas.", "Menos um processo em segundo plano se você não usa ditado da Lenovo.",
            "A transcrição da Lenovo deixa de existir; o ditado do Windows continua disponível. Desinstale pelo painel oficial.",
            descriptionEn: "Lenovo voice transcription and commands.",
            whatItIsEn: "Transcribes speech to text and translates audio.", whoInstalledEn: "Computer manufacturer (Lenovo).",
            whenUsedEn: "When opened by the user.",
            benefitsKeepingEn: "Built-in transcription and translation.", benefitsRemovingEn: "One fewer background process if you don't use Lenovo dictation.",
            consequencesEn: "Lenovo transcription is gone; Windows dictation remains available. Uninstall via the official panel.")),
        ("Lenovo Migration Assistant", new Sig("Lenovo", "Aplicativo (OEM)", "Migração de arquivos de um PC antigo para o Lenovo.",
            ImpactLevel.VeryLow, BloatRiskLevel.Safe, RecommendationLevel.Remove, false, "Nenhum",
            "Copia arquivos e configurações de outro PC.", "Fabricante do computador (Lenovo).", "Apenas na primeira configuração.",
            "Migração guiada do PC antigo.", "Remove app útil só uma vez.",
            "Sem efeito prático após a migração inicial. Desinstale pelo painel oficial.",
            descriptionEn: "Migration of files from an old PC to the Lenovo.",
            whatItIsEn: "Copies files and settings from another PC.", whoInstalledEn: "Computer manufacturer (Lenovo).",
            whenUsedEn: "Only during initial setup.",
            benefitsKeepingEn: "Guided migration from the old PC.", benefitsRemovingEn: "Removes an app useful only once.",
            consequencesEn: "No practical effect after the initial migration. Uninstall via the official panel.")),
        ("Lenovo Quick Clean", new Sig("Lenovo", "Aplicativo (OEM)", "Bloqueio temporário do teclado para limpeza.",
            ImpactLevel.VeryLow, BloatRiskLevel.Safe, RecommendationLevel.Remove, false, "Nenhum",
            "Desativa teclado e touchpad por alguns segundos para você limpá-los.", "Fabricante do computador (Lenovo).", "Raramente, quando aberto.",
            "Limpar o teclado sem digitar sem querer.", "Remove app raramente aberto.",
            "Sem efeito no funcionamento do teclado. Desinstale pelo painel oficial.",
            descriptionEn: "Temporary keyboard lock for cleaning.",
            whatItIsEn: "Disables keyboard and touchpad for a few seconds so you can clean them.", whoInstalledEn: "Computer manufacturer (Lenovo).",
            whenUsedEn: "Rarely, when opened.",
            benefitsKeepingEn: "Clean the keyboard without typing by accident.", benefitsRemovingEn: "Removes a rarely opened app.",
            consequencesEn: "No effect on keyboard operation. Uninstall via the official panel.")),
        ("Lenovo Hotkeys", new Sig("Lenovo", "Aplicativo (OEM)", "Teclas de função especiais do teclado Lenovo.",
            ImpactLevel.VeryLow, BloatRiskLevel.NotRecommended, RecommendationLevel.Keep, true, "Baixo",
            "Faz as teclas Fn de brilho, volume, avião e microfone funcionarem.", "Fabricante do computador (Lenovo).", "Continuamente em segundo plano.",
            "Mantém as teclas de atalho do notebook funcionando.", "Praticamente nenhum — o consumo é mínimo.",
            "Teclas de função como brilho, modo avião e mudo do microfone podem parar de funcionar. Recomendamos manter.",
            descriptionEn: "Special function keys on the Lenovo keyboard.",
            whatItIsEn: "Makes the Fn keys for brightness, volume, airplane mode and microphone work.", whoInstalledEn: "Computer manufacturer (Lenovo).",
            whenUsedEn: "Continuously in the background.",
            benefitsKeepingEn: "Keeps the laptop's hotkeys working.", benefitsRemovingEn: "Practically none — consumption is minimal.",
            consequencesEn: "Function keys such as brightness, airplane mode and microphone mute may stop working. We recommend keeping it.")),
        ("Commercial Vantage", new Sig("Lenovo", "Aplicativo (OEM)", "Versão corporativa do Lenovo Vantage.",
            ImpactLevel.Medium, BloatRiskLevel.Caution, RecommendationLevel.Optional, true, "Médio",
            "Atualiza drivers/BIOS e aplica políticas de TI em PCs Lenovo corporativos.", "Fabricante do computador ou TI da empresa (Lenovo).", "Em segundo plano.",
            "Drivers atualizados e configurações gerenciadas pela TI.", "Menos processos em segundo plano em PCs de uso pessoal.",
            "Em PC corporativo, a TI pode depender dele para políticas e atualizações — confirme antes de remover. Desinstale pelo painel oficial.",
            descriptionEn: "Enterprise version of Lenovo Vantage.",
            whatItIsEn: "Updates drivers/BIOS and applies IT policies on corporate Lenovo PCs.", whoInstalledEn: "Computer manufacturer or company IT (Lenovo).",
            whenUsedEn: "In the background.",
            benefitsKeepingEn: "Updated drivers and IT-managed settings.", benefitsRemovingEn: "Fewer background processes on personal PCs.",
            consequencesEn: "On a corporate PC, IT may rely on it for policies and updates — confirm before removing. Uninstall via the official panel.")),
        ("Lenovo Utility", new Sig("Lenovo", "Aplicativo (OEM)", "Utilitário de teclas e avisos na tela da Lenovo.",
            ImpactLevel.VeryLow, BloatRiskLevel.NotRecommended, RecommendationLevel.Keep, true, "Baixo",
            "Mostra os avisos na tela de brilho, volume e Caps Lock e apoia as teclas Fn.", "Fabricante do computador (Lenovo).", "Continuamente em segundo plano.",
            "Avisos na tela e teclas de função funcionando.", "Praticamente nenhum — o consumo é mínimo.",
            "Os avisos na tela somem e algumas teclas Fn podem parar de responder. Recomendamos manter.",
            descriptionEn: "Lenovo on-screen display and key utility.",
            whatItIsEn: "Shows on-screen indicators for brightness, volume and Caps Lock and supports the Fn keys.", whoInstalledEn: "Computer manufacturer (Lenovo).",
            whenUsedEn: "Continuously in the background.",
            benefitsKeepingEn: "On-screen indicators and working function keys.", benefitsRemovingEn: "Practically none — consumption is minimal.",
            consequencesEn: "On-screen indicators disappear and some Fn keys may stop responding. We recommend keeping it.")),
        ("ASUS Live Update", new Sig("ASUS", "Aplicativo (OEM)", "Atualizador de drivers e BIOS da ASUS.",
            ImpactLevel.Low, BloatRiskLevel.Caution, RecommendationLevel.Optional, true, "Baixo",
            "Verifica e instala atualizações de driver/BIOS da ASUS.", "Fabricante do computador (ASUS).", "Periodicamente.",
            "Drivers e BIOS sempre atualizados.", "Menos uma tarefa em segundo plano.",
            "Atualize drivers/BIOS manualmente pelo site da ASUS de vez em quando. Desinstale pelo painel oficial.",
            descriptionEn: "ASUS driver and BIOS updater.",
            whatItIsEn: "Checks and installs ASUS driver/BIOS updates.", whoInstalledEn: "Computer manufacturer (ASUS).", whenUsedEn: "Periodically.",
            benefitsKeepingEn: "Drivers and BIOS always up to date.", benefitsRemovingEn: "One fewer background task.",
            consequencesEn: "Update drivers/BIOS manually from ASUS' site occasionally. Uninstall via the official panel.")),
        ("MyASUS", new Sig("ASUS", "Aplicativo (OEM)", "Central de suporte e configurações do PC ASUS.",
            ImpactLevel.Medium, BloatRiskLevel.Caution, RecommendationLevel.Optional, true, "Médio",
            "Diagnóstico, garantia, atualizações e ajustes de ventoinha/bateria do hardware ASUS.", "Fabricante do computador (ASUS).", "Em segundo plano e ao abrir o app.",
            "Controla limites de carga da bateria e modos de ventoinha em muitos notebooks ASUS.", "Menos processos em segundo plano.",
            "Perfis de ventoinha e limite de carga da bateria voltam ao padrão; diagnósticos e garantia passam a ser consultados pelo site da ASUS. Desinstale pelo painel oficial.",
            descriptionEn: "ASUS PC support and settings hub.",
            whatItIsEn: "Diagnostics, warranty, updates and fan/battery adjustments for ASUS hardware.", whoInstalledEn: "Computer manufacturer (ASUS).",
            whenUsedEn: "In the background and when opening the app.",
            benefitsKeepingEn: "Controls battery charge limits and fan modes on many ASUS laptops.", benefitsRemovingEn: "Fewer background processes.",
            consequencesEn: "Fan profiles and battery charge limits revert to defaults; diagnostics and warranty move to ASUS' website. Uninstall via the official panel.")),
        ("ASUS Splendid", new Sig("ASUS", "Aplicativo (OEM)", "Ajuste de cor e temperatura da tela ASUS.",
            ImpactLevel.Low, BloatRiskLevel.Caution, RecommendationLevel.Optional, true, "Baixo",
            "Aplica perfis de cor e filtro de luz azul na tela do notebook ASUS.", "Fabricante do computador (ASUS).", "Continuamente em segundo plano.",
            "Perfis de cor calibrados para a tela do modelo.", "Menos um processo em segundo plano.",
            "A tela volta ao perfil de cor padrão e pode parecer diferente do que você está acostumado; use a Luz Noturna do Windows. Desinstale pelo painel oficial.",
            descriptionEn: "ASUS screen color and temperature adjustment.",
            whatItIsEn: "Applies color profiles and a blue light filter to the ASUS laptop screen.", whoInstalledEn: "Computer manufacturer (ASUS).",
            whenUsedEn: "Continuously in the background.",
            benefitsKeepingEn: "Color profiles calibrated for the model's panel.", benefitsRemovingEn: "One fewer background process.",
            consequencesEn: "The screen reverts to the default color profile and may look different from what you're used to; use Windows Night Light. Uninstall via the official panel.")),
        ("GlideX", new Sig("ASUS", "Aplicativo (OEM)", "Compartilhamento de tela e arquivos entre dispositivos ASUS.",
            ImpactLevel.Low, BloatRiskLevel.Safe, RecommendationLevel.Remove, true, "Baixo",
            "Usa o celular ou outro PC como segunda tela e transfere arquivos.", "Fabricante do computador (ASUS).", "Em segundo plano, se pareado.",
            "Segunda tela e transferência de arquivos entre dispositivos.", "Menos um processo em segundo plano se você não usa o recurso.",
            "O espelhamento e a transferência pelo GlideX deixam de funcionar. Desinstale pelo painel oficial.",
            descriptionEn: "Screen and file sharing between ASUS devices.",
            whatItIsEn: "Uses your phone or another PC as a second screen and transfers files.", whoInstalledEn: "Computer manufacturer (ASUS).",
            whenUsedEn: "In the background, if paired.",
            benefitsKeepingEn: "Second screen and file transfer between devices.", benefitsRemovingEn: "One fewer background process if you don't use the feature.",
            consequencesEn: "Mirroring and transfers through GlideX stop working. Uninstall via the official panel.")),
        ("ASUS Product Register", new Sig("ASUS", "Aplicativo (OEM)", "Registro de produto e garantia da ASUS.",
            ImpactLevel.VeryLow, BloatRiskLevel.Safe, RecommendationLevel.Remove, true, "Baixo",
            "Lembra de registrar o produto para ativar a garantia.", "Fabricante do computador (ASUS).", "Apenas na primeira configuração.",
            "Lembrete para registrar a garantia.", "Remove notificações repetitivas de registro.",
            "Se ainda não registrou a garantia, faça isso pelo site da ASUS antes de remover. Desinstale pelo painel oficial.",
            descriptionEn: "ASUS product and warranty registration.",
            whatItIsEn: "Reminds you to register the product to activate the warranty.", whoInstalledEn: "Computer manufacturer (ASUS).",
            whenUsedEn: "Only during initial setup.",
            benefitsKeepingEn: "Reminder to register the warranty.", benefitsRemovingEn: "Removes repetitive registration notifications.",
            consequencesEn: "If you haven't registered the warranty yet, do it on ASUS' site before removing. Uninstall via the official panel.")),
        ("Armoury Crate", new Sig("ASUS", "Aplicativo (OEM)", "Central de desempenho, ventoinhas e RGB da ASUS/ROG.",
            ImpactLevel.High, BloatRiskLevel.NotRecommended, RecommendationLevel.Keep, true, "Alto",
            "Controla perfis de desempenho, curva de ventoinha, iluminação Aura RGB e teclas ROG.", "Fabricante do computador (ASUS/ROG).", "Continuamente em segundo plano.",
            "Em notebooks gamer ASUS/ROG é o único jeito de escolher o modo Turbo/Silencioso e ajustar as ventoinhas e o RGB.", "Reduz bastante o uso de RAM/CPU — o app é pesado e tem vários serviços.",
            "Em máquinas gaming ASUS/ROG, o notebook fica preso no perfil padrão de ventoinha e desempenho, o RGB volta ao padrão e as teclas ROG param. Só remova se não usa esses recursos; a reinstalação é trabalhosa.",
            descriptionEn: "ASUS/ROG performance, fan and RGB control center.",
            whatItIsEn: "Controls performance profiles, fan curves, Aura RGB lighting and ROG keys.", whoInstalledEn: "Computer manufacturer (ASUS/ROG).",
            whenUsedEn: "Continuously in the background.",
            benefitsKeepingEn: "On ASUS/ROG gaming laptops it's the only way to pick Turbo/Silent mode and adjust fans and RGB.", benefitsRemovingEn: "Significantly reduces RAM/CPU usage — the app is heavy and has several services.",
            consequencesEn: "On ASUS/ROG gaming machines the laptop gets stuck on the default fan and performance profile, RGB reverts to default and the ROG keys stop working. Remove only if you don't use these features; reinstalling is troublesome.")),
        ("Acer Jumpstart", new Sig("Acer", "Aplicativo (OEM)", "Assistente de boas-vindas e ofertas da Acer.",
            ImpactLevel.Low, BloatRiskLevel.Safe, RecommendationLevel.Remove, true, "Baixo",
            "Apresenta o PC e sugere apps e ofertas parceiras.", "Fabricante do computador (Acer).", "Apenas na primeira configuração.",
            "Apresentação guiada na primeira vez.", "Remove app promocional útil só na configuração inicial.",
            "Sem efeito prático após a configuração inicial. Desinstale pelo painel oficial.",
            descriptionEn: "Acer welcome assistant and offers.",
            whatItIsEn: "Introduces the PC and suggests partner apps and offers.", whoInstalledEn: "Computer manufacturer (Acer).",
            whenUsedEn: "Only during initial setup.",
            benefitsKeepingEn: "Guided introduction the first time.", benefitsRemovingEn: "Removes a promotional app useful only during initial setup.",
            consequencesEn: "No practical effect after initial setup. Uninstall via the official panel.")),
        ("Acer Product Registration", new Sig("Acer", "Aplicativo (OEM)", "Registro de produto e garantia da Acer.",
            ImpactLevel.VeryLow, BloatRiskLevel.Safe, RecommendationLevel.Remove, true, "Baixo",
            "Lembra de registrar o produto para ativar a garantia.", "Fabricante do computador (Acer).", "Apenas na primeira configuração.",
            "Lembrete para registrar a garantia.", "Remove notificações repetitivas de registro.",
            "Se ainda não registrou a garantia, faça isso pelo site da Acer antes de remover. Desinstale pelo painel oficial.",
            descriptionEn: "Acer product and warranty registration.",
            whatItIsEn: "Reminds you to register the product to activate the warranty.", whoInstalledEn: "Computer manufacturer (Acer).",
            whenUsedEn: "Only during initial setup.",
            benefitsKeepingEn: "Reminder to register the warranty.", benefitsRemovingEn: "Removes repetitive registration notifications.",
            consequencesEn: "If you haven't registered the warranty yet, do it on Acer's site before removing. Uninstall via the official panel.")),
        ("Acer Collection", new Sig("Acer", "Aplicativo (OEM)", "Vitrine de aplicativos recomendados pela Acer.",
            ImpactLevel.Low, BloatRiskLevel.Safe, RecommendationLevel.Remove, true, "Baixo",
            "Lista apps parceiros sugeridos pela Acer.", "Fabricante do computador (Acer).", "Em segundo plano.",
            "Sugestões de apps selecionados pela Acer.", "Menos notificações promocionais e processos em segundo plano.",
            "Sem efeito prático no funcionamento do PC. Desinstale pelo painel oficial.",
            descriptionEn: "Showcase of apps recommended by Acer.",
            whatItIsEn: "Lists partner apps suggested by Acer.", whoInstalledEn: "Computer manufacturer (Acer).", whenUsedEn: "In the background.",
            benefitsKeepingEn: "App suggestions curated by Acer.", benefitsRemovingEn: "Fewer promotional notifications and background processes.",
            consequencesEn: "No practical effect on the PC's operation. Uninstall via the official panel.")),
        ("Planet9", new Sig("Acer", "Aplicativo (OEM)", "Plataforma de eSports da Acer (descontinuada).",
            ImpactLevel.Low, BloatRiskLevel.Safe, RecommendationLevel.Remove, true, "Baixo",
            "Plataforma de torneios e times amadores de eSports.", "Fabricante do computador (Acer).", "Raramente, se você participa de torneios.",
            "Acesso à comunidade de torneios da Acer.", "Serviço descontinuado; remove um app sem função.",
            "Sem efeito prático — a plataforma foi encerrada pela Acer. Desinstale pelo painel oficial.",
            descriptionEn: "Acer eSports platform (discontinued).",
            whatItIsEn: "Platform for amateur eSports tournaments and teams.", whoInstalledEn: "Computer manufacturer (Acer).",
            whenUsedEn: "Rarely, if you join tournaments.",
            benefitsKeepingEn: "Access to Acer's tournament community.", benefitsRemovingEn: "Discontinued service; removes an app with no function.",
            consequencesEn: "No practical effect — the platform was shut down by Acer. Uninstall via the official panel.")),
        ("Acer Configuration Manager", new Sig("Acer", "Aplicativo (OEM)", "Gerenciamento remoto de PCs Acer corporativos.",
            ImpactLevel.Low, BloatRiskLevel.Caution, RecommendationLevel.Optional, true, "Baixo",
            "Permite que a TI configure e atualize PCs Acer remotamente.", "Fabricante do computador ou TI da empresa (Acer).", "Em segundo plano.",
            "Configuração centralizada pela TI.", "Menos processos em segundo plano em PCs de uso pessoal.",
            "Em PC corporativo, a TI perde o gerenciamento remoto desta máquina — confirme antes de remover. Desinstale pelo painel oficial.",
            descriptionEn: "Remote management of corporate Acer PCs.",
            whatItIsEn: "Lets IT configure and update Acer PCs remotely.", whoInstalledEn: "Computer manufacturer or company IT (Acer).",
            whenUsedEn: "In the background.",
            benefitsKeepingEn: "Centralized configuration by IT.", benefitsRemovingEn: "Fewer background processes on personal PCs.",
            consequencesEn: "On a corporate PC, IT loses remote management of this machine — confirm before removing. Uninstall via the official panel.")),
        ("Samsung Update", new Sig("Samsung", "Aplicativo (OEM)", "Atualizador de drivers e software da Samsung.",
            ImpactLevel.Low, BloatRiskLevel.Caution, RecommendationLevel.Optional, true, "Baixo",
            "Verifica atualizações de driver e software do notebook Samsung.", "Fabricante do computador (Samsung).", "Periodicamente.",
            "Drivers específicos do Galaxy Book sempre atualizados.", "Menos uma tarefa em segundo plano.",
            "Atualize drivers manualmente pelo site da Samsung de vez em quando. Desinstale pelo painel oficial.",
            descriptionEn: "Samsung driver and software updater.",
            whatItIsEn: "Checks for driver and software updates on the Samsung laptop.", whoInstalledEn: "Computer manufacturer (Samsung).",
            whenUsedEn: "Periodically.",
            benefitsKeepingEn: "Galaxy Book-specific drivers always up to date.", benefitsRemovingEn: "One fewer background task.",
            consequencesEn: "Update drivers manually from Samsung's site occasionally. Uninstall via the official panel.")),
        ("Samsung Settings", new Sig("Samsung", "Aplicativo (OEM)", "Ajustes de hardware do notebook Samsung.",
            ImpactLevel.Low, BloatRiskLevel.NotRecommended, RecommendationLevel.Keep, true, "Baixo",
            "Controla teclas Fn, limite de carga da bateria, brilho do teclado e modos de desempenho.", "Fabricante do computador (Samsung).", "Continuamente em segundo plano.",
            "Único painel que ajusta teclas Fn, bateria e modos de desempenho do Galaxy Book.", "Praticamente nenhum — o consumo é baixo.",
            "Teclas de função, limite de carga da bateria e luz do teclado podem parar de ser ajustáveis. Recomendamos manter.",
            descriptionEn: "Samsung laptop hardware settings.",
            whatItIsEn: "Controls Fn keys, battery charge limit, keyboard backlight and performance modes.", whoInstalledEn: "Computer manufacturer (Samsung).",
            whenUsedEn: "Continuously in the background.",
            benefitsKeepingEn: "The only panel that adjusts Fn keys, battery and Galaxy Book performance modes.", benefitsRemovingEn: "Practically none — consumption is low.",
            consequencesEn: "Function keys, battery charge limit and keyboard backlight may become unadjustable. We recommend keeping it.")),
        ("Samsung Recovery", new Sig("Samsung", "Aplicativo (OEM)", "Backup e restauração de fábrica da Samsung.",
            ImpactLevel.Low, BloatRiskLevel.Caution, RecommendationLevel.Optional, false, "Baixo",
            "Cria backups e restaura o notebook ao estado de fábrica.", "Fabricante do computador (Samsung).", "Raramente, ao restaurar o PC.",
            "Restauração de fábrica com os drivers originais Samsung.", "Remove app raramente aberto.",
            "Você perde a restauração de fábrica pelo app; a Redefinição do Windows continua disponível, mas sem os drivers/apps originais da Samsung. Desinstale pelo painel oficial.",
            descriptionEn: "Samsung backup and factory restore.",
            whatItIsEn: "Creates backups and restores the laptop to factory state.", whoInstalledEn: "Computer manufacturer (Samsung).",
            whenUsedEn: "Rarely, when restoring the PC.",
            benefitsKeepingEn: "Factory restore with the original Samsung drivers.", benefitsRemovingEn: "Removes a rarely opened app.",
            consequencesEn: "You lose factory restore through the app; Windows Reset remains available, but without Samsung's original drivers/apps. Uninstall via the official panel.")),
        ("LG Smart Assistant", new Sig("LG", "Aplicativo (OEM)", "Central de ajustes do notebook LG gram.",
            ImpactLevel.Low, BloatRiskLevel.Caution, RecommendationLevel.Optional, true, "Baixo",
            "Ajusta bateria, ventoinha e teclas especiais dos notebooks LG.", "Fabricante do computador (LG).", "Em segundo plano.",
            "Ajustes de bateria e ventoinha específicos do LG gram.", "Menos um processo em segundo plano.",
            "Limite de carga da bateria e modos de ventoinha voltam ao padrão da BIOS. Desinstale pelo painel oficial.",
            descriptionEn: "LG gram laptop settings hub.",
            whatItIsEn: "Adjusts battery, fan and special keys on LG laptops.", whoInstalledEn: "Computer manufacturer (LG).",
            whenUsedEn: "In the background.",
            benefitsKeepingEn: "LG gram-specific battery and fan adjustments.", benefitsRemovingEn: "One fewer background process.",
            consequencesEn: "Battery charge limit and fan modes revert to BIOS defaults. Uninstall via the official panel.")),
        ("LG Update Center", new Sig("LG", "Aplicativo (OEM)", "Atualizador de drivers e software da LG.",
            ImpactLevel.Low, BloatRiskLevel.Caution, RecommendationLevel.Optional, true, "Baixo",
            "Verifica atualizações de driver e firmware do notebook LG.", "Fabricante do computador (LG).", "Periodicamente.",
            "Drivers e firmware sempre atualizados.", "Menos uma tarefa em segundo plano.",
            "Atualize drivers manualmente pelo site da LG de vez em quando. Desinstale pelo painel oficial.",
            descriptionEn: "LG driver and software updater.",
            whatItIsEn: "Checks for driver and firmware updates on the LG laptop.", whoInstalledEn: "Computer manufacturer (LG).",
            whenUsedEn: "Periodically.",
            benefitsKeepingEn: "Drivers and firmware always up to date.", benefitsRemovingEn: "One fewer background task.",
            consequencesEn: "Update drivers manually from LG's site occasionally. Uninstall via the official panel.")),
        ("Dragon Center", new Sig("MSI", "Aplicativo (OEM)", "Central de desempenho e RGB da MSI (versão anterior ao MSI Center).",
            ImpactLevel.Medium, BloatRiskLevel.NotRecommended, RecommendationLevel.Keep, true, "Médio",
            "Controla perfis de desempenho, ventoinhas e iluminação Mystic Light do hardware MSI.", "Fabricante do computador (MSI).", "Continuamente em segundo plano.",
            "Em notebooks gamer MSI, é o que ajusta modo de ventoinha, desempenho e RGB.", "Reduz o uso de RAM/CPU — o app roda vários serviços.",
            "O notebook fica preso no perfil padrão de ventoinha e desempenho e o RGB volta ao padrão. Só remova se não usa esses recursos.",
            descriptionEn: "MSI performance and RGB center (predecessor of MSI Center).",
            whatItIsEn: "Controls performance profiles, fans and Mystic Light lighting on MSI hardware.", whoInstalledEn: "Computer manufacturer (MSI).",
            whenUsedEn: "Continuously in the background.",
            benefitsKeepingEn: "On MSI gaming laptops it's what adjusts fan mode, performance and RGB.", benefitsRemovingEn: "Reduces RAM/CPU usage — the app runs several services.",
            consequencesEn: "The laptop gets stuck on the default fan and performance profile and RGB reverts to default. Remove only if you don't use these features.")),
        ("MSI Companion", new Sig("MSI", "Aplicativo (OEM)", "Atalhos rápidos para funções do MSI Center.",
            ImpactLevel.Low, BloatRiskLevel.Safe, RecommendationLevel.Optional, true, "Baixo",
            "Painel de atalhos para modos e funções do MSI Center.", "Fabricante do computador (MSI).", "Em segundo plano.",
            "Acesso rápido aos modos do MSI Center.", "Menos um processo em segundo plano; o MSI Center continua funcionando.",
            "Você perde só o atalho rápido; os mesmos ajustes continuam dentro do MSI Center. Desinstale pelo painel oficial.",
            descriptionEn: "Quick shortcuts for MSI Center functions.",
            whatItIsEn: "Shortcut panel for MSI Center modes and functions.", whoInstalledEn: "Computer manufacturer (MSI).",
            whenUsedEn: "In the background.",
            benefitsKeepingEn: "Quick access to MSI Center modes.", benefitsRemovingEn: "One fewer background process; MSI Center keeps working.",
            consequencesEn: "You only lose the quick shortcut; the same settings remain inside MSI Center. Uninstall via the official panel.")),
        ("Gigabyte Control Center", new Sig("Gigabyte", "Aplicativo (OEM)", "Central de desempenho, ventoinhas e RGB da Gigabyte/AORUS.",
            ImpactLevel.Medium, BloatRiskLevel.NotRecommended, RecommendationLevel.Keep, true, "Médio",
            "Controla perfis de desempenho, curva de ventoinha e iluminação RGB do hardware Gigabyte/AORUS.", "Fabricante do computador (Gigabyte).", "Continuamente em segundo plano.",
            "Único painel que ajusta ventoinhas, modos de desempenho e RGB nessas máquinas.", "Reduz o uso de RAM/CPU em segundo plano.",
            "Ventoinhas e desempenho ficam no perfil padrão da BIOS e o RGB volta ao padrão. Só remova se não usa esses recursos.",
            descriptionEn: "Gigabyte/AORUS performance, fan and RGB center.",
            whatItIsEn: "Controls performance profiles, fan curves and RGB lighting on Gigabyte/AORUS hardware.", whoInstalledEn: "Computer manufacturer (Gigabyte).",
            whenUsedEn: "Continuously in the background.",
            benefitsKeepingEn: "The only panel that adjusts fans, performance modes and RGB on these machines.", benefitsRemovingEn: "Reduces background RAM/CPU usage.",
            consequencesEn: "Fans and performance stay on the BIOS default profile and RGB reverts to default. Remove only if you don't use these features.")),
        ("Huawei PC Manager", new Sig("Huawei", "Aplicativo (OEM)", "Central de drivers e integração com celular da Huawei.",
            ImpactLevel.Medium, BloatRiskLevel.Caution, RecommendationLevel.Optional, true, "Médio",
            "Atualiza drivers e habilita o Multi-Screen Collaboration com celulares Huawei.", "Fabricante do computador (Huawei).", "Continuamente em segundo plano.",
            "Drivers do MateBook e espelhamento com celular Huawei.", "Menos processos em segundo plano.",
            "O espelhamento com o celular Huawei para de funcionar e drivers passam a ser baixados manualmente do site da Huawei. Desinstale pelo painel oficial.",
            descriptionEn: "Huawei driver hub and phone integration.",
            whatItIsEn: "Updates drivers and enables Multi-Screen Collaboration with Huawei phones.", whoInstalledEn: "Computer manufacturer (Huawei).",
            whenUsedEn: "Continuously in the background.",
            benefitsKeepingEn: "MateBook drivers and mirroring with a Huawei phone.", benefitsRemovingEn: "Fewer background processes.",
            consequencesEn: "Mirroring with the Huawei phone stops working and drivers must be downloaded manually from Huawei's site. Uninstall via the official panel.")),
        ("Positivo", new Sig("Positivo", "Aplicativo (OEM)", "Utilitário de suporte pré-instalado pela Positivo.",
            ImpactLevel.Low, BloatRiskLevel.Caution, RecommendationLevel.Optional, true, "Baixo",
            "Aplicativo de suporte, garantia e atualizações dos notebooks Positivo.", "Fabricante do computador (Positivo).", "Em segundo plano e ao consultar suporte.",
            "Acesso rápido a suporte e garantia no Brasil.", "Menos um processo em segundo plano.",
            "Consulte suporte e garantia pelo site da Positivo. Confirme o nome do programa antes de remover, pois alguns utilitários Positivo também trazem drivers. Desinstale pelo painel oficial.",
            descriptionEn: "Support utility pre-installed by Positivo.",
            whatItIsEn: "Support, warranty and update app for Positivo laptops.", whoInstalledEn: "Computer manufacturer (Positivo).",
            whenUsedEn: "In the background and when checking support.",
            benefitsKeepingEn: "Quick access to support and warranty in Brazil.", benefitsRemovingEn: "One fewer background process.",
            consequencesEn: "Check support and warranty on Positivo's site. Confirm the program name before removing, as some Positivo utilities also ship drivers. Uninstall via the official panel.")),
        ("VAIO Care", new Sig("VAIO", "Aplicativo (OEM)", "Diagnóstico, suporte e recuperação dos notebooks VAIO.",
            ImpactLevel.Low, BloatRiskLevel.Caution, RecommendationLevel.Optional, true, "Baixo",
            "Verifica hardware, atualiza drivers e acessa a recuperação do VAIO.", "Fabricante do computador (VAIO).", "Em segundo plano e ao checar o PC.",
            "Diagnóstico e recuperação específicos do VAIO.", "Menos processos em segundo plano.",
            "Diagnóstico e recuperação pelo app deixam de existir; a Redefinição do Windows continua disponível. Desinstale pelo painel oficial.",
            descriptionEn: "VAIO laptop diagnostics, support and recovery.",
            whatItIsEn: "Checks hardware, updates drivers and opens VAIO recovery.", whoInstalledEn: "Computer manufacturer (VAIO).",
            whenUsedEn: "In the background and when checking the PC.",
            benefitsKeepingEn: "VAIO-specific diagnostics and recovery.", benefitsRemovingEn: "Fewer background processes.",
            consequencesEn: "Diagnostics and recovery through the app are gone; Windows Reset remains available. Uninstall via the official panel.")),
        ("McAfee LiveSafe", new Sig("McAfee", "Aplicativo (OEM)", "Antivírus McAfee em versão de teste pré-instalada.",
            ImpactLevel.High, BloatRiskLevel.Caution, RecommendationLevel.Optional, true, "Alto",
            "Antivírus com assinatura paga, instalado em teste pelo fabricante do PC.", "Pré-instalado pelo fabricante do computador (parceria comercial).", "Continuamente em segundo plano.",
            "Proteção antivírus enquanto a assinatura estiver ativa.", "Libera bastante CPU/RAM; após o teste expirar, ele só exibe avisos de renovação e não protege mais.",
            "Se for o único antivírus do PC, remover deixa a máquina desprotegida até o Microsoft Defender assumir — o Defender costuma se reativar automaticamente após a remoção e o reinício, mas confirme em Segurança do Windows antes de navegar.",
            descriptionEn: "McAfee antivirus pre-installed as a trial.",
            whatItIsEn: "Subscription antivirus installed as a trial by the PC manufacturer.", whoInstalledEn: "Pre-installed by the computer manufacturer (commercial partnership).",
            whenUsedEn: "Continuously in the background.",
            benefitsKeepingEn: "Antivirus protection while the subscription is active.", benefitsRemovingEn: "Frees considerable CPU/RAM; once the trial expires it only shows renewal prompts and no longer protects.",
            consequencesEn: "If it's the PC's only antivirus, removing it leaves the machine unprotected until Microsoft Defender takes over — Defender usually re-enables itself after removal and a restart, but confirm in Windows Security before browsing.")),
        ("McAfee WebAdvisor", new Sig("McAfee", "Aplicativo (OEM)", "Extensão de navegação segura da McAfee.",
            ImpactLevel.Low, BloatRiskLevel.Safe, RecommendationLevel.Remove, true, "Baixo",
            "Avalia links de busca e bloqueia sites suspeitos no navegador.", "Pré-instalado pelo fabricante do computador (parceria comercial).", "Enquanto o navegador está aberto.",
            "Aviso de sites suspeitos nos resultados de busca.", "Menos um processo e uma extensão pesando no navegador.",
            "Você perde o selo de segurança nos resultados de busca; o SmartScreen do Edge e o Navegação Segura do Chrome já fazem esse bloqueio. Desinstale pelo painel oficial.",
            descriptionEn: "McAfee safe browsing extension.",
            whatItIsEn: "Rates search links and blocks suspicious sites in the browser.", whoInstalledEn: "Pre-installed by the computer manufacturer (commercial partnership).",
            whenUsedEn: "While the browser is open.",
            benefitsKeepingEn: "Warnings about suspicious sites in search results.", benefitsRemovingEn: "One fewer process and extension weighing on the browser.",
            consequencesEn: "You lose the safety badge in search results; Edge SmartScreen and Chrome Safe Browsing already do that blocking. Uninstall via the official panel.")),
        ("Norton", new Sig("Norton", "Aplicativo (OEM)", "Antivírus Norton em versão de teste pré-instalada.",
            ImpactLevel.High, BloatRiskLevel.Caution, RecommendationLevel.Optional, true, "Alto",
            "Antivírus com assinatura paga (Norton 360 / Security), instalado em teste pelo fabricante.", "Pré-instalado pelo fabricante do computador (parceria comercial).", "Continuamente em segundo plano.",
            "Proteção antivírus e VPN enquanto a assinatura estiver ativa.", "Libera bastante CPU/RAM; após o teste expirar, ele insiste em avisos de renovação sem proteger.",
            "Se for o único antivírus do PC, remover deixa a máquina desprotegida até o Microsoft Defender assumir — confirme em Segurança do Windows após reiniciar. A remoção completa costuma exigir a ferramenta de remoção oficial da Norton.",
            descriptionEn: "Norton antivirus pre-installed as a trial.",
            whatItIsEn: "Subscription antivirus (Norton 360 / Security) installed as a trial by the manufacturer.", whoInstalledEn: "Pre-installed by the computer manufacturer (commercial partnership).",
            whenUsedEn: "Continuously in the background.",
            benefitsKeepingEn: "Antivirus and VPN protection while the subscription is active.", benefitsRemovingEn: "Frees considerable CPU/RAM; once the trial expires it nags about renewal without protecting.",
            consequencesEn: "If it's the PC's only antivirus, removing it leaves the machine unprotected until Microsoft Defender takes over — confirm in Windows Security after restarting. Full removal usually requires Norton's official removal tool.")),
        ("ExpressVPN", new Sig("ExpressVPN", "Aplicativo (OEM)", "VPN em versão de teste pré-instalada.",
            ImpactLevel.Low, BloatRiskLevel.Safe, RecommendationLevel.Optional, true, "Baixo",
            "Serviço de VPN por assinatura, oferecido em teste com o PC novo.", "Pré-instalado pelo fabricante do computador (parceria comercial).", "Em segundo plano, se conectado.",
            "VPN pronta para uso enquanto o teste durar.", "Menos um processo de rede em segundo plano.",
            "Se você usa essa VPN, a conexão protegida deixa de existir e o adaptador virtual é removido. Desinstale pelo painel oficial.",
            descriptionEn: "VPN pre-installed as a trial.",
            whatItIsEn: "Subscription VPN service offered as a trial with the new PC.", whoInstalledEn: "Pre-installed by the computer manufacturer (commercial partnership).",
            whenUsedEn: "In the background, if connected.",
            benefitsKeepingEn: "Ready-to-use VPN while the trial lasts.", benefitsRemovingEn: "One fewer background network process.",
            consequencesEn: "If you use this VPN, the protected connection is gone and the virtual adapter is removed. Uninstall via the official panel.")),
        ("Dropbox Promotion", new Sig("Dropbox", "Aplicativo (OEM)", "Oferta promocional do Dropbox pré-instalada.",
            ImpactLevel.VeryLow, BloatRiskLevel.Safe, RecommendationLevel.Remove, false, "Nenhum",
            "Atalho promocional que oferece espaço extra no Dropbox.", "Pré-instalado pelo fabricante do computador (parceria comercial).", "Apenas na primeira configuração.",
            "Oferta de armazenamento extra.", "Remove um atalho promocional sem função.",
            "Sem efeito prático; se quiser o Dropbox, instale pelo site oficial. Desinstale pelo painel oficial.",
            descriptionEn: "Pre-installed Dropbox promotional offer.",
            whatItIsEn: "Promotional shortcut offering extra Dropbox space.", whoInstalledEn: "Pre-installed by the computer manufacturer (commercial partnership).",
            whenUsedEn: "Only during initial setup.",
            benefitsKeepingEn: "Extra storage offer.", benefitsRemovingEn: "Removes a promotional shortcut with no function.",
            consequencesEn: "No practical effect; if you want Dropbox, install it from the official site. Uninstall via the official panel.")),
        ("Booking.com", new Sig("Booking.com", "Aplicativo (OEM)", "Aplicativo de reservas pré-instalado.",
            ImpactLevel.VeryLow, BloatRiskLevel.Safe, RecommendationLevel.Remove, false, "Nenhum",
            "Atalho para o site de reservas de hotéis.", "Pré-instalado pelo fabricante do computador (parceria comercial).", "Raramente.",
            "Acesso rápido às reservas.", "Remove um app promocional que você provavelmente nunca abriu.",
            "Sem efeito prático; o site continua acessível pelo navegador. Desinstale pelo painel oficial.",
            descriptionEn: "Pre-installed booking application.",
            whatItIsEn: "Shortcut to the hotel booking website.", whoInstalledEn: "Pre-installed by the computer manufacturer (commercial partnership).",
            whenUsedEn: "Rarely.",
            benefitsKeepingEn: "Quick access to bookings.", benefitsRemovingEn: "Removes a promotional app you probably never opened.",
            consequencesEn: "No practical effect; the site remains accessible in the browser. Uninstall via the official panel.")),
        ("Alexa", new Sig("Amazon", "Aplicativo (OEM)", "Assistente Alexa pré-instalada pelo fabricante.",
            ImpactLevel.Medium, BloatRiskLevel.Safe, RecommendationLevel.Optional, true, "Médio",
            "Assistente de voz da Amazon, com escuta por palavra-chave.", "Pré-instalado pelo fabricante do computador (parceria comercial).", "Continuamente em segundo plano, se ativada.",
            "Comandos de voz e controle de casa inteligente pelo PC.", "Menos um processo usando microfone e rede em segundo plano.",
            "Os comandos de voz da Alexa deixam de funcionar neste PC; seus dispositivos Alexa continuam normais. Desinstale pelo painel oficial.",
            descriptionEn: "Alexa assistant pre-installed by the manufacturer.",
            whatItIsEn: "Amazon's voice assistant, with wake-word listening.", whoInstalledEn: "Pre-installed by the computer manufacturer (commercial partnership).",
            whenUsedEn: "Continuously in the background, if enabled.",
            benefitsKeepingEn: "Voice commands and smart home control from the PC.", benefitsRemovingEn: "One fewer process using the microphone and network in the background.",
            consequencesEn: "Alexa voice commands stop working on this PC; your Alexa devices are unaffected. Uninstall via the official panel.")),
        ("WildTangent", new Sig("WildTangent", "Aplicativo (OEM)", "Vitrine de jogos casuais pré-instalada.",
            ImpactLevel.Low, BloatRiskLevel.Safe, RecommendationLevel.Remove, true, "Baixo",
            "Loja de jogos casuais com ofertas e notificações.", "Pré-instalado pelo fabricante do computador (parceria comercial).", "Em segundo plano.",
            "Acesso a jogos casuais gratuitos por tempo limitado.", "Menos notificações promocionais e processos em segundo plano.",
            "Sem efeito no funcionamento do PC; jogos instalados por ela podem parar de abrir. Desinstale pelo painel oficial.",
            descriptionEn: "Pre-installed casual games showcase.",
            whatItIsEn: "Casual game store with offers and notifications.", whoInstalledEn: "Pre-installed by the computer manufacturer (commercial partnership).",
            whenUsedEn: "In the background.",
            benefitsKeepingEn: "Access to time-limited free casual games.", benefitsRemovingEn: "Fewer promotional notifications and background processes.",
            consequencesEn: "No effect on the PC's operation; games installed through it may stop opening. Uninstall via the official panel.")),
        ("Keeper", new Sig("Keeper Security", "Aplicativo (OEM)", "Gerenciador de senhas em versão de teste.",
            ImpactLevel.Low, BloatRiskLevel.Caution, RecommendationLevel.Optional, true, "Baixo",
            "Cofre de senhas por assinatura, oferecido em teste com o PC novo.", "Pré-instalado pelo fabricante do computador (parceria comercial).", "Em segundo plano e ao preencher senhas.",
            "Cofre de senhas pronto para uso.", "Menos um processo em segundo plano se você usa outro gerenciador.",
            "ATENÇÃO: se você já guardou senhas nele, exporte-as antes de remover — o cofre local é apagado na desinstalação. Desinstale pelo painel oficial.",
            descriptionEn: "Password manager trial version.",
            whatItIsEn: "Subscription password vault offered as a trial with the new PC.", whoInstalledEn: "Pre-installed by the computer manufacturer (commercial partnership).",
            whenUsedEn: "In the background and when filling passwords.",
            benefitsKeepingEn: "Ready-to-use password vault.", benefitsRemovingEn: "One fewer background process if you use another manager.",
            consequencesEn: "WARNING: if you already stored passwords in it, export them before removing — the local vault is erased on uninstall. Uninstall via the official panel.")),
        ("Killer Control Center", new Sig("Intel/Rivet", "Aplicativo (OEM)", "Painel de prioridade de rede das placas Killer.",
            ImpactLevel.Low, BloatRiskLevel.NotRecommended, RecommendationLevel.Keep, true, "Baixo",
            "Prioriza o tráfego de jogos e chamadas nas placas de rede Killer.", "Fabricante do computador (placa de rede Killer).", "Continuamente em segundo plano.",
            "Prioridade de banda para jogos e menor latência nas placas Killer.", "Menos um processo em segundo plano.",
            "Remover o painel sem cuidado pode levar junto componentes do driver da placa Killer e derrubar o Wi-Fi/Ethernet. Recomendamos manter; se remover, tenha o driver Killer baixado antes.",
            descriptionEn: "Network prioritization panel for Killer network cards.",
            whatItIsEn: "Prioritizes game and call traffic on Killer network cards.", whoInstalledEn: "Computer manufacturer (Killer network card).",
            whenUsedEn: "Continuously in the background.",
            benefitsKeepingEn: "Bandwidth priority for games and lower latency on Killer cards.", benefitsRemovingEn: "One fewer background process.",
            consequencesEn: "Removing the panel carelessly can take Killer driver components with it and break Wi-Fi/Ethernet. We recommend keeping it; if you remove it, download the Killer driver first.")),
        ("Nahimic", new Sig("SteelSeries/A-Volute", "Aplicativo (OEM)", "Processamento de áudio Nahimic pré-instalado.",
            ImpactLevel.Low, BloatRiskLevel.NotRecommended, RecommendationLevel.Keep, true, "Baixo",
            "Aplica som surround virtual, equalização e supressão de ruído do microfone.", "Fabricante do computador (junto do driver de áudio).", "Continuamente em segundo plano.",
            "Efeitos de áudio e supressão de ruído do microfone que o notebook usa por padrão.", "Menos um processo de áudio em segundo plano.",
            "O som pode ficar mais fraco ou sem graves e o cancelamento de ruído do microfone some; em alguns modelos, remover também afeta o driver de áudio. Recomendamos manter.",
            descriptionEn: "Pre-installed Nahimic audio processing.",
            whatItIsEn: "Applies virtual surround sound, equalization and microphone noise suppression.", whoInstalledEn: "Computer manufacturer (bundled with the audio driver).",
            whenUsedEn: "Continuously in the background.",
            benefitsKeepingEn: "Audio effects and microphone noise suppression the laptop uses by default.", benefitsRemovingEn: "One fewer background audio process.",
            consequencesEn: "Sound may become weaker or lose bass and microphone noise cancellation disappears; on some models removing it also affects the audio driver. We recommend keeping it.")),
        ("Sonic Studio", new Sig("ASUS/A-Volute", "Aplicativo (OEM)", "Processamento de áudio Sonic Studio da ASUS.",
            ImpactLevel.Low, BloatRiskLevel.NotRecommended, RecommendationLevel.Keep, true, "Baixo",
            "Aplica efeitos de áudio, equalização e supressão de ruído em hardware ASUS.", "Fabricante do computador (junto do driver de áudio ASUS).", "Continuamente em segundo plano.",
            "Efeitos de áudio e microfone que o notebook ASUS usa por padrão.", "Menos um processo de áudio em segundo plano.",
            "O som perde os efeitos e a supressão de ruído do microfone; em alguns modelos, remover também afeta o driver de áudio. Recomendamos manter.",
            descriptionEn: "ASUS Sonic Studio audio processing.",
            whatItIsEn: "Applies audio effects, equalization and noise suppression on ASUS hardware.", whoInstalledEn: "Computer manufacturer (bundled with the ASUS audio driver).",
            whenUsedEn: "Continuously in the background.",
            benefitsKeepingEn: "Audio and microphone effects the ASUS laptop uses by default.", benefitsRemovingEn: "One fewer background audio process.",
            consequencesEn: "Sound loses its effects and microphone noise suppression; on some models removing it also affects the audio driver. We recommend keeping it.")),
    };

    // ---- Serviços conhecidos (por nome exato, minúsculo) ----
    private static readonly Dictionary<string, Sig> ServiceSigs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DiagTrack"] = new Sig("Microsoft", "Serviço", "Experiências do Usuário Conectado e Telemetria: coleta e envia dados de diagnóstico à Microsoft.",
            ImpactLevel.Medium, BloatRiskLevel.Safe, RecommendationLevel.Remove, true, "Baixo",
            "Serviço central de telemetria do Windows.", "Componente do Windows.", "Continuamente em segundo plano.",
            "Ajuda a Microsoft a diagnosticar problemas.", "Menos coleta de dados e atividade em segundo plano.",
            "Reduz a telemetria; não afeta o uso normal do PC. Reversível.",
            descriptionEn: "Connected User Experiences and Telemetry: collects and sends diagnostic data to Microsoft.",
            whatItIsEn: "Windows' central telemetry service.", whoInstalledEn: "Windows component.", whenUsedEn: "Continuously in the background.",
            benefitsKeepingEn: "Helps Microsoft diagnose problems.", benefitsRemovingEn: "Less data collection and background activity.",
            consequencesEn: "Reduces telemetry; doesn't affect normal PC use. Reversible."),
        ["dmwappushservice"] = new Sig("Microsoft", "Serviço", "Roteamento de mensagens WAP (associado à telemetria).",
            ImpactLevel.Low, BloatRiskLevel.Safe, RecommendationLevel.Remove, true, "Baixo",
            "Serviço de mensagens WAP usado em coleta de diagnóstico.", "Componente do Windows.", "Em segundo plano.",
            "Funções de mensagens WAP raramente usadas.", "Menos atividade de telemetria.", "Sem impacto no uso comum. Reversível.",
            descriptionEn: "WAP message routing (associated with telemetry).",
            whatItIsEn: "WAP messaging service used in diagnostic collection.", whoInstalledEn: "Windows component.", whenUsedEn: "In the background.",
            benefitsKeepingEn: "Rarely used WAP messaging functions.", benefitsRemovingEn: "Less telemetry activity.",
            consequencesEn: "No impact on common use. Reversible."),
        ["NvTelemetryContainer"] = new Sig("NVIDIA", "Serviço", "Telemetria da NVIDIA: envia informações de diagnóstico e uso para a NVIDIA.",
            ImpactLevel.Low, BloatRiskLevel.Safe, RecommendationLevel.Remove, true, "Baixo",
            "Contêiner de telemetria do driver NVIDIA.", "Instalado com o driver NVIDIA.", "Em segundo plano.",
            "Envio de diagnósticos à NVIDIA.", "Menos coleta de dados; jogos e driver não são afetados.",
            "Não afeta o desempenho gráfico. Reversível.",
            descriptionEn: "NVIDIA telemetry: sends diagnostic and usage information to NVIDIA.",
            whatItIsEn: "Telemetry container for the NVIDIA driver.", whoInstalledEn: "Installed with the NVIDIA driver.", whenUsedEn: "In the background.",
            benefitsKeepingEn: "Sends diagnostics to NVIDIA.", benefitsRemovingEn: "Less data collection; games and driver are not affected.",
            consequencesEn: "Doesn't affect graphics performance. Reversible."),
        ["gupdate"] = new Sig("Google", "Serviço", "Google Update: mantém produtos Google (Chrome) atualizados em segundo plano.",
            ImpactLevel.Low, BloatRiskLevel.Caution, RecommendationLevel.Optional, true, "Baixo",
            "Atualizador automático do Google.", "Instalado com o Chrome/produtos Google.", "Periodicamente.",
            "Atualizações automáticas do Chrome.", "Menos processos em segundo plano.",
            "O Chrome deixa de se atualizar sozinho — atualize manualmente de vez em quando. Reversível.",
            descriptionEn: "Google Update: keeps Google products (Chrome) updated in the background.",
            whatItIsEn: "Google's automatic updater.", whoInstalledEn: "Installed with Chrome/Google products.", whenUsedEn: "Periodically.",
            benefitsKeepingEn: "Automatic Chrome updates.", benefitsRemovingEn: "Fewer background processes.",
            consequencesEn: "Chrome stops updating itself — update it manually occasionally. Reversible."),
        ["gupdatem"] = new Sig("Google", "Serviço", "Google Update (por máquina): atualizador automático do Google.",
            ImpactLevel.Low, BloatRiskLevel.Caution, RecommendationLevel.Optional, true, "Baixo",
            "Atualizador automático do Google.", "Instalado com produtos Google.", "Periodicamente.",
            "Atualizações automáticas do Chrome.", "Menos processos em segundo plano.",
            "O Chrome deixa de se atualizar sozinho. Reversível.",
            descriptionEn: "Google Update (machine-wide): Google's automatic updater.",
            whatItIsEn: "Google's automatic updater.", whoInstalledEn: "Installed with Google products.", whenUsedEn: "Periodically.",
            benefitsKeepingEn: "Automatic Chrome updates.", benefitsRemovingEn: "Fewer background processes.",
            consequencesEn: "Chrome stops updating itself. Reversible."),
        ["AdobeARMservice"] = new Sig("Adobe", "Serviço", "Adobe Acrobat Update Service: verifica atualizações do Acrobat/Reader.",
            ImpactLevel.Low, BloatRiskLevel.Caution, RecommendationLevel.Optional, true, "Baixo",
            "Atualizador automático do Adobe Acrobat/Reader.", "Instalado com produtos Adobe.", "Periodicamente.",
            "Mantém o Acrobat/Reader atualizado.", "Menos atividade em segundo plano.",
            "Atualize o Acrobat manualmente de tempos em tempos. Reversível.",
            descriptionEn: "Adobe Acrobat Update Service: checks for Acrobat/Reader updates.",
            whatItIsEn: "Automatic updater for Adobe Acrobat/Reader.", whoInstalledEn: "Installed with Adobe products.", whenUsedEn: "Periodically.",
            benefitsKeepingEn: "Keeps Acrobat/Reader updated.", benefitsRemovingEn: "Less background activity.",
            consequencesEn: "Update Acrobat manually from time to time. Reversible."),
        ["edgeupdate"] = new Sig("Microsoft", "Serviço", "Microsoft Edge Update: atualizador automático do Edge.",
            ImpactLevel.Low, BloatRiskLevel.Caution, RecommendationLevel.Keep, true, "Baixo",
            "Mantém o navegador Edge atualizado (inclui correções de segurança).", "Componente do Edge.", "Periodicamente.",
            "Correções de segurança automáticas do navegador.", "Menos processos — mas perde atualizações automáticas.",
            "Recomendado manter por segurança do navegador. Reversível.",
            descriptionEn: "Microsoft Edge Update: automatic updater for Edge.",
            whatItIsEn: "Keeps the Edge browser updated (including security fixes).", whoInstalledEn: "Edge component.", whenUsedEn: "Periodically.",
            benefitsKeepingEn: "Automatic browser security fixes.", benefitsRemovingEn: "Fewer processes — but loses automatic updates.",
            consequencesEn: "Recommended to keep for browser security. Reversible."),
        ["MozillaMaintenance"] = new Sig("Mozilla", "Serviço", "Mozilla Maintenance Service: aplica atualizações do Firefox.",
            ImpactLevel.VeryLow, BloatRiskLevel.Caution, RecommendationLevel.Optional, false, "Nenhum",
            "Serviço auxiliar de atualização do Firefox.", "Instalado com o Firefox.", "Ao atualizar o Firefox.",
            "Atualizações do Firefox sem prompt de permissão.", "Remove serviço auxiliar; o Firefox ainda atualiza.",
            "O Firefox pode pedir permissão ao atualizar. Reversível.",
            descriptionEn: "Mozilla Maintenance Service: applies Firefox updates.",
            whatItIsEn: "Auxiliary update service for Firefox.", whoInstalledEn: "Installed with Firefox.", whenUsedEn: "When updating Firefox.",
            benefitsKeepingEn: "Firefox updates without a permission prompt.", benefitsRemovingEn: "Removes an auxiliary service; Firefox still updates.",
            consequencesEn: "Firefox may ask for permission when updating. Reversible."),
    };

    // ---- Tarefas agendadas conhecidas (por trecho do caminho) ----
    private static readonly (string fragment, Sig sig)[] TaskSigs =
    {
        ("Application Experience", new Sig("Microsoft", "Tarefa agendada", "Avaliador de compatibilidade de aplicativos (telemetria).",
            ImpactLevel.Low, BloatRiskLevel.Safe, RecommendationLevel.Remove, false, "Baixo",
            "Coleta dados de compatibilidade/uso de programas.", "Componente do Windows.", "Periodicamente.",
            "Ajuda em diagnósticos de compatibilidade.", "Menos telemetria em segundo plano.", "Sem impacto no uso comum. Reversível.",
            descriptionEn: "Application compatibility evaluator (telemetry).",
            whatItIsEn: "Collects program compatibility/usage data.", whoInstalledEn: "Windows component.", whenUsedEn: "Periodically.",
            benefitsKeepingEn: "Helps with compatibility diagnostics.", benefitsRemovingEn: "Less telemetry in the background.",
            consequencesEn: "No impact on common use. Reversible.")),
        ("Customer Experience Improvement Program", new Sig("Microsoft", "Tarefa agendada", "Programa de Melhoria da Experiência (CEIP / telemetria).",
            ImpactLevel.Low, BloatRiskLevel.Safe, RecommendationLevel.Remove, false, "Baixo",
            "Envia dados de uso anônimos à Microsoft.", "Componente do Windows.", "Periodicamente.",
            "Contribui com dados de melhoria.", "Menos telemetria.", "Sem impacto no uso comum. Reversível.",
            descriptionEn: "Customer Experience Improvement Program (CEIP / telemetry).",
            whatItIsEn: "Sends anonymous usage data to Microsoft.", whoInstalledEn: "Windows component.", whenUsedEn: "Periodically.",
            benefitsKeepingEn: "Contributes improvement data.", benefitsRemovingEn: "Less telemetry.",
            consequencesEn: "No impact on common use. Reversible.")),
        ("NvTmRep", new Sig("NVIDIA", "Tarefa agendada", "Relatório de telemetria da NVIDIA.",
            ImpactLevel.VeryLow, BloatRiskLevel.Safe, RecommendationLevel.Remove, false, "Nenhum",
            "Tarefa que envia telemetria do driver NVIDIA.", "Instalada com o driver NVIDIA.", "Periodicamente.",
            "Diagnósticos à NVIDIA.", "Menos telemetria; gráficos não são afetados.", "Sem impacto no desempenho. Reversível.",
            descriptionEn: "NVIDIA telemetry report.",
            whatItIsEn: "Task that sends telemetry from the NVIDIA driver.", whoInstalledEn: "Installed with the NVIDIA driver.", whenUsedEn: "Periodically.",
            benefitsKeepingEn: "Diagnostics sent to NVIDIA.", benefitsRemovingEn: "Less telemetry; graphics are not affected.",
            consequencesEn: "No impact on performance. Reversible.")),
        ("NvProfileUpdater", new Sig("NVIDIA", "Tarefa agendada", "Atualizador de perfis de jogos da NVIDIA.",
            ImpactLevel.VeryLow, BloatRiskLevel.Caution, RecommendationLevel.Optional, false, "Nenhum",
            "Atualiza perfis de otimização de jogos.", "Instalada com o driver NVIDIA.", "Periodicamente.",
            "Perfis de jogo atualizados automaticamente.", "Menos uma tarefa em segundo plano.",
            "Perfis de jogo deixam de atualizar sozinhos. Reversível.",
            descriptionEn: "NVIDIA game profile updater.",
            whatItIsEn: "Updates game optimization profiles.", whoInstalledEn: "Installed with the NVIDIA driver.", whenUsedEn: "Periodically.",
            benefitsKeepingEn: "Game profiles updated automatically.", benefitsRemovingEn: "One fewer background task.",
            consequencesEn: "Game profiles stop updating automatically. Reversible.")),
        ("GoogleUpdateTask", new Sig("Google", "Tarefa agendada", "Tarefa de atualização automática do Google.",
            ImpactLevel.Low, BloatRiskLevel.Caution, RecommendationLevel.Optional, false, "Baixo",
            "Verifica atualizações de produtos Google.", "Instalada com o Chrome.", "Periodicamente.",
            "Chrome sempre atualizado.", "Menos tarefas em segundo plano.", "Atualize o Chrome manualmente às vezes. Reversível.",
            descriptionEn: "Google automatic update task.",
            whatItIsEn: "Checks for Google product updates.", whoInstalledEn: "Installed with Chrome.", whenUsedEn: "Periodically.",
            benefitsKeepingEn: "Chrome always up to date.", benefitsRemovingEn: "Fewer background tasks.",
            consequencesEn: "Update Chrome manually occasionally. Reversible.")),
        ("Adobe Acrobat Update", new Sig("Adobe", "Tarefa agendada", "Tarefa de atualização do Adobe Acrobat/Reader.",
            ImpactLevel.Low, BloatRiskLevel.Caution, RecommendationLevel.Optional, false, "Baixo",
            "Verifica atualizações do Acrobat/Reader.", "Instalada com produtos Adobe.", "Periodicamente.",
            "Acrobat sempre atualizado.", "Menos tarefas em segundo plano.", "Atualize o Acrobat manualmente. Reversível.",
            descriptionEn: "Adobe Acrobat/Reader update task.",
            whatItIsEn: "Checks for Acrobat/Reader updates.", whoInstalledEn: "Installed with Adobe products.", whenUsedEn: "Periodically.",
            benefitsKeepingEn: "Acrobat always up to date.", benefitsRemovingEn: "Fewer background tasks.",
            consequencesEn: "Update Acrobat manually. Reversible.")),
        ("OneDrive Standalone Update", new Sig("Microsoft", "Tarefa agendada", "Atualizador automático do OneDrive.",
            ImpactLevel.VeryLow, BloatRiskLevel.Caution, RecommendationLevel.Optional, false, "Nenhum",
            "Mantém o cliente do OneDrive atualizado.", "Instalada com o OneDrive.", "Periodicamente.",
            "OneDrive sempre atualizado.", "Menos tarefas em segundo plano.", "Atualize o OneDrive manualmente. Reversível.",
            descriptionEn: "OneDrive automatic updater.",
            whatItIsEn: "Keeps the OneDrive client updated.", whoInstalledEn: "Installed with OneDrive.", whenUsedEn: "Periodically.",
            benefitsKeepingEn: "OneDrive always up to date.", benefitsRemovingEn: "Fewer background tasks.",
            consequencesEn: "Update OneDrive manually. Reversible.")),
        ("MicrosoftEdgeUpdate", new Sig("Microsoft", "Tarefa agendada", "Tarefa de atualização do Microsoft Edge.",
            ImpactLevel.VeryLow, BloatRiskLevel.Caution, RecommendationLevel.Keep, false, "Nenhum",
            "Verifica atualizações de segurança do Edge.", "Componente do Edge.", "Periodicamente.",
            "Correções de segurança do navegador.", "Menos tarefas — mas perde atualizações.",
            "Recomendado manter por segurança. Reversível.",
            descriptionEn: "Microsoft Edge update task.",
            whatItIsEn: "Checks for Edge security updates.", whoInstalledEn: "Edge component.", whenUsedEn: "Periodically.",
            benefitsKeepingEn: "Browser security fixes.", benefitsRemovingEn: "Fewer tasks — but loses updates.",
            consequencesEn: "Recommended to keep for security. Reversible.")),
    };

    // ---- Itens de inicialização conhecidos (por nome/caminho) ----
    private static readonly (string fragment, Sig sig)[] StartupSigs =
    {
        ("OneDrive", new Sig("Microsoft", "Inicialização", "OneDrive inicia junto com o Windows para sincronizar arquivos.",
            ImpactLevel.Medium, BloatRiskLevel.Caution, RecommendationLevel.Optional, true, "Médio",
            "Cliente de sincronização de arquivos na nuvem.", "Componente do Windows / instalado por você.", "Continuamente, ao sincronizar.",
            "Arquivos sincronizados automaticamente.", "Inicialização mais rápida; sincronize manualmente quando abrir.",
            "Se você usa o OneDrive ativamente, mantenha. Reversível.",
            descriptionEn: "OneDrive starts with Windows to sync files.",
            whatItIsEn: "Cloud file sync client.", whoInstalledEn: "Windows component / installed by you.", whenUsedEn: "Continuously, while syncing.",
            benefitsKeepingEn: "Files synced automatically.", benefitsRemovingEn: "Faster startup; sync manually when you open it.",
            consequencesEn: "If you actively use OneDrive, keep it. Reversible.")),
        ("Discord", new Sig("Discord", "Inicialização", "Discord abre automaticamente no boot.",
            ImpactLevel.Medium, BloatRiskLevel.Safe, RecommendationLevel.Optional, true, "Médio",
            "Aplicativo de chat de voz/texto.", "Instalado por você.", "Ao iniciar o Windows.",
            "Discord pronto assim que liga o PC.", "Inicialização mais rápida; abra o Discord quando precisar.",
            "Você só precisa abri-lo manualmente. Reversível.",
            descriptionEn: "Discord opens automatically at boot.",
            whatItIsEn: "Voice/text chat app.", whoInstalledEn: "Installed by you.", whenUsedEn: "When Windows starts.",
            benefitsKeepingEn: "Discord ready as soon as the PC turns on.", benefitsRemovingEn: "Faster startup; open Discord when needed.",
            consequencesEn: "You just need to open it manually. Reversible.")),
        ("Steam", new Sig("Valve", "Inicialização", "Steam inicia junto com o Windows.",
            ImpactLevel.Medium, BloatRiskLevel.Safe, RecommendationLevel.Optional, true, "Médio",
            "Plataforma de jogos.", "Instalado por você.", "Ao iniciar o Windows.",
            "Steam e downloads prontos no boot.", "Inicialização mais rápida.", "Abra a Steam manualmente ao jogar. Reversível.",
            descriptionEn: "Steam starts with Windows.",
            whatItIsEn: "Gaming platform.", whoInstalledEn: "Installed by you.", whenUsedEn: "When Windows starts.",
            benefitsKeepingEn: "Steam and downloads ready at boot.", benefitsRemovingEn: "Faster startup.",
            consequencesEn: "Open Steam manually when gaming. Reversible.")),
        ("EpicGames", new Sig("Epic Games", "Inicialização", "Epic Games Launcher inicia junto com o Windows.",
            ImpactLevel.Medium, BloatRiskLevel.Safe, RecommendationLevel.Optional, true, "Médio",
            "Launcher de jogos da Epic.", "Instalado por você.", "Ao iniciar o Windows.",
            "Launcher pronto no boot.", "Inicialização mais rápida.", "Abra o launcher manualmente. Reversível.",
            descriptionEn: "Epic Games Launcher starts with Windows.",
            whatItIsEn: "Epic's game launcher.", whoInstalledEn: "Installed by you.", whenUsedEn: "When Windows starts.",
            benefitsKeepingEn: "Launcher ready at boot.", benefitsRemovingEn: "Faster startup.",
            consequencesEn: "Open the launcher manually. Reversible.")),
        ("Spotify", new Sig("Spotify AB", "Inicialização", "Spotify abre automaticamente no boot.",
            ImpactLevel.Low, BloatRiskLevel.Safe, RecommendationLevel.Optional, true, "Baixo",
            "Player de música.", "Instalado por você.", "Ao iniciar o Windows.",
            "Música pronta no boot.", "Inicialização mais rápida.", "Abra o Spotify manualmente. Reversível.",
            descriptionEn: "Spotify opens automatically at boot.",
            whatItIsEn: "Music player.", whoInstalledEn: "Installed by you.", whenUsedEn: "When Windows starts.",
            benefitsKeepingEn: "Music ready at boot.", benefitsRemovingEn: "Faster startup.",
            consequencesEn: "Open Spotify manually. Reversible.")),
        ("Adobe", new Sig("Adobe", "Inicialização", "Componente da Adobe na inicialização (atualizador/Creative Cloud).",
            ImpactLevel.Low, BloatRiskLevel.Caution, RecommendationLevel.Optional, true, "Baixo",
            "Auxiliar/atualizador de produtos Adobe.", "Instalado com produtos Adobe.", "Ao iniciar o Windows.",
            "Produtos Adobe prontos e atualizados.", "Inicialização mais rápida.", "Os apps Adobe ainda abrem normalmente. Reversível.",
            descriptionEn: "Adobe startup component (updater/Creative Cloud).",
            whatItIsEn: "Auxiliary/updater for Adobe products.", whoInstalledEn: "Installed with Adobe products.", whenUsedEn: "When Windows starts.",
            benefitsKeepingEn: "Adobe products ready and updated.", benefitsRemovingEn: "Faster startup.",
            consequencesEn: "Adobe apps still open normally. Reversible.")),
        ("Teams", new Sig("Microsoft", "Inicialização", "Microsoft Teams inicia junto com o Windows.",
            ImpactLevel.Medium, BloatRiskLevel.Safe, RecommendationLevel.Optional, true, "Médio",
            "Chat e reuniões.", "Instalado por você/empresa.", "Ao iniciar o Windows.",
            "Teams pronto no boot.", "Inicialização mais rápida.", "Abra o Teams manualmente. Reversível.",
            descriptionEn: "Microsoft Teams starts with Windows.",
            whatItIsEn: "Chat and meetings.", whoInstalledEn: "Installed by you/company.", whenUsedEn: "When Windows starts.",
            benefitsKeepingEn: "Teams ready at boot.", benefitsRemovingEn: "Faster startup.",
            consequencesEn: "Open Teams manually. Reversible.")),
    };

    // ===================== Helpers de Registro =====================

    private int? GetDword(RegistryHive hive, string subKey, string name)
    {
        try
        {
            var root = hive == RegistryHive.LocalMachine ? Registry.LocalMachine : Registry.CurrentUser;
            using var key = root.OpenSubKey(subKey);
            return key?.GetValue(name) is int i ? i : (int?)null;
        }
        catch { return null; }
    }

    private void SetDwordRaw(RegistryHive hive, string subKey, string name, int value)
    {
        try
        {
            var root = hive == RegistryHive.LocalMachine ? Registry.LocalMachine : Registry.CurrentUser;
            using var wk = root.CreateSubKey(subKey);
            wk.SetValue(name, value, RegistryValueKind.DWord);
        }
        catch { }
    }

    private void DeleteValue(RegistryHive hive, string subKey, string name)
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

    // ===================== Preferências =====================

    private void LoadPrefs()
    {
        try
        {
            if (File.Exists(_prefsPath))
                _prefs = JsonSerializer.Deserialize<BloatwarePrefs>(File.ReadAllText(_prefsPath)) ?? new();
        }
        catch { _prefs = new(); }
    }

    private void SavePrefs()
    {
        try { File.WriteAllText(_prefsPath, JsonSerializer.Serialize(_prefs, new JsonSerializerOptions { WriteIndented = true })); }
        catch { }
    }

    // ===================== Util =====================

    private static string FriendlyAppName(string packageName)
    {
        // "Microsoft.BingNews" -> "Bing News"; remove o prefixo do publicador e separa CamelCase.
        string n = packageName.Contains('.') ? packageName[(packageName.LastIndexOf('.') + 1)..] : packageName;
        var sb = new StringBuilder();
        for (int i = 0; i < n.Length; i++)
        {
            if (i > 0 && char.IsUpper(n[i]) && !char.IsUpper(n[i - 1])) sb.Append(' ');
            sb.Append(n[i]);
        }
        return sb.ToString();
    }

    private static string TaskLeafName(string taskPath)
    {
        string p = taskPath.TrimEnd('\\');
        int idx = p.LastIndexOf('\\');
        return idx >= 0 ? p[(idx + 1)..] : p;
    }

    private static (string file, string args) SplitUninstallCommand(string command)
    {
        command = command.Trim();
        if (command.StartsWith("\""))
        {
            int end = command.IndexOf('"', 1);
            if (end > 0)
                return (command[1..end], command[(end + 1)..].Trim());
        }
        int space = command.IndexOf(' ');
        return space < 0 ? (command, "") : (command[..space], command[(space + 1)..].Trim());
    }

    private static List<string> ParseCsvLine(string line)
    {
        var cells = new List<string>();
        if (string.IsNullOrEmpty(line)) return cells;
        var sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes) { cells.Add(sb.ToString()); sb.Clear(); }
            else if (c != '\r') sb.Append(c);
        }
        cells.Add(sb.ToString());
        return cells;
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
            };
            using var proc = new Process { StartInfo = psi };
            var sb = new StringBuilder();
            proc.OutputDataReceived += (_, e) => { if (e.Data is not null) sb.AppendLine(e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) sb.AppendLine(e.Data); };
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            await proc.WaitForExitAsync();
            return (proc.ExitCode, sb.ToString());
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }
}
