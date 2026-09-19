using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace Pulse1x.App.Services;

/// <summary>Como uma desinstalação terminou.</summary>
public enum UninstallOutcome
{
    /// <summary>O desinstalador rodou sozinho e terminou com sucesso.</summary>
    SilentSuccess,
    /// <summary>Não havia modo silencioso confiável: o desinstalador oficial foi aberto para o usuário.</summary>
    OpenedInteractive,
    /// <summary>O desinstalador rodou mas devolveu erro.</summary>
    Failed,
}

/// <summary>Resultado de uma desinstalação, com o que sobrou para limpar depois.</summary>
public record UninstallResult(UninstallOutcome Outcome, string? Detail = null);

/// <summary>
/// Desinstalação silenciosa de programas Win32.
///
/// A regra de ouro aqui é não inventar: o Pulse1x nunca apaga os arquivos de um programa por conta
/// própria para "desinstalá-lo". Ele usa o desinstalador que o próprio programa registrou no
/// Windows, apenas acrescentando o argumento de modo silencioso que aquele tipo de desinstalador
/// reconhece — MSI, NSIS, Inno Setup e InstallShield têm cada um o seu, e são os quatro que cobrem
/// praticamente todo software de fabricante.
///
/// Quando o formato do desinstalador não é reconhecido, nada é adivinhado: o desinstalador oficial
/// é aberto normalmente e quem conduz a remoção é o usuário. Melhor um clique a mais do que um
/// argumento errado passado a um instalador desconhecido.
/// </summary>
public class SilentUninstallService
{
    /// <summary>
    /// Desinstala usando o <c>UninstallString</c> registrado pelo programa. Tenta o modo silencioso
    /// quando reconhece o tipo de desinstalador; caso contrário abre o oficial.
    /// </summary>
    public async Task<UninstallResult> UninstallAsync(string uninstallString, string? quietUninstallString = null, int timeoutMs = 300000)
    {
        // Alguns programas registram o comando silencioso oficial — quando existe, ele tem
        // preferência sobre qualquer suposição nossa.
        if (!string.IsNullOrWhiteSpace(quietUninstallString))
        {
            var (file, args) = SplitCommand(quietUninstallString!);
            return await RunUninstallerAsync(file, args, timeoutMs);
        }

        if (string.IsNullOrWhiteSpace(uninstallString))
            return new UninstallResult(UninstallOutcome.Failed, "Nenhum desinstalador registrado.");

        var (exe, rawArgs) = SplitCommand(uninstallString);
        string? silentArgs = SilentArgumentsFor(exe, rawArgs);

        if (silentArgs is null)
        {
            OpenInteractive(exe, rawArgs);
            return new UninstallResult(UninstallOutcome.OpenedInteractive);
        }

        return await RunUninstallerAsync(exe, silentArgs, timeoutMs);
    }

    /// <summary>
    /// Descobre o argumento de modo silencioso para este desinstalador, ou null quando o formato
    /// não é reconhecido (e portanto não se deve arriscar um palpite).
    /// </summary>
    private static string? SilentArgumentsFor(string exe, string args)
    {
        string fileName = Path.GetFileName(exe).ToLowerInvariant();

        // MSI: msiexec /x {GUID}. Trocamos qualquer /i por /x e acrescentamos /qn (sem interface)
        // e /norestart, para que a remoção não reinicie a máquina sozinha.
        if (fileName is "msiexec" or "msiexec.exe")
        {
            string product = ExtractMsiProductCode(args);
            if (product.Length == 0) return null;
            return $"/x {product} /qn /norestart";
        }

        string lowerArgs = args.ToLowerInvariant();

        // Inno Setup: o desinstalador se chama unins000.exe e aceita /VERYSILENT.
        if (fileName.StartsWith("unins", StringComparison.Ordinal))
            return $"{args} /VERYSILENT /SUPPRESSMSGBOXES /NORESTART".Trim();

        // InstallShield: reconhecido pelo -runfromtemp/-removeonly e pelo arquivo de resposta.
        if (lowerArgs.Contains("-runfromtemp") || lowerArgs.Contains("-removeonly") || lowerArgs.Contains(".iss"))
            return $"{args} -silent".Trim();

        // NSIS: uninstall.exe / uninst.exe aceitam /S (maiúsculo, obrigatoriamente).
        if (fileName.Contains("uninst"))
            return $"{args} /S".Trim();

        return null;
    }

    // O código do produto é o GUID entre chaves na linha do msiexec.
    private static string ExtractMsiProductCode(string args)
    {
        var match = Regex.Match(args, @"\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}");
        return match.Success ? match.Value : "";
    }

    private static async Task<UninstallResult> RunUninstallerAsync(string exe, string args, int timeoutMs)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process is null) return new UninstallResult(UninstallOutcome.Failed, "Não foi possível iniciar o desinstalador.");

            using var cts = new CancellationTokenSource(timeoutMs);
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                return new UninstallResult(UninstallOutcome.Failed, "O desinstalador demorou demais e foi deixado rodando.");
            }

            // 0 = sucesso; 3010 = sucesso, mas pede reinício (comum em software de fabricante);
            // 1605 do MSI = o produto já não está instalado, o que para nós também é sucesso.
            return process.ExitCode is 0 or 3010 or 1605
                ? new UninstallResult(UninstallOutcome.SilentSuccess)
                : new UninstallResult(UninstallOutcome.Failed, $"O desinstalador retornou o código {process.ExitCode}.");
        }
        catch (Exception ex)
        {
            return new UninstallResult(UninstallOutcome.Failed, ex.Message);
        }
    }

    private static void OpenInteractive(string exe, string args)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = exe, Arguments = args, UseShellExecute = true });
        }
        catch { /* desinstalador indisponível: o item simplesmente continua na lista */ }
    }

    /// <summary>
    /// Separa o executável dos argumentos numa linha de comando do Registro. O caminho pode vir
    /// entre aspas (com espaços) ou solto, e nesse caso termina no primeiro ".exe".
    /// </summary>
    internal static (string file, string args) SplitCommand(string command)
    {
        command = command.Trim();

        if (command.StartsWith('"'))
        {
            int end = command.IndexOf('"', 1);
            if (end > 0)
                return (command[1..end], command[(end + 1)..].Trim());
        }

        int exe = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exe > 0)
            return (command[..(exe + 4)], command[(exe + 4)..].Trim());

        int space = command.IndexOf(' ');
        return space > 0 ? (command[..space], command[(space + 1)..].Trim()) : (command, "");
    }
}
