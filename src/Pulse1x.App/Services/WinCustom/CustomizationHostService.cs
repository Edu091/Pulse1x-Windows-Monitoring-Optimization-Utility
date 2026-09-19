using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace Pulse1x.App.Services.WinCustom;

/// <summary>
/// Faz a personalização sobreviver ao fechamento do Pulse, ao logoff e ao desligamento.
///
/// O requisito é que a personalização NÃO dependa do Pulse principal ficar aberto. Isso é
/// resolvido com um atalho de inicialização próprio, registrado em
/// HKCU\...\Run\Pulse1xCustomization, que abre o Pulse no modo host: uma execução com
/// <c>--wincustom-host</c>, que aplica o tema e permanece apenas como um processo leve, sem
/// abrir janela.
///
/// Por que HKCU e não um serviço do Windows: a personalização atua na sessão interativa do
/// usuário (as janelas do Shell só existem lá), então um serviço não teria acesso a elas. Além
/// disso, uma chave Run é removível pelo próprio usuário a qualquer momento — o que sustenta a
/// promessa de reversibilidade total, inclusive na desinstalação.
/// </summary>
public class CustomizationHostService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "Pulse1xCustomization";

    /// <summary>Argumento que coloca o Pulse em modo host (sem interface).</summary>
    public const string HostArgument = "--wincustom-host";

    /// <summary>Argumento que executa a restauração de emergência e sai.</summary>
    public const string RestoreArgument = "--wincustom-restore";

    // =====================================================================================
    //  Inicialização com o Windows
    // =====================================================================================

    /// <summary>Se o host está registrado para iniciar com o Windows.</summary>
    public bool IsRegistered
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
                return key?.GetValue(RunValueName) is not null;
            }
            catch { return false; }
        }
    }

    /// <summary>Registra (ou remove) o host na inicialização do Windows.</summary>
    public bool SetRegistered(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key is null) return false;

            if (enabled)
            {
                string exe = Environment.ProcessPath ?? Environment.GetCommandLineArgs()[0];
                key.SetValue(RunValueName, $"\"{exe}\" {HostArgument}");
            }
            else if (key.GetValue(RunValueName) is not null)
            {
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Se existe um processo host rodando agora (além deste).</summary>
    public bool IsHostRunning()
    {
        try
        {
            int self = Environment.ProcessId;
            foreach (var p in Process.GetProcessesByName("Pulse1x.App"))
            {
                using (p)
                {
                    if (p.Id == self) continue;
                    return true;
                }
            }
        }
        catch { }
        return false;
    }

    // =====================================================================================
    //  Restauração de emergência
    // =====================================================================================

    /// <summary>Onde o script de recuperação é gravado.</summary>
    public static string EmergencyScriptPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Pulse1x", "Restaurar-Windows.cmd");

    /// <summary>
    /// Cria um script de recuperação que funciona MESMO QUE o Pulse não abra.
    ///
    /// É o requisito de restauração independente. O script não depende do app: ele remove a
    /// chave de inicialização, encerra qualquer host em execução e reinicia o Explorer — e
    /// reiniciar o Explorer é o que efetivamente apaga toda a personalização sem injeção, já que
    /// ela vive só na memória das janelas. Por isso o Windows volta ao padrão mesmo com um tema
    /// quebrado ou com o subsistema falhando.
    /// </summary>
    public void WriteEmergencyScript()
    {
        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine("chcp 65001 > nul");
        sb.AppendLine("title Pulse1x - Restaurar personalizacao do Windows");
        sb.AppendLine("echo.");
        sb.AppendLine("echo  Restaurando o visual padrao do Windows...");
        sb.AppendLine("echo.");
        sb.AppendLine();
        sb.AppendLine("rem 1) Impede que a personalizacao volte no proximo logon.");
        sb.AppendLine($"reg delete \"HKCU\\{RunKeyPath}\" /v {RunValueName} /f > nul 2>&1");
        sb.AppendLine();
        sb.AppendLine("rem 2) Encerra o Pulse1x (inclusive o host de personalizacao).");
        sb.AppendLine("taskkill /f /im Pulse1x.App.exe > nul 2>&1");
        sb.AppendLine();
        sb.AppendLine("rem 3) Reinicia o Explorer. Os efeitos aplicados pelo Pulse vivem apenas na");
        sb.AppendLine("rem    memoria das janelas, entao recria-las devolve o visual padrao.");
        sb.AppendLine("taskkill /f /im explorer.exe > nul 2>&1");
        sb.AppendLine("timeout /t 2 /nobreak > nul");
        sb.AppendLine("start explorer.exe");
        sb.AppendLine();
        sb.AppendLine("echo  Pronto. O Windows voltou ao visual padrao.");
        sb.AppendLine("echo.");
        sb.AppendLine("pause");

        try
        {
            string path = EmergencyScriptPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // O .cmd roda no console, que não lê UTF-8 por padrão — gravamos sem BOM e o
            // próprio script troca a página de código.
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch
        {
            // Sem permissão de escrita: o botão "Restaurar tudo" dentro do app continua valendo.
        }
    }

    /// <summary>Abre a pasta do script de recuperação, para o usuário guardá-lo ou executá-lo.</summary>
    public void RevealEmergencyScript()
    {
        try
        {
            WriteEmergencyScript();
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{EmergencyScriptPath}\"")
            {
                UseShellExecute = true,
            });
        }
        catch { }
    }

    // =====================================================================================
    //  Ocultação automática da barra de tarefas
    // =====================================================================================

    private const string StuckRectsKey =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StuckRects3";

    /// <summary>
    /// Posição, dentro do blob StuckRects3, do byte de flags da barra de tarefas. O bit 0 é a
    /// ocultação automática. É um formato não documentado, mas estável desde o Windows 7.
    /// </summary>
    private const int AutoHideFlagIndex = 8;

    /// <summary>
    /// Se a ocultação automática está ligada. Importa para a personalização porque, com ela
    /// ativa, o Windows mantém a barra fora da tela: o efeito É aplicado, mas fica invisível — e
    /// de fora parece que a personalização não funcionou.
    /// </summary>
    public static bool IsTaskbarAutoHideEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StuckRectsKey);
            if (key?.GetValue("Settings") is not byte[] blob || blob.Length <= AutoHideFlagIndex)
                return false;
            return (blob[AutoHideFlagIndex] & 1) != 0;
        }
        catch { return false; }
    }

    /// <summary>
    /// Liga ou desliga a ocultação automática. Mexe APENAS no bit 0 do blob, preservando todo o
    /// resto (posição, tamanho, monitor) — reescrever o blob inteiro bagunçaria a barra.
    ///
    /// É reversível como qualquer outra preferência do Windows: o mesmo botão desfaz, e o valor
    /// é o mesmo que a tela de Configurações do Windows grava.
    /// </summary>
    public static bool SetTaskbarAutoHide(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StuckRectsKey, writable: true);
            if (key?.GetValue("Settings") is not byte[] blob || blob.Length <= AutoHideFlagIndex)
                return false;

            byte current = blob[AutoHideFlagIndex];
            byte updated = enabled ? (byte)(current | 1) : (byte)(current & ~1);
            if (current == updated) return true;   // já está como pedido

            blob[AutoHideFlagIndex] = updated;
            key.SetValue("Settings", blob, RegistryValueKind.Binary);

            // O Explorer só relê o blob ao ser reiniciado; sem isso a mudança não aparece.
            RestartExplorer();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Reinicia o Explorer. É a forma mais confiável de devolver o Shell ao padrão e também o
    /// que o usuário espera de "Restaurar Explorer" quando algo ficou estranho.
    /// </summary>
    public static void RestartExplorer()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("explorer"))
            {
                using (p)
                {
                    try { p.Kill(); } catch { }
                }
            }
            // O Windows normalmente reinicia o Shell sozinho; garantimos o retorno caso não o faça.
            System.Threading.Thread.Sleep(1500);
            if (Process.GetProcessesByName("explorer").Length == 0)
                Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
        }
        catch { }
    }
}
