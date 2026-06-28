using System.Diagnostics;
using System.IO;

namespace Pulse1x.App.Services;

/// <summary>
/// Verifica se existe uma versao mais nova publicada na pasta de staging local
/// (C:\Pulse1x\staging, gerada por atualizar_pulse1x.bat) e, se houver, substitui
/// o executavel em uso e reinicia o app automaticamente.
///
/// A pasta de staging e tratada como uma "caixa de entrada" de uso unico: depois que a
/// atualizacao e aplicada, os arquivos de lá sao apagados. Assim ela so contem algo quando
/// ha uma atualizacao pendente, evitando que builds antigos fiquem acumulados e confundindo
/// qual e a versao realmente instalada (a unica copia que importa e a do atalho/instalacao).
/// </summary>
public static class SelfUpdateService
{
    private const string StagingExePath = @"C:\Pulse1x\staging\Pulse1x.App.exe";
    private const string StagingPdbPath = @"C:\Pulse1x\staging\Pulse1x.App.pdb";

    /// <summary>
    /// Retorna true se uma atualizacao foi disparada (o processo atual deve encerrar
    /// imediatamente, pois um novo processo ja foi iniciado em seu lugar).
    /// </summary>
    public static bool TryApplyPendingUpdate()
    {
        try
        {
            if (!File.Exists(StagingExePath))
                return false;

            var currentExe = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(currentExe) || !File.Exists(currentExe))
                return false;

            // Nunca atualiza a partir de si mesmo (evita loop caso o app já esteja rodando direto do staging).
            if (string.Equals(Path.GetFullPath(currentExe), Path.GetFullPath(StagingExePath), StringComparison.OrdinalIgnoreCase))
                return false;

            var currentTime = File.GetLastWriteTimeUtc(currentExe);
            var stagingTime = File.GetLastWriteTimeUtc(StagingExePath);
            if (stagingTime <= currentTime)
                return false;

            int pid = Environment.ProcessId;

            // Script auxiliar: espera o processo atual encerrar, copia a nova versao por
            // cima da instalacao em uso, limpa a pasta de staging (uso unico) e reabre o
            // app no mesmo lugar.
            string script =
                $"$ErrorActionPreference = 'SilentlyContinue';" +
                $"while (Get-Process -Id {pid} -ErrorAction SilentlyContinue) {{ Start-Sleep -Milliseconds 300 }};" +
                $"Start-Sleep -Milliseconds 300;" +
                $"Copy-Item -Path '{StagingExePath}' -Destination '{currentExe}' -Force;" +
                $"Remove-Item -Path '{StagingExePath}' -Force;" +
                $"Remove-Item -Path '{StagingPdbPath}' -Force;" +
                $"Start-Process -FilePath '{currentExe}';";

            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -Command \"{script}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });

            return true;
        }
        catch
        {
            // Se a verificacao falhar por qualquer motivo, segue com a versao atual em uso.
            return false;
        }
    }
}
