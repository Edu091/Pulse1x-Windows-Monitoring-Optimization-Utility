namespace Pulse1x.App.Models.GameHub;

/// <summary>
/// Um emulador conhecido: o que ele roda, com que argumentos e por qual processo atende.
/// </summary>
public record EmulatorPreset(
    string Name,
    string Platform,
    /// <summary>Extensões de ROM, sem ponto.</summary>
    string Extensions,
    /// <summary>Argumentos com <c>{rom}</c> no lugar do caminho da ROM.</summary>
    string Arguments,
    /// <summary>Nomes de executável que identificam este emulador (sem .exe, minúsculo).</summary>
    string[] ExecutableNames,
    /// <summary>
    /// Processo que realmente fica de pé com o jogo, quando é diferente do executável chamado.
    /// Sem isso, o Pulse1x aplicaria prioridade ao processo errado e não perceberia o jogo fechar.
    /// </summary>
    string? MainProcessName = null,
    /// <summary>Observação mostrada ao usuário quando o preset é reconhecido.</summary>
    string? NoteKey = null,
    /// <summary>
    /// Nome do arquivo que deve ser escolhido, quando não é óbvio — o Citron é iniciado pelo
    /// citron-cmd.exe, e apontar para o citron.exe faz o emulador abrir sem carregar a ROM.
    /// </summary>
    string? ExecutableHintKey = null);

/// <summary>
/// Catálogo de emuladores conhecidos, usado para preencher o formulário de cadastro sozinho.
///
/// O cadastro continua inteiramente manual — todo campo pode ser editado depois. O catálogo só
/// evita que o usuário tenha que descobrir na tentativa e erro qual é a sintaxe de linha de
/// comando de cada emulador, que varia mais do que se imagina: uns aceitam o caminho solto,
/// outros exigem <c>-g</c>, e há os que têm um executável separado só para a linha de comando.
///
/// Os argumentos aqui vêm da documentação de cada projeto (ver os comentários por entrada). Onde
/// o emulador aceita as duas formas, ficou a mais explícita, que é a que não depende de o
/// emulador adivinhar que o argumento solto é um jogo.
/// </summary>
public static class EmulatorPresets
{
    // Extensões da família Switch: os três primeiros são os formatos de cartucho/loja; nro e nca
    // aparecem em homebrew e conteúdo solto.
    private const string SwitchExtensions = "nsp, xci, nca, nro, nsz, xcz";

    public static readonly EmulatorPreset[] All =
    {
        // ---------- Nintendo Switch ----------

        // Ryujinx (e o fork Ryubing, que herdou o nome do executável): aceita o caminho da ROM
        // como argumento solto. O GTK antigo (Ryujinx.Ava.exe) continua na lista porque ainda
        // circula em instalações mais velhas.
        new("Ryujinx", "Nintendo Switch", SwitchExtensions, "\"{rom}\"",
            new[] { "ryujinx", "ryujinx.ava", "ryujinxlauncher" },
            ExecutableHintKey: "GH_EmuHintRyujinx"),

        // Eden: o Qt aceita tanto o caminho solto quanto -g; usamos -g por ser explícito.
        // O eden-cli é o SDL, com as mesmas flags.
        new("Eden", "Nintendo Switch", SwitchExtensions, "-g \"{rom}\"",
            new[] { "eden", "eden-cli" },
            ExecutableHintKey: "GH_EmuHintEden"),

        // Citron: quem entende linha de comando é o citron-cmd, não o executável da interface.
        // Apontar para o citron.exe aqui costuma resultar no emulador abrindo sem carregar nada.
        new("Citron", "Nintendo Switch", SwitchExtensions, "-g \"{rom}\"",
            new[] { "citron", "citron-cmd" }, MainProcessName: "citron",
            NoteKey: "GH_EmuNoteCitron", ExecutableHintKey: "GH_EmuHintCitron"),

        // Forks do Yuzu (Suyu, Sudachi e afins): mantiveram a CLI do Yuzu original — yuzu.exe -g rom.
        new("Yuzu (e forks)", "Nintendo Switch", SwitchExtensions, "-g \"{rom}\"",
            new[] { "yuzu", "suyu", "sudachi", "yuzu-cmd" },
            ExecutableHintKey: "GH_EmuHintYuzu"),

        // ---------- Outros consoles ----------

        new("RetroArch", "Multi-plataforma", "nes, sfc, smc, gba, gb, gbc, n64, z64, md, gen, iso, cue, chd, zip",
            "-L \"{core}\" \"{rom}\"", new[] { "retroarch" }, NoteKey: "GH_EmuNoteRetroArch"),

        new("Dolphin", "GameCube / Wii", "iso, gcm, wbfs, rvz, ciso, gcz, wad",
            "-b -e \"{rom}\"", new[] { "dolphin", "dolphinwx" }),

        // PCSX2: o "--" separa os parâmetros do caminho do jogo, e o -batch encerra o emulador
        // junto com o jogo. Sem o -batch o PCSX2 volta para a própria interface ao sair da
        // partida, e o GameHub continuaria achando que a sessão está em andamento.
        new("PCSX2", "PlayStation 2", "iso, chd, cso, gz, bin, mdf, iso.gz, nrg",
            "-batch -- \"{rom}\"", new[] { "pcsx2-qt", "pcsx2-qtx64", "pcsx2", "pcsx2x64" },
            ExecutableHintKey: "GH_EmuHintPcsx2"),

        new("RPCS3", "PlayStation 3", "bin, iso, pkg, elf, self",
            "--no-gui \"{rom}\"", new[] { "rpcs3" }),

        // PPSSPP aceita o caminho do jogo solto. O --escape-exit deixa o Esc fechar o emulador,
        // que num setup de sofá é o que devolve o controle ao GameHub sem teclado.
        new("PPSSPP", "PSP", "iso, cso, pbp, elf, chd, prx",
            "\"{rom}\" --escape-exit", new[] { "ppssppwindows64", "ppssppwindows", "ppsspp" },
            ExecutableHintKey: "GH_EmuHintPpsspp"),

        new("Cemu", "Wii U", "wud, wux, wua, rpx, iso",
            "-g \"{rom}\"", new[] { "cemu" }),

        new("Citra (e forks)", "Nintendo 3DS", "3ds, cci, cxi, cia, app, 3dsx",
            "\"{rom}\"", new[] { "citra", "citra-qt", "lime", "lime-qt", "azahar" }),

        new("DuckStation", "PlayStation 1", "cue, bin, chd, iso, img, ecm, pbp",
            "-- \"{rom}\"", new[] { "duckstation-qt-x64", "duckstation", "duckstation-nogui-x64" }),

        new("xemu", "Xbox", "iso, xiso",
            "-dvd_path \"{rom}\"", new[] { "xemu" }),

        new("Vita3K", "PlayStation Vita", "vpk, zip",
            "-r \"{rom}\"", new[] { "vita3k" }),

        new("melonDS", "Nintendo DS", "nds, srl, dsi",
            "\"{rom}\"", new[] { "melonds" }),
    };

    /// <summary>
    /// Emuladores oferecidos na escolha rápida do cadastro, na ordem em que aparecem. É a lista
    /// que dispensa o usuário de saber a sintaxe de linha de comando: escolher aqui já define
    /// extensões, argumentos e plataforma.
    ///
    /// Os demais do catálogo continuam sendo reconhecidos pelo executável, em "Outro emulador" —
    /// esta lista é só o que aparece pronto para escolher.
    /// </summary>
    public static IReadOnlyList<EmulatorPreset> Featured { get; } =
        new[] { "Ryujinx", "Eden", "Citron", "Yuzu (e forks)", "PCSX2", "PPSSPP" }
            .Select(name => All.First(p => p.Name == name))
            .ToList();

    /// <summary>
    /// Procura o preset correspondente a um executável. A comparação é pelo nome do arquivo, sem
    /// extensão nem caminho, então funciona igual para uma instalação portátil ou no Program Files.
    /// </summary>
    public static EmulatorPreset? FindByExecutable(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return null;

        string name;
        try { name = System.IO.Path.GetFileNameWithoutExtension(executablePath).ToLowerInvariant(); }
        catch { return null; }
        if (name.Length == 0) return null;

        // Nome exato primeiro: "citron-cmd" precisa vencer "citron", e "pcsx2-qt" vencer "pcsx2".
        foreach (var preset in All)
            if (preset.ExecutableNames.Contains(name))
                return preset;

        // Builds trazem sufixos de versão e arquitetura ("Ryujinx-1.2.3-win_x64", "eden_v0.1").
        // Aqui o nome do executável precisa COMEÇAR pelo nome conhecido, para que um prefixo curto
        // não capture um emulador diferente que apenas contenha aquelas letras.
        foreach (var preset in All)
            foreach (var known in preset.ExecutableNames)
                if (name.StartsWith(known, StringComparison.Ordinal) &&
                    (name.Length == known.Length || !char.IsLetter(name[known.Length])))
                    return preset;

        return null;
    }
}
