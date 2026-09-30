using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using Pulse1x.App.Localization;

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

            // Procura o instalador entre os assets da release. A release também pode trazer o
            // Pulse1x.App.exe solto (versão portátil); executá-lo com os argumentos silenciosos do
            // Inno Setup só abriria uma segunda cópia do app sem atualizar nada. Por isso o
            // "Pulse1x-Setup-*.exe" tem prioridade e qualquer outro .exe é apenas o último recurso.
            string? url = null, name = null;
            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                string assetName = asset.GetProperty("name").GetString() ?? "";
                if (!assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;

                bool isInstaller = assetName.Contains("Setup", StringComparison.OrdinalIgnoreCase);
                if (url is null || isInstaller)
                {
                    url = asset.GetProperty("browser_download_url").GetString();
                    name = assetName;
                }
                if (isInstaller) break;
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

    // Um download por vez: o aviso da abertura e o botão das Configurações gravavam no mesmo
    // arquivo temporário ao mesmo tempo (violação de compartilhamento ou dois instaladores).
    private static readonly SemaphoreSlim InstallGate = new(1, 1);

    /// <summary>Baixa o instalador e agenda sua execução silenciosa para logo depois que o Pulse1x
    /// fechar. Quando <see cref="UpdateInstallResult.Started"/> é true, o CHAMADOR deve encerrar o
    /// app (<see cref="Application.Shutdown()"/>) — o instalador substitui o .exe e o reabre. Nunca
    /// falha em silêncio: qualquer erro é registrado em <c>update.log</c> e devolvido em
    /// <see cref="UpdateInstallResult.ErrorMessage"/> para a UI poder mostrar ao usuário.</summary>
    public async Task<UpdateInstallResult> DownloadAndInstallAsync(string downloadUrl, string assetName, IProgress<double>? progress = null)
    {
        if (!await InstallGate.WaitAsync(0))
            return new UpdateInstallResult(false, Loc.S("Update_AlreadyRunning"));
        try
        {
            return await DownloadAndLaunchAsync(downloadUrl, assetName, progress);
        }
        finally
        {
            InstallGate.Release();
        }
    }

    private async Task<UpdateInstallResult> DownloadAndLaunchAsync(string downloadUrl, string assetName, IProgress<double>? progress)
    {
        string filePath;
        try
        {
            string dir = Path.Combine(Path.GetTempPath(), "Pulse1x-Update");
            Directory.CreateDirectory(dir);
            filePath = Path.Combine(dir, assetName);

            // O Timeout do HttpClient só cobre os cabeçalhos quando se lê em fluxo: numa conexão
            // travada, o laço abaixo esperava para sempre e o botão Instalar ficava preso.
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            using var resp = await Http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            resp.EnsureSuccessStatusCode();
            long? total = resp.Content.Headers.ContentLength;
            long readTotal = 0;
            await using (var httpStream = await resp.Content.ReadAsStreamAsync(cts.Token))
            await using (var fileStream = File.Create(filePath))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await httpStream.ReadAsync(buffer, cts.Token)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), cts.Token);
                    readTotal += read;
                    if (total is > 0) progress?.Report((double)readTotal / total.Value);
                }
            }

            // Download interrompido: executar um instalador truncado só daria um erro obscuro.
            if (total is > 0 && readTotal != total.Value)
                throw new IOException($"Download incompleto: {readTotal} de {total.Value} bytes.");
        }
        catch (Exception ex)
        {
            LogFailure("download", ex);
            return new UpdateInstallResult(false, ex.Message);
        }

        try
        {
            // O instalador procura o Pulse1x pelo AppMutex e, em modo silencioso, a pergunta "feche
            // o Pulse1x" assume Cancelar: a atualização abortava com código 1 (registrado no
            // update.log). Agora quem sai do caminho é o app: o instalador é agendado para daqui a
            // ~3 s por um cmd filho — que herda a elevação, sem novo UAC — e o chamador encerra o
            // Pulse1x em seguida, liberando o mutex. Ao terminar, o próprio instalador reabre o app.
            string log = Path.Combine(Path.GetDirectoryName(filePath)!, "install.log");
            string command = $"/c ping -n 4 127.0.0.1 >nul & \"{filePath}\" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG=\"{log}\"";
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                Arguments = command,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process is null)
                throw new Exception("Process.Start retornou null.");
            return new UpdateInstallResult(true, null);
        }
        catch (Exception ex)
        {
            LogFailure("install", ex);
            return new UpdateInstallResult(false, ex.Message);
        }
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
