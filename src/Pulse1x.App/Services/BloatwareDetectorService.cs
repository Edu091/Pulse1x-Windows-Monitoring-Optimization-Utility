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
    /// Desinstala um aplicativo. Para UWP, remove o pacote para o usuário atual; para Win32, abre o
    /// desinstalador OFICIAL do programa (o Pulse1x não apaga arquivos de programas por conta própria).
    /// A desinstalação não é revertida pelo Pulse1x — o app pode ser reinstalado pela loja/fabricante.
    /// </summary>
    public async Task UninstallAsync(BloatwareItem item)
    {
        if (item.IsCritical) return;

        if (item.IsUwp && item.PackageFullName is not null)
        {
            await RunAsync("powershell",
                $"-NoProfile -ExecutionPolicy Bypass -Command \"Remove-AppxPackage -Package '{item.PackageFullName}'\"");
            item.CurrentlyEnabled = false;
        }
        else if (!string.IsNullOrWhiteSpace(item.UninstallString))
        {
            // Abre o desinstalador oficial; a remoção em si é conduzida pelo próprio programa.
            try
            {
                var (file, args) = SplitUninstallCommand(item.UninstallString!);
                Process.Start(new ProcessStartInfo { FileName = file, Arguments = args, UseShellExecute = true });
            }
            catch { /* desinstalador indisponível */ }
        }
    }

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
    };

    private static readonly string[] OemEssentialKeywords =
    {
        "Driver", "BIOS", "Firmware", "Chipset", "Audio Driver", "Graphics Driver", "Touchpad",
        "Fingerprint", "Camera Driver", "Bluetooth Driver", "Wireless Driver", "WLAN Driver",
        "Trusted Platform", "Modem", "Realtek", "Synaptics", "Precision Touchpad",
    };

    private static readonly string[] OemBloatKeywords =
    {
        "Assist", "Companion", "Center", "Hub", "Suite", "Connect", "Link", "Promotion", "Welcome",
        "Experience", "Service Agent", "Registration", "Optimizer", "Advisor", "Now", "Vantage", "Care",
        "Gift", "Live Update", "Quick Access", "JumpStart", "Customer", "Premium",
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
