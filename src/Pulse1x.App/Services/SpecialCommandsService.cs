using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace Pulse1x.App.Services;

public enum CommandStatus { None, Success, Warning, Error }

public enum ActivePowerPlan { Other, Balanced, HighPerformance, Ultimate }

public record CommandResult(int ExitCode, string Output);

/// <summary>
/// Central de comandos/utilitários do Windows. Executa comandos de manutenção e reparo
/// (SFC, DISM, chkdsk, defrag), troca planos de energia, cria pontos de restauração,
/// abre ferramentas nativas (.msc/exe) e reúne informações de rede/sistema.
///
/// Nenhuma ação é disparada automaticamente — quem decide é sempre o usuário (a confirmação
/// fica na camada de ViewModel). O app já roda como administrador, o que estes comandos exigem.
/// </summary>
public class SpecialCommandsService
{
    // Abre uma ferramenta nativa do Windows (associação de arquivo / ShellExecute).
    // Retorna o processo (quando disponível) para que o chamador saiba quando ela é fechada.
    public Process? OpenTool(string fileName, string arguments = "")
    {
        return Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = true
        });
    }

    // Executa um processo de console capturando a saída (stdout+stderr) linha a linha.
    public async Task<CommandResult> RunProcessAsync(string exe, string args, IProgress<string>? onOutput = null)
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

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var sb = new StringBuilder();

        void Handle(string? data)
        {
            if (data is null) return;
            sb.AppendLine(data);
            onOutput?.Report(data);
        }

        proc.OutputDataReceived += (_, e) => Handle(e.Data);
        proc.ErrorDataReceived += (_, e) => Handle(e.Data);

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        await proc.WaitForExitAsync();

        return new CommandResult(proc.ExitCode, sb.ToString());
    }

    // Roda vários comandos em sequência, juntando a saída. Para na primeira falha grave (exceção).
    public async Task<CommandResult> RunSequenceAsync(IEnumerable<(string exe, string args)> commands, IProgress<string>? onOutput = null)
    {
        var sb = new StringBuilder();
        int lastExit = 0;
        foreach (var (exe, args) in commands)
        {
            onOutput?.Report($"> {exe} {args}");
            var r = await RunProcessAsync(exe, args, onOutput);
            sb.AppendLine(r.Output);
            lastExit = r.ExitCode;
        }
        return new CommandResult(lastExit, sb.ToString());
    }

    // ===================== Planos de energia =====================

    private const string BalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";
    private const string HighPerfGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    private const string UltimateTemplateGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";

    public Task<CommandResult> SetBalancedPlanAsync() => RunProcessAsync("powercfg", $"/setactive {BalancedGuid}");

    public async Task<CommandResult> SetHighPerformancePlanAsync()
    {
        // Tenta o GUID padrão; se o plano não existir nesta máquina, procura por nome ou o recria.
        var result = await RunProcessAsync("powercfg", $"/setactive {HighPerfGuid}");
        if (result.ExitCode == 0) return result;

        var list = await RunProcessAsync("powercfg", "/list");
        string? guid = FindPlanGuidByName(list.Output, "Alto Desempenho", "High performance");

        if (guid is null)
        {
            var dup = await RunProcessAsync("powercfg", $"/duplicatescheme {HighPerfGuid}");
            guid = ExtractGuid(dup.Output);
        }

        return guid is null
            ? new CommandResult(1, "Não foi possível ativar o plano Alto Desempenho.")
            : await RunProcessAsync("powercfg", $"/setactive {guid}");
    }

    // Identifica qual dos três planos conhecidos está ativo agora, consultando o Windows
    // diretamente (em vez de confiar em um estado guardado pelo app) — assim o interruptor
    // na tela sempre reflete a realidade, mesmo se o plano foi trocado fora do Pulse1x.
    public async Task<ActivePowerPlan> GetActivePowerPlanAsync()
    {
        var result = await RunProcessAsync("powercfg", "/getactivescheme");
        string output = result.Output;

        if (output.Contains(BalancedGuid, StringComparison.OrdinalIgnoreCase))
            return ActivePowerPlan.Balanced;

        if (output.Contains(HighPerfGuid, StringComparison.OrdinalIgnoreCase))
            return ActivePowerPlan.HighPerformance;

        // O GUID do Ultimate Performance é gerado dinamicamente quando o plano é duplicado,
        // então comparamos pelo nome (varia por idioma) em vez do GUID do template.
        string[] ultimateNames = { "Ultimate", "Máximo", "Maximo", "Maximum" };
        if (ultimateNames.Any(n => output.Contains(n, StringComparison.OrdinalIgnoreCase)))
            return ActivePowerPlan.Ultimate;

        return ActivePowerPlan.Other;
    }

    public async Task<CommandResult> SetUltimatePlanAsync()
    {
        // Procura um plano Ultimate já existente (nome varia por idioma: "Ultimate Performance",
        // "Desempenho Máximo", "Maximum Performance"). Se não houver, cria a partir do template.
        var list = await RunProcessAsync("powercfg", "/list");
        string? guid = FindPlanGuidByName(list.Output, "Ultimate", "Máximo", "Maximo", "Maximum");

        if (guid is null)
        {
            var dup = await RunProcessAsync("powercfg", $"/duplicatescheme {UltimateTemplateGuid}");
            guid = ExtractGuid(dup.Output);
        }

        if (guid is null)
            return new CommandResult(1, "Não foi possível criar o plano Ultimate Performance.");

        return await RunProcessAsync("powercfg", $"/setactive {guid}");
    }

    private static string? FindPlanGuidByName(string powercfgOutput, params string[] nameFragments)
    {
        foreach (var line in powercfgOutput.Split('\n'))
        {
            if (nameFragments.Any(f => line.Contains(f, StringComparison.OrdinalIgnoreCase)))
            {
                var g = ExtractGuid(line);
                if (g is not null) return g;
            }
        }
        return null;
    }

    private static string? ExtractGuid(string text)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            text, @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
        return match.Success ? match.Value : null;
    }

    // ===================== Ponto de restauração =====================

    public Task<CommandResult> CreateRestorePointAsync(string description)
    {
        string safe = description.Replace("'", " ").Replace("\"", " ");
        // Habilita a proteção do sistema, libera o limite de frequência e cria o ponto.
        string ps =
            "$ErrorActionPreference='Stop'; " +
            "try { Enable-ComputerRestore -Drive 'C:\\' } catch {}; " +
            "New-ItemProperty -Path 'HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\SystemRestore' " +
            "-Name 'SystemRestorePointCreationFrequency' -Value 0 -PropertyType DWord -Force | Out-Null; " +
            $"Checkpoint-Computer -Description '{safe}' -RestorePointType 'MODIFY_SETTINGS'";

        return RunProcessAsync("powershell", $"-NoProfile -ExecutionPolicy Bypass -Command \"{ps}\"");
    }

    // ===================== God Mode =====================

    public string CreateGodMode()
    {
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string path = Path.Combine(desktop, "Painel de Controle Total.{ED7BA470-8E54-465E-825C-99712043E01C}");
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);
        return path;
    }

    // ===================== Rede =====================

    public string GetNetworkInfo()
    {
        var sb = new StringBuilder();

        var nics = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                        && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                        && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel);

        foreach (var nic in nics)
        {
            var props = nic.GetIPProperties();
            var gateway = props.GatewayAddresses.FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork);
            if (gateway is null) continue; // só interfaces com gateway (conexão real)

            var ipv4 = props.UnicastAddresses.FirstOrDefault(u => u.Address.AddressFamily == AddressFamily.InterNetwork);
            var dns = props.DnsAddresses.Where(d => d.AddressFamily == AddressFamily.InterNetwork).Select(d => d.ToString());

            sb.AppendLine($"Adaptador em uso: {nic.Name}");
            sb.AppendLine($"Descrição: {nic.Description}");
            sb.AppendLine($"Tipo: {nic.NetworkInterfaceType}");
            sb.AppendLine($"Endereço IP: {ipv4?.Address}");
            sb.AppendLine($"Máscara de sub-rede: {ipv4?.IPv4Mask}");
            sb.AppendLine($"Gateway: {gateway.Address}");
            sb.AppendLine($"DNS: {string.Join(", ", dns)}");
            sb.AppendLine($"Velocidade da conexão: {nic.Speed / 1_000_000} Mbps");
            sb.AppendLine($"Endereço físico (MAC): {FormatMac(nic.GetPhysicalAddress().ToString())}");
            sb.AppendLine();
        }

        string result = sb.ToString().TrimEnd();
        return result.Length > 0 ? result : "Nenhuma conexão de rede ativa foi encontrada.";
    }

    private static string FormatMac(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "--";
        return string.Join(":", Enumerable.Range(0, raw.Length / 2).Select(i => raw.Substring(i * 2, 2)));
    }

    public Task<CommandResult> RenewNetworkAsync(IProgress<string>? onOutput = null) =>
        RunSequenceAsync(new[]
        {
            ("ipconfig", "/flushdns"),
            ("ipconfig", "/renew"),
        }, onOutput);

    public Task<CommandResult> ResetNetworkAsync(IProgress<string>? onOutput = null) =>
        RunSequenceAsync(new[]
        {
            ("netsh", "winsock reset"),
            ("netsh", "int ip reset"),
            ("ipconfig", "/flushdns"),
        }, onOutput);

    // ===================== Relatório do sistema =====================

    public string BuildReportText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== RELATÓRIO DO SISTEMA — Pulse1x ===");
        sb.AppendLine($"Gerado em: {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
        sb.AppendLine();

        sb.AppendLine("[ SISTEMA ]");
        sb.AppendLine($"Computador: {Environment.MachineName}");
        sb.AppendLine($"Usuário: {Environment.UserName}");
        sb.AppendLine($"Sistema operacional: {GetOsName()}");
        sb.AppendLine($"Versão: {Environment.OSVersion.Version}");
        sb.AppendLine($"Arquitetura: {(Environment.Is64BitOperatingSystem ? "64 bits" : "32 bits")}");
        sb.AppendLine($"Processador: {GetCpuName()}");
        sb.AppendLine($"Memória RAM total: {GetTotalRamGb():0.0} GB");
        sb.AppendLine($"Tempo ligado: {FormatUptime()}");
        sb.AppendLine();

        sb.AppendLine("[ ARMAZENAMENTO ]");
        foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
        {
            double totalGb = d.TotalSize / 1073741824d;
            double freeGb = d.TotalFreeSpace / 1073741824d;
            sb.AppendLine($"{d.Name}  {freeGb:0.0} GB livres de {totalGb:0.0} GB  ({d.DriveFormat})");
        }
        sb.AppendLine();

        sb.AppendLine("[ REDE ]");
        sb.AppendLine(GetNetworkInfo());

        return sb.ToString();
    }

    public string BuildReportHtml()
    {
        string body = System.Net.WebUtility.HtmlEncode(BuildReportText());
        return "<!DOCTYPE html><html><head><meta charset='utf-8'><title>Relatório do Sistema — Pulse1x</title>" +
               "<style>body{font-family:Segoe UI,Arial,sans-serif;background:#f5f5f5;color:#222;padding:24px;}" +
               "pre{background:#fff;border:1px solid #ddd;border-radius:8px;padding:20px;white-space:pre-wrap;" +
               "font-size:13px;line-height:1.5;}h1{font-size:20px;}</style></head><body>" +
               "<h1>Relatório do Sistema — Pulse1x</h1><pre>" + body + "</pre></body></html>";
    }

    private static string GetOsName()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            return key?.GetValue("ProductName") as string ?? "Windows";
        }
        catch { return "Windows"; }
    }

    private static string GetCpuName()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return (key?.GetValue("ProcessorNameString") as string)?.Trim() ?? "Desconhecido";
        }
        catch { return "Desconhecido"; }
    }

    private static double GetTotalRamGb()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem");
            foreach (ManagementObject mo in searcher.Get())
            {
                if (mo["TotalVisibleMemorySize"] is not null)
                    return Convert.ToDouble(mo["TotalVisibleMemorySize"]) / 1048576d; // KB -> GB
            }
        }
        catch { }
        return 0;
    }

    private static string FormatUptime()
    {
        var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
        return $"{(int)up.TotalHours}h {up.Minutes}min";
    }
}
