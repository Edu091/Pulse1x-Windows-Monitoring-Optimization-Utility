using System.Text.Json.Serialization;

namespace Pulse1x.App.Models.GameHub;

/// <summary>Origem de um item da biblioteca — define o ícone/rótulo da plataforma e como ele é iniciado.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LauncherKind
{
    /// <summary>Executável adicionado à mão pelo usuário.</summary>
    Manual,
    Steam,
    Epic,
    Gog,
    /// <summary>ROM executada por um emulador cadastrado.</summary>
    Emulator,
    /// <summary>Atalho (.lnk/.url) do Menu Iniciar ou da Área de Trabalho.</summary>
    Shortcut,
    /// <summary>Aplicativo comum (Premiere, VS Code, Blender...) — usa o mesmo motor de perfis.</summary>
    Application,
    /// <summary>EA App / Origin.</summary>
    Ea,
    /// <summary>Ubisoft Connect.</summary>
    Ubisoft,
}

/// <summary>Critério de ordenação da biblioteca.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GameSortMode { Name, LastPlayed, Playtime, Added, Launcher }

/// <summary>
/// Um item da biblioteca do GameHub: jogo, ROM ou aplicativo. É o registro persistido em
/// <c>gamehub-library.json</c> — tudo o que o GameHub precisa para exibir, iniciar e medir o uso.
/// </summary>
public class GameEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "";

    /// <summary>Executável ou comando de inicialização (pode ser uma URI como <c>steam://rungameid/...</c>).</summary>
    public string Executable { get; set; } = "";

    public string Arguments { get; set; } = "";

    public string WorkingDirectory { get; set; } = "";

    public LauncherKind Launcher { get; set; } = LauncherKind.Manual;

    /// <summary>Id do jogo dentro do launcher de origem (AppId da Steam, CatalogItemId da Epic...).</summary>
    public string? LauncherAppId { get; set; }

    // ---- Arte (caminhos em disco, dentro do cache do GameHub ou escolhidos pelo usuário) ----
    public string? CoverPath { get; set; }
    public string? HeroPath { get; set; }
    public string? IconPath { get; set; }

    /// <summary>Marcado quando o usuário troca a arte à mão — impede que a busca automática sobrescreva.</summary>
    public bool ArtLockedByUser { get; set; }

    /// <summary>AppId da Steam descoberto pela busca online por nome; evita repetir a busca.</summary>
    public string? OnlineArtAppId { get; set; }

    // ---- Uso ----
    public DateTime? LastPlayed { get; set; }
    public long TotalMinutesPlayed { get; set; }
    public int LaunchCount { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.Now;

    // ---- Organização ----
    public bool IsFavorite { get; set; }
    public bool IsHidden { get; set; }
    public string Category { get; set; } = "";

    /// <summary>Perfil de sistema associado (null = nenhum; o GameHub não altera nada ao iniciar).</summary>
    public string? ProfileId { get; set; }

    // ---- Emulação ----
    /// <summary>Emulador que executa esta ROM (quando <see cref="Launcher"/> é <see cref="LauncherKind.Emulator"/>).</summary>
    public string? EmulatorId { get; set; }
    public string? RomPath { get; set; }

    /// <summary>Nome do processo principal observado na última execução — acelera a detecção nas próximas.</summary>
    public string? KnownProcessName { get; set; }

    /// <summary>Detectado automaticamente por um scanner (não foi criado à mão). Itens detectados são
    /// atualizados/removidos por nova varredura; itens manuais nunca são tocados.</summary>
    public bool AutoDetected { get; set; }

    /// <summary>Chave estável usada para reconhecer o mesmo jogo entre varreduras e evitar duplicatas.</summary>
    [JsonIgnore]
    public string DedupeKey => Launcher switch
    {
        LauncherKind.Steam or LauncherKind.Epic or LauncherKind.Gog when !string.IsNullOrEmpty(LauncherAppId)
            => $"{Launcher}:{LauncherAppId}",
        LauncherKind.Emulator => $"emu:{EmulatorId}:{(RomPath ?? "").ToLowerInvariant()}",
        _ => $"exe:{(Executable ?? "").ToLowerInvariant()}|{(Arguments ?? "").ToLowerInvariant()}",
    };

    /// <summary>Tempo jogado formatado para a interface ("4 h 20 min" / "35 min").</summary>
    [JsonIgnore]
    public string PlaytimeText
    {
        get
        {
            if (TotalMinutesPlayed <= 0) return Localization.Loc.S("GH_NeverPlayed");
            long h = TotalMinutesPlayed / 60, m = TotalMinutesPlayed % 60;
            return h > 0 ? $"{h} h {m} min" : $"{m} min";
        }
    }
}

/// <summary>
/// Um emulador cadastrado. A biblioteca varre <see cref="RomsFolder"/> pelas extensões conhecidas e
/// cria um <see cref="GameEntry"/> por ROM, iniciado com <see cref="ArgumentsTemplate"/>.
/// </summary>
public class EmulatorEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";

    /// <summary>Executável do emulador.</summary>
    public string Executable { get; set; } = "";

    /// <summary>Pasta do emulador (diretório de trabalho ao iniciar).</summary>
    public string Directory { get; set; } = "";

    /// <summary>Pasta varrida em busca de ROMs (inclui subpastas).</summary>
    public string RomsFolder { get; set; } = "";

    /// <summary>Extensões reconhecidas, sem ponto ("nes", "sfc", "iso"...).</summary>
    public List<string> Extensions { get; set; } = new();

    /// <summary>Argumentos para iniciar uma ROM; <c>{rom}</c> é trocado pelo caminho completo (com aspas).</summary>
    public string ArgumentsTemplate { get; set; } = "\"{rom}\"";

    /// <summary>Plataforma exibida na biblioteca (ex.: "Super Nintendo").</summary>
    public string Platform { get; set; } = "";

    /// <summary>Perfil aplicado por padrão às ROMs deste emulador.</summary>
    public string? DefaultProfileId { get; set; }

    public bool ScanEnabled { get; set; } = true;
}

/// <summary>Raiz persistida da biblioteca do GameHub (<c>gamehub-library.json</c>).</summary>
public class GameLibraryData
{
    public List<GameEntry> Games { get; set; } = new();
    public List<EmulatorEntry> Emulators { get; set; } = new();
    public List<string> Categories { get; set; } = new();

    /// <summary>Pastas extras varridas em busca de executáveis avulsos.</summary>
    public List<string> CustomScanFolders { get; set; } = new();

    public DateTime? LastScan { get; set; }
}
