using System.IO;
using System.Text.Json;
using Pulse1x.App.Models.GameHub;

namespace Pulse1x.App.Services.GameHub;

/// <summary>Resumo de uma varredura, mostrado ao usuário ao final.</summary>
public record ScanResult(int Added, int Updated, int Removed, IReadOnlyList<string> Sources);

/// <summary>
/// Guarda e organiza a biblioteca do GameHub (jogos, aplicativos, ROMs e emuladores). Persiste em
/// <c>%APPDATA%\Pulse1x\gamehub-library.json</c>, seguindo o mesmo padrão dos outros módulos do
/// Pulse1x (SettingsService, OptimizationChangeLog).
///
/// Regra central da varredura automática: itens criados à mão pelo usuário NUNCA são alterados ou
/// removidos. A detecção só mexe no que ela mesma criou — assim, desinstalar um jogo o tira da
/// lista sozinho, sem risco de apagar o que o usuário cadastrou.
/// </summary>
public class GameLibraryService
{
    private readonly string _filePath;
    private readonly object _gate = new();
    private GameLibraryData _data = new();

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Disparado quando a biblioteca muda (item adicionado/removido/editado, varredura).</summary>
    public event Action? Changed;

    public GameLibraryService()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pulse1x");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "gamehub-library.json");
        Load();
    }

    // =====================================================================================
    //  Persistência
    // =====================================================================================

    private void Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var data = JsonSerializer.Deserialize<GameLibraryData>(File.ReadAllText(_filePath));
                if (data is not null) { _data = data; return; }
            }
        }
        catch
        {
            // Arquivo corrompido/inacessível: começa com uma biblioteca vazia em vez de impedir o
            // GameHub de abrir. O usuário pode rodar a detecção de novo.
        }
        _data = new GameLibraryData();
    }

    public void Save()
    {
        lock (_gate)
        {
            try { File.WriteAllText(_filePath, JsonSerializer.Serialize(_data, JsonOptions)); }
            catch { /* disco cheio/sem permissão: o estado em memória continua válido nesta sessão */ }
        }
        Changed?.Invoke();
    }

    // =====================================================================================
    //  Consulta
    // =====================================================================================

    public IReadOnlyList<GameEntry> Games
    {
        get { lock (_gate) return _data.Games.ToList(); }
    }

    public IReadOnlyList<EmulatorEntry> Emulators
    {
        get { lock (_gate) return _data.Emulators.ToList(); }
    }

    public IReadOnlyList<string> Categories
    {
        get
        {
            lock (_gate)
                return _data.Categories
                    .Concat(_data.Games.Select(g => g.Category))
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct(StringComparer.CurrentCultureIgnoreCase)
                    .OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
        }
    }

    public IReadOnlyList<string> CustomScanFolders
    {
        get { lock (_gate) return _data.CustomScanFolders.ToList(); }
    }

    public DateTime? LastScan
    {
        get { lock (_gate) return _data.LastScan; }
    }

    public GameEntry? FindGame(string id)
    {
        lock (_gate) return _data.Games.FirstOrDefault(g => g.Id == id);
    }

    public EmulatorEntry? FindEmulator(string id)
    {
        lock (_gate) return _data.Emulators.FirstOrDefault(e => e.Id == id);
    }

    // =====================================================================================
    //  Edição
    // =====================================================================================

    public void AddGame(GameEntry game)
    {
        lock (_gate)
        {
            if (_data.Games.Any(g => g.DedupeKey == game.DedupeKey)) return;
            _data.Games.Add(game);
        }
        Save();
    }

    public void UpdateGame(GameEntry game)
    {
        lock (_gate)
        {
            int index = _data.Games.FindIndex(g => g.Id == game.Id);
            if (index >= 0) _data.Games[index] = game;
            else _data.Games.Add(game);
        }
        Save();
    }

    public void RemoveGame(string id)
    {
        lock (_gate) _data.Games.RemoveAll(g => g.Id == id);
        Save();
    }

    public void AddOrUpdateEmulator(EmulatorEntry emulator)
    {
        lock (_gate)
        {
            int index = _data.Emulators.FindIndex(e => e.Id == emulator.Id);
            if (index >= 0) _data.Emulators[index] = emulator;
            else _data.Emulators.Add(emulator);
        }
        Save();
    }

    /// <summary>Remove um emulador e, junto, as ROMs que ele havia trazido para a biblioteca.</summary>
    public void RemoveEmulator(string id)
    {
        lock (_gate)
        {
            _data.Emulators.RemoveAll(e => e.Id == id);
            _data.Games.RemoveAll(g => g.EmulatorId == id && g.AutoDetected);
        }
        Save();
    }

    public void AddCategory(string category)
    {
        if (string.IsNullOrWhiteSpace(category)) return;
        lock (_gate)
        {
            if (!_data.Categories.Contains(category, StringComparer.CurrentCultureIgnoreCase))
                _data.Categories.Add(category.Trim());
        }
        Save();
    }

    public void AddScanFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return;
        lock (_gate)
        {
            if (!_data.CustomScanFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
                _data.CustomScanFolders.Add(folder);
        }
        Save();
    }

    public void RemoveScanFolder(string folder)
    {
        lock (_gate) _data.CustomScanFolders.RemoveAll(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));
        Save();
    }

    /// <summary>
    /// Remove duplicatas que já estejam na biblioteca — o mesmo jogo detectado por duas lojas antes
    /// de a deduplicação existir. Só descarta itens detectados automaticamente e sem nada do
    /// usuário (favorito, perfil, tempo jogado); qualquer coisa que você tenha ajustado permanece.
    /// Devolve quantos foram removidos.
    /// </summary>
    public int RemoveDuplicates()
    {
        int removed;

        lock (_gate)
        {
            var groups = _data.Games
                .GroupBy(g => NormalizedName(g.Name), StringComparer.Ordinal)
                .Where(g => g.Key.Length > 0 && g.Count() > 1);

            var toRemove = new List<GameEntry>();

            foreach (var group in groups)
            {
                // O melhor item fica: prioridade de loja e, em empate, o que tem histórico de uso.
                var keeper = group
                    .OrderByDescending(g => LauncherPriority(g.Launcher))
                    .ThenByDescending(g => g.TotalMinutesPlayed)
                    .ThenByDescending(g => g.LaunchCount)
                    .First();

                foreach (var game in group)
                {
                    if (ReferenceEquals(game, keeper)) continue;
                    // Nunca remove o que o usuário criou ou personalizou.
                    if (!game.AutoDetected) continue;
                    if (game.IsFavorite || game.ProfileId is not null ||
                        game.TotalMinutesPlayed > 0 || game.ArtLockedByUser) continue;

                    toRemove.Add(game);
                }
            }

            removed = toRemove.Count;
            foreach (var game in toRemove) _data.Games.Remove(game);
        }

        if (removed > 0) Save();
        return removed;
    }

    /// <summary>Desfaz a associação de um perfil que foi apagado, em todos os itens que o usavam.</summary>
    public void ClearProfileReferences(string profileId)
    {
        lock (_gate)
        {
            foreach (var game in _data.Games.Where(g => g.ProfileId == profileId))
                game.ProfileId = null;
            foreach (var emulator in _data.Emulators.Where(e => e.DefaultProfileId == profileId))
                emulator.DefaultProfileId = null;
        }
        Save();
    }

    // =====================================================================================
    //  Registro de uso
    // =====================================================================================

    /// <summary>Contabiliza uma sessão encerrada: última execução, tempo total e nome do processo.</summary>
    public void RecordSession(string gameId, TimeSpan duration, string? processName)
    {
        lock (_gate)
        {
            var game = _data.Games.FirstOrDefault(g => g.Id == gameId);
            if (game is null) return;

            game.LastPlayed = DateTime.Now;
            game.LaunchCount++;
            // Sessões de menos de um minuto ainda contam como "jogou", mas não somam tempo.
            game.TotalMinutesPlayed += (long)Math.Max(0, Math.Round(duration.TotalMinutes));
            if (!string.IsNullOrEmpty(processName)) game.KnownProcessName = processName;
        }
        Save();
    }

    // =====================================================================================
    //  Varredura
    // =====================================================================================

    /// <summary>
    /// Procura jogos em todas as fontes disponíveis e reconcilia com a biblioteca:
    /// acrescenta o que é novo, atualiza o que mudou de lugar e remove o que foi desinstalado
    /// (sempre apenas entre os itens detectados automaticamente).
    /// </summary>
    public async Task<ScanResult> ScanAsync(IProgress<string>? progress = null)
    {
        var scanners = new List<ILauncherScanner>
        {
            new SteamScanner(), new EpicScanner(), new GogScanner(), new EaScanner(), new UbisoftScanner(),
        };
        var found = new List<GameEntry>();
        var sources = new List<string>();

        foreach (var scanner in scanners)
        {
            if (!scanner.IsInstalled) continue;
            progress?.Report(scanner.Kind.ToString());
            try
            {
                var games = await scanner.ScanAsync();
                if (games.Count > 0)
                {
                    found.AddRange(games);
                    sources.Add($"{scanner.Kind} ({games.Count})");
                }
            }
            catch { /* uma fonte com problema não pode derrubar a varredura inteira */ }
        }

        // Pastas indicadas pelo usuário.
        foreach (var folder in CustomScanFolders)
        {
            progress?.Report(folder);
            try
            {
                var games = await FolderScanner.ScanAsync(folder);
                if (games.Count > 0)
                {
                    found.AddRange(games);
                    sources.Add($"{Path.GetFileName(folder)} ({games.Count})");
                }
            }
            catch { }
        }

        // ROMs dos emuladores cadastrados.
        foreach (var emulator in Emulators)
        {
            progress?.Report(emulator.Name);
            try
            {
                var roms = await EmulatorScanner.ScanAsync(emulator);
                if (roms.Count > 0)
                {
                    found.AddRange(roms);
                    sources.Add($"{emulator.Name} ({roms.Count})");
                }
            }
            catch { }
        }

        return Reconcile(found, sources);
    }

    /// <summary>Varre somente os atalhos — é uma fonte ruidosa, então fica sob demanda.</summary>
    public async Task<ScanResult> ScanShortcutsAsync()
    {
        var found = new List<GameEntry>();
        try { found.AddRange(await new ShortcutScanner().ScanAsync()); }
        catch { }
        return Reconcile(found, new[] { $"Atalhos ({found.Count})" });
    }

    /// <summary>
    /// Varre apenas as lojas instaladas (Steam, Epic, GOG, EA, Ubisoft), sem pastas nem emuladores.
    /// É a opção "procurar nas lojas" do assistente de adicionar jogos.
    /// </summary>
    public async Task<ScanResult> ScanStoresAsync(IProgress<string>? progress = null)
    {
        var scanners = new List<ILauncherScanner>
        {
            new SteamScanner(), new EpicScanner(), new GogScanner(), new EaScanner(), new UbisoftScanner(),
        };

        var found = new List<GameEntry>();
        var sources = new List<string>();

        foreach (var scanner in scanners)
        {
            if (!scanner.IsInstalled) continue;
            progress?.Report(scanner.Kind.ToString());
            try
            {
                var games = await scanner.ScanAsync();
                if (games.Count == 0) continue;
                found.AddRange(games);
                sources.Add($"{scanner.Kind} ({games.Count})");
            }
            catch { }
        }

        return Reconcile(found, sources);
    }

    /// <summary>
    /// Varre uma pasta específica em busca de executáveis e já a memoriza como pasta de varredura,
    /// para as próximas detecções automáticas incluírem o que for instalado ali depois.
    /// </summary>
    public async Task<ScanResult> ScanFolderAsync(string folder, bool remember = true)
    {
        var found = new List<GameEntry>();
        try { found.AddRange(await FolderScanner.ScanAsync(folder)); }
        catch { }

        if (remember) AddScanFolder(folder);
        return Reconcile(found, new[] { $"{Path.GetFileName(folder)} ({found.Count})" });
    }

    /// <summary>Varre as ROMs de um emulador recém-cadastrado, sem mexer no resto da biblioteca.</summary>
    public async Task<ScanResult> ScanEmulatorAsync(EmulatorEntry emulator)
    {
        var found = new List<GameEntry>();
        try { found.AddRange(await EmulatorScanner.ScanAsync(emulator)); }
        catch { }
        return Reconcile(found, new[] { $"{emulator.Name} ({found.Count})" });
    }

    /// <summary>Reconcilia o que foi encontrado com o que já está na biblioteca.</summary>
    /// <summary>
    /// Prioridade da loja quando o MESMO jogo é encontrado por mais de uma fonte. O caso clássico é
    /// um jogo comprado na Steam que também registra uma entrada da EA (porque usa o EA Desktop por
    /// baixo): ele aparecia duas vezes na biblioteca. Vence quem realmente inicia o jogo melhor —
    /// a loja onde ele foi instalado.
    /// </summary>
    private static int LauncherPriority(LauncherKind kind) => kind switch
    {
        LauncherKind.Steam => 100,
        LauncherKind.Epic => 90,
        LauncherKind.Gog => 85,
        LauncherKind.Ubisoft => 80,
        LauncherKind.Ea => 70,
        LauncherKind.Emulator => 60,
        LauncherKind.Manual => 50,
        LauncherKind.Application => 40,
        LauncherKind.Shortcut => 10,
        _ => 0,
    };

    /// <summary>
    /// Nome normalizado para comparar títulos entre lojas: sem acentos de marca, sem pontuação e
    /// sem diferença de maiúsculas. "Need for Speed™ Unbound" e "Need for Speed Unbound" viram a
    /// mesma chave.
    /// </summary>
    private static string NormalizedName(string name)
    {
        var builder = new System.Text.StringBuilder(name.Length);
        foreach (char c in name)
        {
            if (char.IsLetterOrDigit(c)) builder.Append(char.ToLowerInvariant(c));
            else if (c is ' ' or '-' or ':') builder.Append(' ');
        }
        return System.Text.RegularExpressions.Regex.Replace(builder.ToString(), @"\s+", " ").Trim();
    }

    /// <summary>
    /// Remove, de uma varredura, o mesmo jogo achado por fontes diferentes — mantendo a entrada da
    /// loja de maior prioridade. Também descarta itens que já existem na biblioteca sob outra loja.
    /// </summary>
    private List<GameEntry> RemoveCrossLauncherDuplicates(List<GameEntry> found)
    {
        var byName = new Dictionary<string, GameEntry>(StringComparer.Ordinal);

        foreach (var game in found)
        {
            string key = NormalizedName(game.Name);
            if (key.Length == 0) continue;

            if (!byName.TryGetValue(key, out var kept))
            {
                byName[key] = game;
                continue;
            }

            // Já temos este título nesta varredura: fica o da loja com prioridade maior.
            if (LauncherPriority(game.Launcher) > LauncherPriority(kept.Launcher))
                byName[key] = game;
        }

        var result = byName.Values.ToList();

        // E também não duplicamos o que a biblioteca já tem por outra loja.
        var existingNames = _data.Games
            .Where(g => !g.IsHidden)
            .ToDictionary(g => NormalizedName(g.Name), g => g, StringComparer.Ordinal);

        return result
            .Where(game =>
            {
                string key = NormalizedName(game.Name);
                if (!existingNames.TryGetValue(key, out var existing)) return true;
                // Mesma chave exata = é o próprio item, deixa passar para ser atualizado.
                if (existing.DedupeKey.Equals(game.DedupeKey, StringComparison.OrdinalIgnoreCase)) return true;
                // Título repetido vindo de outra loja: só entra se for de prioridade maior.
                return LauncherPriority(game.Launcher) > LauncherPriority(existing.Launcher);
            })
            .ToList();
    }

    private ScanResult Reconcile(List<GameEntry> rawFound, IReadOnlyList<string> sources)
    {
        int added = 0, updated = 0, removed = 0;

        lock (_gate)
        {
            var found = RemoveCrossLauncherDuplicates(rawFound);
            var existing = _data.Games.ToDictionary(g => g.DedupeKey, StringComparer.OrdinalIgnoreCase);
            var foundKeys = new HashSet<string>(found.Select(g => g.DedupeKey), StringComparer.OrdinalIgnoreCase);

            foreach (var game in found)
            {
                if (existing.TryGetValue(game.DedupeKey, out var current))
                {
                    // Já conhecemos este jogo: só atualizamos o que a detecção sabe melhor do que
                    // nós (caminho, pasta), preservando tudo o que é do usuário — favorito, perfil,
                    // categoria, arte escolhida à mão e tempo jogado.
                    if (!current.AutoDetected) continue;

                    bool changed = false;
                    if (current.Executable != game.Executable) { current.Executable = game.Executable; changed = true; }
                    if (current.WorkingDirectory != game.WorkingDirectory) { current.WorkingDirectory = game.WorkingDirectory; changed = true; }
                    if (current.Arguments != game.Arguments) { current.Arguments = game.Arguments; changed = true; }
                    if (string.IsNullOrEmpty(current.KnownProcessName) && !string.IsNullOrEmpty(game.KnownProcessName))
                    {
                        current.KnownProcessName = game.KnownProcessName;
                        changed = true;
                    }
                    if (changed) updated++;
                }
                else
                {
                    _data.Games.Add(game);
                    added++;
                }
            }

            // O que sumiu das fontes varridas agora e foi detectado automaticamente saiu do disco.
            // Só consideramos as origens presentes nesta varredura, para uma varredura parcial
            // (só atalhos, por exemplo) não apagar os jogos da Steam.
            var scannedKinds = found.Select(g => g.Launcher).Distinct().ToHashSet();
            if (scannedKinds.Count > 0)
            {
                removed = _data.Games.RemoveAll(g =>
                    g.AutoDetected && scannedKinds.Contains(g.Launcher) && !foundKeys.Contains(g.DedupeKey));
            }

            _data.LastScan = DateTime.Now;
        }

        Save();
        return new ScanResult(added, updated, removed, sources);
    }
}
