using System.Diagnostics;
using System.Management;
using Microsoft.Win32;
using Pulse1x.App.Models;

namespace Pulse1x.App.Services;

/// <summary>Resultado de uma tentativa de instalação de um item.</summary>
public enum InstallOutcome { Success, AlreadyInstalled, OpenedOfficialPage, Failed }

public sealed record InstallResult(InstallOutcome Outcome, string Message);

/// <summary>Retrato do hardware/SO detectado, usado para destacar recomendações compatíveis.</summary>
public sealed record SystemProfile(
    string GpuName,
    string CpuName,
    string WindowsVersion,
    string Architecture,
    string OemManufacturer,
    IReadOnlySet<HwVendor> Vendors);

/// <summary>
/// Backend da Central Pós-Formatação. Instala SEMPRE de fonte oficial: usa o winget (Windows
/// Package Manager), que baixa os pacotes direto dos publicadores oficiais; para itens sem
/// pacote winget (drivers, ferramentas OEM), abre a página oficial do fabricante.
///
/// Também faz a Detecção Inteligente do hardware (GPU/CPU/fabricante/SO) por WMI, para a UI
/// destacar automaticamente os aplicativos compatíveis (ex.: GPU NVIDIA → apps NVIDIA).
/// </summary>
public class AppInstallService
{
    private bool? _wingetAvailable;

    /// <summary>True se o winget está instalado (App Installer presente). Cacheado após a 1ª checagem.</summary>
    public bool IsWingetAvailable => _wingetAvailable ??= DetectWinget();

    private static bool DetectWinget()
    {
        try
        {
            var psi = new ProcessStartInfo("winget", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return false;
            p.WaitForExit(8000);
            return p.HasExited && p.ExitCode == 0;
        }
        catch { return false; }
    }

    /// <summary>Instala (ou abre a página oficial de) um item, reportando a saída linha a linha.</summary>
    public async Task<InstallResult> InstallAsync(AppEntry app, IProgress<string>? onOutput = null)
    {
        // Itens sem pacote winget: a fonte oficial é o site do fabricante — abrimos no navegador.
        if (!app.UsesWinget)
        {
            if (TryOpenUrl(app.Url))
                return new InstallResult(InstallOutcome.OpenedOfficialPage, app.Url ?? "");
            return new InstallResult(InstallOutcome.Failed, Localization.Loc.S("Pf_NoOfficialUrl"));
        }

        if (!IsWingetAvailable)
            return new InstallResult(InstallOutcome.Failed, Localization.Loc.S("Pf_WingetUnavailable"));

        // -e: correspondência exata do ID. --silent: sem telas do instalador. Os --accept evitam
        // qualquer prompt interativo (que travaria a fila). --source: itens "9XXXX" vêm da Loja.
        bool isStoreId = !app.WingetId!.Contains('.');
        string source = isStoreId ? "msstore" : "winget";
        string args =
            $"install --id {app.WingetId} -e --source {source} " +
            "--accept-package-agreements --accept-source-agreements --silent --disable-interactivity";

        var result = await RunWingetAsync(args, onOutput);

        if (result.ExitCode == 0)
            return new InstallResult(InstallOutcome.Success, "");

        // winget devolve códigos específicos para "já instalado" / "sem atualização aplicável",
        // mas eles variam por versão; como a saída do winget é localizada (PT/EN conforme o
        // Windows), reconhecemos também pelas frases mais comuns. Tratamos como sucesso silencioso
        // para não assustar o usuário com "erro" quando o app já está presente e atualizado.
        const int APPINSTALLER_NO_APPLICABLE_UPGRADE = unchecked((int)0x8A15002B);
        const int APPINSTALLER_PACKAGE_ALREADY_INSTALLED = unchecked((int)0x8A150061);

        string output = result.Output;
        string[] alreadyPhrases =
        {
            "already installed", "no available upgrade", "no newer", "no applicable upgrade",
            "no installed package found matching", "já está instalad", "nenhuma atualização",
            "não há atualiz", "versão mais recente", "já está na versão", "nenhum upgrade",
        };
        bool alreadyByText = alreadyPhrases.Any(p => output.Contains(p, StringComparison.OrdinalIgnoreCase));

        if (result.ExitCode == APPINSTALLER_PACKAGE_ALREADY_INSTALLED ||
            result.ExitCode == APPINSTALLER_NO_APPLICABLE_UPGRADE ||
            alreadyByText)
            return new InstallResult(InstallOutcome.AlreadyInstalled, "");

        return new InstallResult(InstallOutcome.Failed, LastMeaningfulLine(output, result.ExitCode));
    }

    private static async Task<CommandResult> RunWingetAsync(string args, IProgress<string>? onOutput)
    {
        var psi = new ProcessStartInfo("winget", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var sb = new System.Text.StringBuilder();

        void Handle(string? data)
        {
            // winget usa muitas linhas de barra de progresso (só símbolos); filtramos para o log.
            if (string.IsNullOrWhiteSpace(data)) return;
            sb.AppendLine(data);
            string trimmed = data.Trim();
            if (trimmed.Length > 2 && !IsProgressNoise(trimmed))
                onOutput?.Report(trimmed);
        }

        proc.OutputDataReceived += (_, e) => Handle(e.Data);
        proc.ErrorDataReceived += (_, e) => Handle(e.Data);

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        await proc.WaitForExitAsync();

        return new CommandResult(proc.ExitCode, sb.ToString());
    }

    // Linhas de barra de progresso do winget são compostas só de blocos/traços e porcentagem.
    private static bool IsProgressNoise(string line) =>
        line.All(c => "█▓▒░-\\|/ %0123456789.KMGBkmgb".Contains(c));

    private static string LastMeaningfulLine(string output, int exitCode)
    {
        var lines = output.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 3 && !IsProgressNoise(l))
            .ToList();
        string tail = lines.Count > 0 ? lines[^1] : "";
        return tail.Length > 0 ? tail : Localization.Loc.F("Pf_WingetExitCode", exitCode);
    }

    public static bool TryOpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch { return false; }
    }

    /// <summary>Abre a Loja para instalar o App Installer (winget) quando ele não está presente.</summary>
    public static void OpenWingetInstallPage() =>
        TryOpenUrl("https://apps.microsoft.com/detail/9nblggh4nns1");

    // ===================== Detecção Inteligente =====================

    public Task<SystemProfile> DetectSystemAsync() => Task.Run(DetectSystem);

    private static SystemProfile DetectSystem()
    {
        var vendors = new HashSet<HwVendor>();

        string gpu = ReadGpuName();
        AddVendorFromText(vendors, gpu, ("nvidia", HwVendor.Nvidia), ("geforce", HwVendor.Nvidia),
            ("radeon", HwVendor.Amd), ("amd", HwVendor.Amd), ("intel", HwVendor.Intel));

        string cpu = ReadCpuName();
        AddVendorFromText(vendors, cpu, ("intel", HwVendor.Intel), ("amd", HwVendor.Amd), ("ryzen", HwVendor.Amd));

        // Áudio Realtek (muito comum em placas-mãe) → recomenda Realtek Audio Console.
        if (ReadSoundDevices().Contains("realtek", StringComparison.OrdinalIgnoreCase))
            vendors.Add(HwVendor.Realtek);

        string oem = ReadOemManufacturer();
        AddVendorFromText(vendors, oem,
            ("asus", HwVendor.Asus), ("micro-star", HwVendor.Msi), ("msi", HwVendor.Msi),
            ("gigabyte", HwVendor.Gigabyte), ("acer", HwVendor.Acer), ("lenovo", HwVendor.Lenovo),
            ("dell", HwVendor.Dell), ("hp", HwVendor.Hp), ("hewlett", HwVendor.Hp));

        string arch = Environment.Is64BitOperatingSystem ? "64 bits (x64)" : "32 bits (x86)";
        return new SystemProfile(
            string.IsNullOrWhiteSpace(gpu) ? "—" : gpu,
            string.IsNullOrWhiteSpace(cpu) ? "—" : cpu,
            ReadWindowsVersion(),
            arch,
            string.IsNullOrWhiteSpace(oem) ? "—" : oem,
            vendors);
    }

    private static void AddVendorFromText(HashSet<HwVendor> set, string text, params (string needle, HwVendor vendor)[] map)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        foreach (var (needle, vendor) in map)
            if (text.Contains(needle, StringComparison.OrdinalIgnoreCase))
                set.Add(vendor);
    }

    private static string ReadGpuName()
    {
        // Prefere a GPU dedicada (NVIDIA/AMD) quando há mais de uma controladora de vídeo.
        try
        {
            using var s = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
            string? best = null;
            foreach (ManagementObject o in s.Get())
            {
                string name = o["Name"]?.ToString()?.Trim() ?? "";
                if (name.Length == 0) continue;
                bool discrete = name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("GeForce", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase);
                if (best is null || discrete) best = name;
                if (discrete) break;
            }
            return best ?? "";
        }
        catch { return ""; }
    }

    private static string ReadCpuName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return (key?.GetValue("ProcessorNameString") as string)?.Trim() ?? "";
        }
        catch { return ""; }
    }

    private static string ReadSoundDevices()
    {
        try
        {
            using var s = new ManagementObjectSearcher("SELECT Name FROM Win32_SoundDevice");
            return string.Join(" | ", s.Get().Cast<ManagementObject>().Select(o => o["Name"]?.ToString() ?? ""));
        }
        catch { return ""; }
    }

    private static string ReadOemManufacturer()
    {
        try
        {
            using var s = new ManagementObjectSearcher("SELECT Manufacturer, Model FROM Win32_ComputerSystem");
            foreach (ManagementObject o in s.Get())
            {
                string m = o["Manufacturer"]?.ToString()?.Trim() ?? "";
                string model = o["Model"]?.ToString()?.Trim() ?? "";
                return string.IsNullOrEmpty(model) ? m : $"{m} {model}".Trim();
            }
        }
        catch { }
        return "";
    }

    private static string ReadWindowsVersion()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            string product = key?.GetValue("ProductName") as string ?? "Windows";
            string display = key?.GetValue("DisplayVersion") as string ?? "";
            // O ProductName ainda diz "Windows 10" no Win11; corrige pelo número de build.
            if (key?.GetValue("CurrentBuildNumber") is string bn && int.TryParse(bn, out int build) && build >= 22000)
                product = product.Replace("Windows 10", "Windows 11");
            return string.IsNullOrEmpty(display) ? product : $"{product} ({display})";
        }
        catch { return "Windows"; }
    }
}
