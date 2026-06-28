using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Pulse1x.App.Models;

namespace Pulse1x.App.Services;

/// <summary>
/// Limpeza de disco segura: identifica e remove apenas arquivos temporários, caches e
/// resíduos comprovadamente descartáveis. Nunca toca em documentos, imagens, vídeos,
/// músicas, Área de Trabalho, Downloads, jogos, programas, drivers ou arquivos do Windows.
///
/// Toda exclusão é tolerante a falhas: arquivos em uso (bloqueados) ou sem permissão são
/// simplesmente pulados, sem interromper a operação nem comprometer a estabilidade.
/// </summary>
public class DiskCleanupService
{
    /// <summary>Monta a lista de categorias, incluindo alvos dinâmicos (navegadores instalados).</summary>
    public IReadOnlyList<CleanupCategoryDefinition> GetCategories()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        string temp = Path.GetTempPath();
        string winRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        var categories = new List<CleanupCategoryDefinition>
        {
            new()
            {
                Key = "win_temp",
                Title = "Arquivos Temporários do Windows",
                Description = "Arquivos temporários criados pelo Windows, por instaladores e por aplicativos. " +
                              "Arquivos em uso são automaticamente ignorados.",
                Safety = CleanupSafety.TotallySafe,
                Targets = new[]
                {
                    new CleanupTarget("Pasta TEMP do usuário", temp, "*", true, 0, true),
                    new CleanupTarget("Pasta TEMP do sistema", Path.Combine(winRoot, "Temp"), "*", true, 0, true),
                }
            },
            new()
            {
                Key = "win_cache",
                Title = "Cache do Windows",
                Description = "Cache de miniaturas e ícones do Explorer e relatórios de erro antigos. " +
                              "O Windows recria esses caches automaticamente quando precisar.",
                Safety = CleanupSafety.TotallySafe,
                Targets = new[]
                {
                    new CleanupTarget("Cache de miniaturas", Path.Combine(local, "Microsoft", "Windows", "Explorer"), "thumbcache_*.db", false, 0, false),
                    new CleanupTarget("Cache de ícones", Path.Combine(local, "Microsoft", "Windows", "Explorer"), "iconcache_*.db", false, 0, false),
                    new CleanupTarget("Relatórios de erro (usuário)", Path.Combine(local, "Microsoft", "Windows", "WER"), "*", true, 0, true),
                    new CleanupTarget("Relatórios de erro (sistema)", Path.Combine(programData, "Microsoft", "Windows", "WER"), "*", true, 0, true),
                }
            },
            new()
            {
                Key = "recycle",
                Title = "Lixeira",
                Description = "Esvazia a Lixeira de todas as unidades. Atenção: os itens na Lixeira são " +
                              "apagados de forma permanente e não poderão mais ser restaurados.",
                Safety = CleanupSafety.Safe,
                IsRecycleBin = true,
            },
            new()
            {
                Key = "browser",
                Title = "Cache de Navegadores",
                Description = "Remove apenas o cache de páginas, imagens e scripts dos navegadores instalados. " +
                              "Favoritos, senhas, logins, extensões e histórico NÃO são afetados.",
                Safety = CleanupSafety.TotallySafe,
                Targets = BuildBrowserTargets(local, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)),
            },
            new()
            {
                Key = "app_cache",
                Title = "Cache de Aplicativos",
                Description = "Despejos de falha e cache de internet legado (WinINet). Conteúdo descartável " +
                              "que aplicativos recriam quando necessário.",
                Safety = CleanupSafety.Safe,
                Targets = new[]
                {
                    new CleanupTarget("Despejos de falha (CrashDumps)", Path.Combine(local, "CrashDumps"), "*", true, 0, true),
                    new CleanupTarget("Cache de internet (WinINet)", Path.Combine(local, "Microsoft", "Windows", "INetCache"), "*", true, 0, true),
                }
            },
            new()
            {
                Key = "gfx",
                Title = "Cache Gráfico",
                Description = "Caches de shaders do DirectX e das placas de vídeo. São totalmente recriáveis; " +
                              "após a limpeza, o primeiro carregamento de jogos pode demorar um pouco mais.",
                Safety = CleanupSafety.TotallySafe,
                Targets = new[]
                {
                    new CleanupTarget("DirectX Shader Cache", Path.Combine(local, "D3DSCache"), "*", true, 0, true),
                    new CleanupTarget("NVIDIA DXCache", Path.Combine(local, "NVIDIA", "DXCache"), "*", true, 0, true),
                    new CleanupTarget("NVIDIA GLCache", Path.Combine(local, "NVIDIA", "GLCache"), "*", true, 0, true),
                    new CleanupTarget("NVIDIA NV_Cache", Path.Combine(programData, "NVIDIA Corporation", "NV_Cache"), "*", true, 0, true),
                    new CleanupTarget("AMD DxCache", Path.Combine(local, "AMD", "DxCache"), "*", true, 0, true),
                }
            },
            new()
            {
                Key = "fonts",
                Title = "Cache de Fontes",
                Description = "Arquivos temporários do cache de fontes. Arquivos em uso pelo serviço de fontes " +
                              "são ignorados automaticamente.",
                Safety = CleanupSafety.Safe,
                Targets = new[]
                {
                    new CleanupTarget("Cache de fontes (usuário)", Path.Combine(local, "FontCache"), "*", true, 0, true),
                    new CleanupTarget("Cache de fontes (sistema)", Path.Combine(winRoot, "ServiceProfiles", "LocalService", "AppData", "Local", "FontCache"), "*", true, 0, true),
                }
            },
            new()
            {
                Key = "prefetch",
                Title = "Arquivos Prefetch",
                Description = "Remove apenas arquivos de Prefetch obsoletos (mais de 30 dias). O Windows os recria; " +
                              "alguns programas podem abrir um pouco mais devagar na primeira vez depois disso.",
                Safety = CleanupSafety.RequiresConfirmation,
                Targets = new[]
                {
                    new CleanupTarget("Prefetch obsoleto (> 30 dias)", Path.Combine(winRoot, "Prefetch"), "*.pf", false, 30, false),
                }
            },
            new()
            {
                Key = "update_residue",
                Title = "Resíduos de Atualizações",
                Description = "Arquivos de instalação de atualizações do Windows que já foram aplicadas. " +
                              "O Windows volta a baixá-los se precisar.",
                Safety = CleanupSafety.Safe,
                Targets = new[]
                {
                    new CleanupTarget("Downloads do Windows Update", Path.Combine(winRoot, "SoftwareDistribution", "Download"), "*", true, 0, true),
                }
            },
            new()
            {
                Key = "delivery",
                Title = "Otimização de Entrega",
                Description = "Cache usado pelo sistema de distribuição de atualizações (Delivery Optimization). " +
                              "Seguro de remover quando não está mais em uso.",
                Safety = CleanupSafety.Safe,
                Targets = new[]
                {
                    new CleanupTarget("Cache de Delivery Optimization", Path.Combine(winRoot, "SoftwareDistribution", "DeliveryOptimization"), "*", true, 0, true),
                    new CleanupTarget("Cache (NetworkService)", Path.Combine(winRoot, "ServiceProfiles", "NetworkService", "AppData", "Local", "Microsoft", "Windows", "DeliveryOptimization", "Cache"), "*", true, 0, true),
                }
            },
            new()
            {
                Key = "logs",
                Title = "Logs Antigos",
                Description = "Arquivos de log e diagnóstico do Windows com mais de 7 dias. Logs em uso são ignorados.",
                Safety = CleanupSafety.Safe,
                Targets = new[]
                {
                    new CleanupTarget("Logs do Windows (> 7 dias)", Path.Combine(winRoot, "Logs"), "*.log", true, 7, false),
                }
            },
        };

        // Mantém só categorias com algo a fazer (a Lixeira é sempre incluída).
        return categories
            .Where(c => c.IsRecycleBin || c.Targets.Any(t => Directory.Exists(t.Directory)))
            .ToList();
    }

    public DiskSpaceInfo GetSystemDriveSpace()
    {
        try
        {
            string root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\";
            var drive = new DriveInfo(root);
            long total = drive.TotalSize;
            long free = drive.TotalFreeSpace;
            long used = Math.Max(0, total - free);
            double pct = total > 0 ? used / (double)total * 100 : 0;
            return new DiskSpaceInfo(total, used, free, pct);
        }
        catch
        {
            return new DiskSpaceInfo(0, 0, 0, 0);
        }
    }

    public Task<IReadOnlyList<CleanupCategoryResult>> AnalyzeAsync(
        IReadOnlyList<CleanupCategoryDefinition> categories, IProgress<double>? progress = null)
    {
        return Task.Run<IReadOnlyList<CleanupCategoryResult>>(() =>
        {
            var results = new List<CleanupCategoryResult>();
            int done = 0;

            foreach (var category in categories)
            {
                if (category.IsRecycleBin)
                {
                    var (bytes, count) = QueryRecycleBin();
                    results.Add(new CleanupCategoryResult(category.Key, bytes, count,
                        new[] { new CleanupDetail("Itens na Lixeira", bytes, count) }));
                }
                else
                {
                    long totalBytes = 0;
                    int totalCount = 0;
                    var details = new List<CleanupDetail>();

                    foreach (var target in category.Targets)
                    {
                        var (b, c) = MeasureTarget(target);
                        if (c > 0)
                            details.Add(new CleanupDetail(target.Label, b, c));
                        totalBytes += b;
                        totalCount += c;
                    }

                    results.Add(new CleanupCategoryResult(category.Key, totalBytes, totalCount, details));
                }

                done++;
                progress?.Report(done / (double)categories.Count);
            }

            return results;
        });
    }

    public Task<CleanupRunResult> CleanAsync(
        IReadOnlyList<CleanupCategoryDefinition> categories, IProgress<CleanupProgress>? progress = null)
    {
        return Task.Run(() =>
        {
            var sw = Stopwatch.StartNew();
            long freed = 0;
            int removed = 0;
            var cleaned = new List<string>();

            int totalUnits = categories.Sum(c => c.IsRecycleBin ? 1 : c.Targets.Count);
            if (totalUnits == 0) totalUnits = 1;
            int unit = 0;

            foreach (var category in categories)
            {
                bool anything = false;

                if (category.IsRecycleBin)
                {
                    var (bytes, count) = QueryRecycleBin();
                    if (EmptyRecycleBin() && count > 0)
                    {
                        freed += bytes;
                        removed += count;
                        anything = true;
                    }
                    unit++;
                    progress?.Report(new CleanupProgress(category.Title, (double)unit / totalUnits, freed, removed));
                }
                else
                {
                    foreach (var target in category.Targets)
                    {
                        var (b, c) = CleanTarget(target);
                        freed += b;
                        removed += c;
                        if (c > 0) anything = true;

                        unit++;
                        progress?.Report(new CleanupProgress(category.Title, (double)unit / totalUnits, freed, removed));
                    }
                }

                if (anything)
                    cleaned.Add(category.Title);
            }

            sw.Stop();
            return new CleanupRunResult(freed, removed, cleaned, sw.Elapsed, DateTime.Now);
        });
    }

    // ===================== Arquivos grandes (Limpeza Inteligente) =====================

    /// <summary>
    /// Procura arquivos grandes (≥ minBytes) dentro das pastas pessoais do usuário
    /// (Downloads, Documentos, Vídeos, Área de Trabalho, etc.), ignorando AppData,
    /// junções/links e qualquer coisa fora do perfil — portanto, nada de sistema,
    /// programas, jogos ou drivers. A decisão de apagar é sempre do usuário.
    /// </summary>
    // Pastas que nunca são varridas: sistema, programas, dados de app e bibliotecas de jogos.
    // Assim, os "arquivos grandes" sugeridos são sempre conteúdo do usuário — nunca arquivos
    // de programa/jogo (que devem ser desinstalados, não apagados) nem do Windows.
    private static readonly HashSet<string> ExcludedDirNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Windows", "Windows.old", "Program Files", "Program Files (x86)", "ProgramData",
        "AppData", "$Recycle.Bin", "System Volume Information", "Recovery", "Config.Msi",
        "PerfLogs", "MSOCache", "Intel", "AMD", "NVIDIA",
        "steamapps", "Epic Games", "GOG Galaxy", "Riot Games", "EA Games", "Origin Games",
        "Battle.net", "Ubisoft", "Ubisoft Game Launcher",
    };

    // Atributos que marcam arquivos a ignorar: offline / placeholders de nuvem (OneDrive).
    private const int CloudOrOfflineMask = 0x1000 /*Offline*/ | 0x40000 /*RecallOnOpen*/ | 0x400000 /*RecallOnDataAccess*/;

    public Task<IReadOnlyList<LargeFileInfo>> ScanLargeFilesAsync(long minBytes, int maxResults, IProgress<int>? progress = null)
    {
        return Task.Run<IReadOnlyList<LargeFileInfo>>(() =>
        {
            var found = new List<LargeFileInfo>();
            foreach (var root in GetScanRoots())
                ScanForLargeFiles(root, minBytes, found, progress);
            return found.OrderByDescending(f => f.Bytes).Take(maxResults).ToList();
        });
    }

    // Raízes de varredura: todos os discos fixos prontos (C:, D:, ...).
    private static IEnumerable<string> GetScanRoots()
    {
        DriveInfo[] drives;
        try { drives = DriveInfo.GetDrives(); }
        catch { yield break; }

        foreach (var d in drives)
        {
            bool ok;
            try { ok = d.DriveType == DriveType.Fixed && d.IsReady; }
            catch { ok = false; }
            if (ok) yield return d.RootDirectory.FullName;
        }
    }

    private static void ScanForLargeFiles(string root, long minBytes, List<LargeFileInfo> found, IProgress<int>? progress)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        int scannedDirs = 0;

        while (stack.Count > 0)
        {
            string current = stack.Pop();

            // Ignora junções/links simbólicos (evita loops e duplicatas).
            try
            {
                if (new DirectoryInfo(current).Attributes.HasFlag(FileAttributes.ReparsePoint))
                    continue;
            }
            catch { continue; }

            string[] files;
            try { files = Directory.GetFiles(current); }
            catch { files = Array.Empty<string>(); }

            foreach (var f in files)
            {
                try
                {
                    var fi = new FileInfo(f);
                    if (fi.Length < minBytes) continue;
                    // Pula arquivos de sistema (pagefile.sys, hiberfil.sys, etc.) e placeholders de nuvem.
                    if (fi.Attributes.HasFlag(FileAttributes.System)) continue;
                    if (((int)fi.Attributes & CloudOrOfflineMask) != 0) continue;
                    found.Add(new LargeFileInfo(f, fi.Length, fi.LastWriteTime));
                }
                catch { /* sem acesso — ignora */ }
            }

            scannedDirs++;
            if (scannedDirs % 40 == 0)
                progress?.Report(found.Count);

            string[] subdirs;
            try { subdirs = Directory.GetDirectories(current); }
            catch { subdirs = Array.Empty<string>(); }

            foreach (var d in subdirs)
            {
                if (ExcludedDirNames.Contains(Path.GetFileName(d)))
                    continue;
                stack.Push(d);
            }
        }
    }

    // ===================== Aplicativos instalados =====================

    /// <summary>Lê os aplicativos instalados (registro de desinstalação), ordenados por tamanho.</summary>
    public Task<IReadOnlyList<InstalledAppInfo>> GetInstalledApplicationsAsync()
    {
        return Task.Run<IReadOnlyList<InstalledAppInfo>>(() =>
        {
            var apps = new Dictionary<string, InstalledAppInfo>(StringComparer.OrdinalIgnoreCase);
            ReadUninstallKey(Microsoft.Win32.Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", apps);
            ReadUninstallKey(Microsoft.Win32.Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", apps);
            ReadUninstallKey(Microsoft.Win32.Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", apps);
            return apps.Values.OrderByDescending(a => a.Bytes).ToList();
        });
    }

    private static void ReadUninstallKey(Microsoft.Win32.RegistryKey hive, string path, Dictionary<string, InstalledAppInfo> apps)
    {
        try
        {
            using var key = hive.OpenSubKey(path);
            if (key is null) return;

            foreach (var subName in key.GetSubKeyNames())
            {
                try
                {
                    using var sub = key.OpenSubKey(subName);
                    if (sub is null) continue;

                    string? name = sub.GetValue("DisplayName") as string;
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    // Pula componentes do sistema, atualizações e entradas filhas.
                    if ((sub.GetValue("SystemComponent") as int? ?? 0) == 1) continue;
                    if (sub.GetValue("ParentKeyName") is not null) continue;
                    if (sub.GetValue("ReleaseType") is string rt &&
                        (rt.Contains("Update", StringComparison.OrdinalIgnoreCase) || rt.Contains("Hotfix", StringComparison.OrdinalIgnoreCase)))
                        continue;

                    string? uninstall = (sub.GetValue("QuietUninstallString") as string)
                                        ?? (sub.GetValue("UninstallString") as string);
                    if (string.IsNullOrWhiteSpace(uninstall)) continue;

                    // Só lista apps com tamanho conhecido (para poder ordenar por peso).
                    long bytes = 0;
                    if (sub.GetValue("EstimatedSize") is int kb && kb > 0)
                        bytes = (long)kb * 1024;
                    if (bytes <= 0) continue;

                    string publisher = sub.GetValue("Publisher") as string ?? "";
                    DateTime? installDate = ParseInstallDate(sub.GetValue("InstallDate") as string);

                    if (!apps.ContainsKey(name))
                        apps[name] = new InstalledAppInfo(name, publisher, bytes, installDate, uninstall!);
                }
                catch { /* entrada malformada — ignora */ }
            }
        }
        catch { /* sem acesso à chave — ignora */ }
    }

    private static DateTime? ParseInstallDate(string? raw)
    {
        if (!string.IsNullOrWhiteSpace(raw) && raw.Length == 8 &&
            DateTime.TryParseExact(raw, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var dt))
            return dt;
        return null;
    }

    /// <summary>Abre o desinstalador oficial do aplicativo (o Windows/o programa cuida da remoção).</summary>
    public void LaunchUninstaller(string command)
    {
        var (exe, args) = SplitCommand(command);
        Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = true
        });
    }

    private static (string exe, string args) SplitCommand(string command)
    {
        command = command.Trim();
        if (command.StartsWith("\""))
        {
            int end = command.IndexOf('"', 1);
            if (end > 0)
                return (command.Substring(1, end - 1), command[(end + 1)..].Trim());
        }
        int space = command.IndexOf(' ');
        return space < 0 ? (command, "") : (command[..space], command[(space + 1)..].Trim());
    }

    /// <summary>Envia os arquivos para a Lixeira (reversível). Retorna o total liberado e a contagem.</summary>
    public Task<(long freed, int removed)> RecycleFilesAsync(IReadOnlyList<(string Path, long Bytes)> files)
    {
        return Task.Run(() =>
        {
            if (files.Count == 0) return (0L, 0);

            // pFrom precisa terminar com duplo \0.
            string from = string.Join("\0", files.Select(f => f.Path)) + "\0\0";
            var op = new SHFILEOPSTRUCT
            {
                wFunc = FO_DELETE,
                pFrom = from,
                fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT)
            };

            try { SHFileOperation(ref op); }
            catch { /* falha geral — verificamos arquivo a arquivo abaixo */ }

            long freed = 0;
            int removed = 0;
            foreach (var (path, bytes) in files)
            {
                if (!File.Exists(path))
                {
                    freed += bytes;
                    removed++;
                }
            }
            return (freed, removed);
        });
    }

    // ===================== Varredura / exclusão =====================

    private static (long bytes, int count) MeasureTarget(CleanupTarget target)
    {
        long bytes = 0;
        int count = 0;
        DateTime cutoff = DateTime.UtcNow.AddDays(-target.MinAgeDays);

        foreach (var file in EnumerateFilesSafe(target.Directory, target.Pattern, target.Recursive))
        {
            try
            {
                var fi = new FileInfo(file);
                if (target.MinAgeDays > 0 && fi.LastWriteTimeUtc > cutoff) continue;
                bytes += fi.Length;
                count++;
            }
            catch { /* arquivo sumiu/sem acesso — ignora */ }
        }

        return (bytes, count);
    }

    private static (long bytes, int count) CleanTarget(CleanupTarget target)
    {
        long bytes = 0;
        int count = 0;
        DateTime cutoff = DateTime.UtcNow.AddDays(-target.MinAgeDays);

        foreach (var file in EnumerateFilesSafe(target.Directory, target.Pattern, target.Recursive))
        {
            try
            {
                var fi = new FileInfo(file);
                if (target.MinAgeDays > 0 && fi.LastWriteTimeUtc > cutoff) continue;

                long len = fi.Length;
                if (fi.Attributes.HasFlag(FileAttributes.ReadOnly))
                    fi.Attributes &= ~FileAttributes.ReadOnly;
                fi.Delete();

                bytes += len;
                count++;
            }
            catch { /* em uso/sem permissão/protegido — pula com segurança */ }
        }

        if (target.RemoveEmptyDirs)
            RemoveEmptyDirectories(target.Directory);

        return (bytes, count);
    }

    // Enumera arquivos tolerando pastas sem permissão (não lança no meio da varredura).
    private static IEnumerable<string> EnumerateFilesSafe(string root, string pattern, bool recursive)
    {
        if (!Directory.Exists(root)) yield break;

        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            string current = stack.Pop();

            string[] files;
            try { files = Directory.GetFiles(current, pattern); }
            catch { files = Array.Empty<string>(); }

            foreach (var f in files)
                yield return f;

            if (!recursive) continue;

            string[] subdirs;
            try { subdirs = Directory.GetDirectories(current); }
            catch { subdirs = Array.Empty<string>(); }

            foreach (var d in subdirs)
                stack.Push(d);
        }
    }

    // Remove subpastas vazias (nunca a raiz informada).
    private static void RemoveEmptyDirectories(string root)
    {
        string[] subdirs;
        try { subdirs = Directory.GetDirectories(root); }
        catch { return; }

        foreach (var dir in subdirs)
        {
            RemoveEmptyDirectories(dir);
            try
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir, false);
            }
            catch { /* sem permissão/em uso — ignora */ }
        }
    }

    // ===================== Navegadores (alvos dinâmicos) =====================

    private static IReadOnlyList<CleanupTarget> BuildBrowserTargets(string local, string roaming)
    {
        var targets = new List<CleanupTarget>();

        // Navegadores baseados em Chromium (Chrome, Edge, Brave, Vivaldi, Opera).
        var chromium = new (string name, string userData)[]
        {
            ("Google Chrome", Path.Combine(local, "Google", "Chrome", "User Data")),
            ("Microsoft Edge", Path.Combine(local, "Microsoft", "Edge", "User Data")),
            ("Brave", Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data")),
            ("Vivaldi", Path.Combine(local, "Vivaldi", "User Data")),
            ("Opera", Path.Combine(roaming, "Opera Software", "Opera Stable")),
        };

        string[] cacheSubdirs = { "Cache", "Code Cache", "GPUCache", "Service Worker\\CacheStorage" };

        foreach (var (name, userData) in chromium)
        {
            if (!Directory.Exists(userData)) continue;

            foreach (var profile in EnumerateChromiumProfiles(userData))
            {
                string profileName = Path.GetFileName(profile);
                foreach (var sub in cacheSubdirs)
                {
                    string dir = Path.Combine(profile, sub);
                    if (Directory.Exists(dir))
                        targets.Add(new CleanupTarget($"{name} — {profileName} ({sub})", dir, "*", true, 0, true));
                }
            }
        }

        // Mozilla Firefox.
        string firefoxProfiles = Path.Combine(local, "Mozilla", "Firefox", "Profiles");
        if (Directory.Exists(firefoxProfiles))
        {
            foreach (var profile in SafeGetDirectories(firefoxProfiles))
            {
                string cache2 = Path.Combine(profile, "cache2");
                if (Directory.Exists(cache2))
                    targets.Add(new CleanupTarget($"Firefox — {Path.GetFileName(profile)}", cache2, "*", true, 0, true));
            }
        }

        return targets;
    }

    private static IEnumerable<string> EnumerateChromiumProfiles(string userData)
    {
        bool any = false;
        foreach (var dir in SafeGetDirectories(userData))
        {
            string name = Path.GetFileName(dir);
            if (name.Equals("Default", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase))
            {
                any = true;
                yield return dir;
            }
        }

        // Opera e perfis únicos guardam o cache na própria pasta raiz.
        if (!any)
            yield return userData;
    }

    private static string[] SafeGetDirectories(string path)
    {
        try { return Directory.GetDirectories(path); }
        catch { return Array.Empty<string>(); }
    }

    // ===================== Lixeira (Shell API) =====================

    [StructLayout(LayoutKind.Sequential)]
    private struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    private const uint SHERB_NOCONFIRMATION = 0x00000001;
    private const uint SHERB_NOPROGRESSUI = 0x00000002;
    private const uint SHERB_NOSOUND = 0x00000004;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    private static (long bytes, int count) QueryRecycleBin()
    {
        try
        {
            var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
            // null = todas as unidades.
            if (SHQueryRecycleBin(null, ref info) == 0)
                return (info.i64Size, (int)Math.Min(info.i64NumItems, int.MaxValue));
        }
        catch { /* indisponível */ }
        return (0, 0);
    }

    private static bool EmptyRecycleBin()
    {
        try
        {
            int hr = SHEmptyRecycleBin(IntPtr.Zero, null,
                SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
            return hr == 0; // S_OK
        }
        catch
        {
            return false;
        }
    }

    // ===================== SHFileOperation (mover para a Lixeira) =====================

    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;     // envia para a Lixeira (reversível)
    private const ushort FOF_NOERRORUI = 0x0400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);
}
