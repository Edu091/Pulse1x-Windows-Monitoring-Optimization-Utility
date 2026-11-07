using System.IO;
using System.Runtime.InteropServices;

namespace Pulse1x.App.Services.Sound;

/// <summary>
/// Observa a pasta Downloads por um tempo e avisa quando um arquivo de áudio novo termina de baixar.
/// É o caminho dos memes: o usuário abre o site pelo Pulse1x, baixa o som no navegador e ele entra
/// na mesa sozinho — o Pulse1x nunca acessa o site por conta própria. Arrastar e soltar não serve
/// aqui porque o Windows bloqueia arrastar de programas comuns para janelas de administrador.
/// </summary>
public sealed class DownloadsWatcher : IDisposable
{
    private static readonly string[] Extensions = { ".mp3", ".wav", ".m4a", ".aac", ".wma", ".ogg", ".flac" };

    private FileSystemWatcher? _watcher;
    private Timer? _stopTimer;
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    /// <summary>Disparado (em thread de fundo) com o caminho de um áudio novo já completo.</summary>
    public event Action<string>? AudioDownloaded;

    public bool IsWatching => _watcher is not null;

    public static string? DownloadsFolder()
    {
        try
        {
            var id = new Guid("374DE290-123F-4565-9164-39C4925E467B"); // FOLDERID_Downloads
            if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out IntPtr ptr) == 0)
            {
                try { return Marshal.PtrToStringUni(ptr); }
                finally { Marshal.FreeCoTaskMem(ptr); }
            }
        }
        catch { }
        string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        return Directory.Exists(fallback) ? fallback : null;
    }

    /// <summary>Começa (ou renova) a observação por <paramref name="duration"/>.</summary>
    public bool Start(TimeSpan duration, string? folder = null)
    {
        folder ??= DownloadsFolder();
        if (folder is null || !Directory.Exists(folder)) return false;
        lock (_gate)
        {
            if (_watcher is null)
            {
                _watcher = new FileSystemWatcher(folder) { IncludeSubdirectories = false, EnableRaisingEvents = false };
                // Navegadores baixam como .crdownload/.part e renomeiam no fim: os dois eventos contam.
                _watcher.Created += (_, e) => Consider(e.FullPath);
                _watcher.Renamed += (_, e) => Consider(e.FullPath);
                _watcher.EnableRaisingEvents = true;
            }
            _stopTimer?.Dispose();
            _stopTimer = new Timer(_ => Stop(), null, duration, Timeout.InfiniteTimeSpan);
        }
        return true;
    }

    public void Stop()
    {
        lock (_gate)
        {
            _stopTimer?.Dispose();
            _stopTimer = null;
            _watcher?.Dispose();
            _watcher = null;
        }
    }

    private void Consider(string path)
    {
        if (!Extensions.Contains(Path.GetExtension(path).ToLowerInvariant())) return;
        lock (_gate) { if (!_seen.Add(path)) return; }
        _ = Task.Run(async () =>
        {
            // Espera o arquivo parar de crescer e ficar livre (o navegador ainda pode estar escrevendo).
            long last = -1;
            for (int i = 0; i < 40; i++)
            {
                await Task.Delay(250);
                try
                {
                    var info = new FileInfo(path);
                    if (!info.Exists) return;
                    if (info.Length > 0 && info.Length == last)
                    {
                        using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read)) { }
                        AudioDownloaded?.Invoke(path);
                        return;
                    }
                    last = info.Length;
                }
                catch (IOException) { }
            }
        });
    }

    public void Dispose() => Stop();

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out IntPtr path);
}
