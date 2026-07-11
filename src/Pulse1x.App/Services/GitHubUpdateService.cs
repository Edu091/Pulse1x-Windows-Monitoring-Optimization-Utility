using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace Pulse1x.App.Services;

public enum UpdateCheckStatus { UpToDate, UpdateAvailable, Failed }

/// <summary>Resultado de uma checagem de atualização contra a Release mais recente do GitHub.</summary>
public record UpdateCheckResult(UpdateCheckStatus Status, string? LatestVersion, string? DownloadUrl, string? AssetName);

/// <summary>Resultado de uma tentativa de baixar e iniciar o instalador. <see cref="Started"/> só
/// indica que o processo foi lançado com sucesso — não que a instalação terminou (o próprio
/// Pulse1x é fechado pelo instalador antes disso, então não há como esperar o resultado final).</summary>
public record UpdateInstallResult(bool Started, string? ErrorMessage);

/// <summary>
/// Verifica e instala atualizações a partir das Releases do repositório público do Pulse1x
/// (github.com/Edu091/Pulse1x-Windows-Monitoring-Optimization-Utility). Cada Release publica o
/// instalador Inno Setup (Pulse1x-Setup-X.Y.Z.exe, gerado pelo workflow de CI a partir de uma tag);
/// baixamos esse instalador e o executamos silenciosamente com /CLOSEAPPLICATIONS
/// /RESTARTAPPLICATIONS — o próprio Inno Setup fecha o Pulse1x em execução, substitui os arquivos
/// em Program Files e o reabre, sem precisarmos reimplementar essa troca manualmente (diferente do
/// <see cref="SelfUpdateService"/>, que só troca um .exe solto numa pasta de staging local).
///
/// Todas as falhas (sem internet, GitHub fora do ar, rate limit da API não-autenticada) são
/// silenciosas: o app continua funcionando normalmente com a versão atual.
/// </summary>
public class GitHubUpdateService
{
    private const string Owner = "Edu091";
    private const string Repo = "Pulse1x-Windows-Monitoring-Optimization-Utility";
    private static readonly HttpClient Http = BuildHttpClient();

    private static HttpClient BuildHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        // A API do GitHub exige um User-Agent; sem ele a requisição é rejeitada com 403.
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Pulse1x", CurrentVersion.ToString()));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    /// <summary>Versão em execução, definida por &lt;Version&gt; no csproj (a Release do CI sobrescreve
    /// no publish com -p:Version=X.Y.Z, mesmo número da tag do GitHub).</summary>
    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

    /// <summary>Consulta a última Release publicada e compara com a versão em execução.</summary>
    public async Task<UpdateCheckResult> CheckAsync()
    {
        try
        {
            using var resp = await Http.GetAsync($"https://api.github.com/repos/{Owner}/{Repo}/releases/latest");
            if (!resp.IsSuccessStatusCode)
                return new UpdateCheckResult(UpdateCheckStatus.Failed, null, null, null);

            using var stream = await resp.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            var root = doc.RootElement;

            string tag = root.GetProperty("tag_name").GetString() ?? "";
            // Tags podem vir com ou sem o prefixo "v" (a Release atual usa "1.0.0" sem prefixo).
            string versionText = tag.TrimStart('v', 'V');
            if (!Version.TryParse(versionText, out var remote))
                return new UpdateCheckResult(UpdateCheckStatus.Failed, null, null, null);

            if (remote <= CurrentVersion)
                return new UpdateCheckResult(UpdateCheckStatus.UpToDate, versionText, null, null);

            // Procura o instalador entre os assets da release (único .exe publicado).
            string? url = null, name = null;
            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                string assetName = asset.GetProperty("name").GetString() ?? "";
                if (assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    url = asset.GetProperty("browser_download_url").GetString();
                    name = assetName;
                    break;
                }
            }

            return url is null
                ? new UpdateCheckResult(UpdateCheckStatus.Failed, versionText, null, null)
                : new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable, versionText, url, name);
        }
        catch
        {
            return new UpdateCheckResult(UpdateCheckStatus.Failed, null, null, null);
        }
    }

    /// <summary>Baixa o instalador e o executa silenciosamente. O Inno Setup fecha o Pulse1x em
    /// execução, substitui os arquivos e o reabre automaticamente — este método retorna assim que
    /// o instalador é iniciado (ou falha em iniciar), antes da instalação em si terminar. Nunca
    /// falha em silêncio: qualquer erro é registrado em <c>update.log</c> e devolvido em
    /// <see cref="UpdateInstallResult.ErrorMessage"/> para a UI poder mostrar ao usuário.</summary>
    public async Task<UpdateInstallResult> DownloadAndInstallAsync(string downloadUrl, string assetName, IProgress<double>? progress = null)
    {
        string filePath;
        try
        {
            string dir = Path.Combine(Path.GetTempPath(), "Pulse1x-Update");
            Directory.CreateDirectory(dir);
            filePath = Path.Combine(dir, assetName);

            using var resp = await Http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();
            long? total = resp.Content.Headers.ContentLength;
            await using (var httpStream = await resp.Content.ReadAsStreamAsync())
            await using (var fileStream = File.Create(filePath))
            {
                var buffer = new byte[81920];
                long readTotal = 0;
                int read;
                while ((read = await httpStream.ReadAsync(buffer)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read));
                    readTotal += read;
                    if (total is > 0) progress?.Report((double)readTotal / total.Value);
                }
            }
        }
        catch (Exception ex)
        {
            LogFailure("download", ex);
            return new UpdateInstallResult(false, ex.Message);
        }

        // Um instalador recém-baixado, ainda sem reputação (não assinado digitalmente), costuma
        // ser retido por alguns segundos pelo antivírus/SmartScreen na primeira tentativa de
        // execução ("arquivo em uso"/"não foi possível executar") — geralmente resolve sozinho
        // em poucos segundos. Tenta algumas vezes com espera curta antes de desistir.
        const int maxAttempts = 3;
        Exception? lastError = null;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                // O instalador roda elevado (PrivilegesRequired=admin no .iss) e pode disparar um
                // UAC próprio mesmo com o Pulse1x já elevado — ShellExecute não herda elevação
                // entre processos. /CLOSEAPPLICATIONS fecha o Pulse1x que está usando os arquivos
                // a substituir; /RESTARTAPPLICATIONS o reabre ao final.
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = filePath,
                    Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS",
                    UseShellExecute = true,
                });

                if (process is null)
                {
                    lastError = new Exception("Process.Start retornou null.");
                    continue;
                }

                // Uma instalação bem-sucedida vai fechar este mesmo processo Pulse1x pouco depois
                // (CLOSEAPPLICATIONS), então não dá para esperar o instalador terminar de verdade.
                // Só esperamos uma janela curta para pegar falhas IMEDIATAS (ex.: UAC cancelado,
                // antivírus bloqueando a execução, argumento inválido).
                var waitTask = process.WaitForExitAsync();
                var completed = await Task.WhenAny(waitTask, Task.Delay(3000));
                if (completed == waitTask && process.ExitCode != 0)
                {
                    lastError = new Exception($"O instalador encerrou logo no início com código {process.ExitCode}.");
                    if (attempt < maxAttempts) await Task.Delay(2000);
                    continue;
                }

                return new UpdateInstallResult(true, null);
            }
            catch (Exception ex)
            {
                lastError = ex;
                if (attempt < maxAttempts) await Task.Delay(2000);
            }
        }

        LogFailure("install", lastError ?? new Exception("Falha desconhecida."));
        return new UpdateInstallResult(false, lastError?.Message);
    }

    // Registra falhas em %LOCALAPPDATA%\Pulse1x\update.log — mesmo padrão do crash.log do
    // App.xaml.cs, para dar um lugar único a checar quando uma atualização não aplica.
    private static void LogFailure(string stage, Exception ex)
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pulse1x");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "update.log"),
                $"==== {DateTime.Now:yyyy-MM-dd HH:mm:ss} | {stage} ===={Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { /* logger nunca pode quebrar o fluxo de atualização */ }
    }
}
