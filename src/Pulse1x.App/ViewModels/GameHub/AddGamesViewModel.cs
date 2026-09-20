using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pulse1x.App.Localization;
using Pulse1x.App.Models.GameHub;
using Pulse1x.App.Services.GameHub;

namespace Pulse1x.App.ViewModels.GameHub;

/// <summary>Cada caminho possível para trazer jogos para a biblioteca.</summary>
public enum AddGameMethod
{
    /// <summary>Procurar nas lojas instaladas (Steam, Epic, GOG, EA, Ubisoft).</summary>
    Stores,
    /// <summary>Varrer uma pasta do disco atrás de executáveis.</summary>
    Folder,
    /// <summary>Apontar um executável, atalho ou comando específico.</summary>
    File,
    /// <summary>Cadastrar um emulador e a pasta de ROMs.</summary>
    Emulator,
    /// <summary>Trazer os atalhos do Menu Iniciar e da Área de Trabalho.</summary>
    Shortcuts,
}

/// <summary>Uma opção do assistente, como aparece na lista da esquerda.</summary>
public partial class AddMethodOption : ObservableObject
{
    public AddGameMethod Method { get; }
    public string Icon { get; }
    public string Title { get; }
    public string Description { get; }

    public AddMethodOption(AddGameMethod method, string icon, string titleKey, string descriptionKey)
    {
        Method = method;
        Icon = icon;
        Title = Loc.S(titleKey);
        Description = Loc.S(descriptionKey);
    }
}

/// <summary>
/// Um emulador na escolha rápida. <see cref="Preset"/> nulo é a opção "outro emulador", que deixa
/// o usuário preencher tudo à mão para consoles que o catálogo ainda não cobre.
/// </summary>
public class EmulatorChoice
{
    public EmulatorPreset? Preset { get; }
    public string Label { get; }

    public EmulatorChoice(EmulatorPreset? preset)
    {
        Preset = preset;
        Label = preset?.Name ?? Loc.S("GH_EmuOther");
    }
}

/// <summary>
/// Assistente de "Adicionar jogos". Reúne num lugar só todas as formas de popular a biblioteca, em
/// vez de espalhar botões pela barra de ferramentas: escolher o método à esquerda, ajustar o que
/// for preciso à direita e confirmar.
///
/// Cada método devolve o mesmo tipo de resultado (quantos itens entraram), então a tela sempre
/// termina mostrando o que aconteceu — inclusive quando não encontra nada, que é a hora em que o
/// usuário mais precisa saber o motivo.
/// </summary>
public partial class AddGamesViewModel : ObservableObject
{
    private readonly GameLibraryService _library;
    private readonly ProfileStoreService _profiles;
    private readonly GameArtService _art;

    /// <summary>Fecha a janela. true = algo foi adicionado (a biblioteca deve recarregar).</summary>
    public event Action<bool>? CloseRequested;

    /// <summary>Seletores de arquivo/pasta, atendidos pela janela.</summary>
    public event Func<string, string, string?>? PickFileRequested;
    public event Func<string?>? PickFolderRequested;

    public ObservableCollection<AddMethodOption> Methods { get; } = new();

    [ObservableProperty] private AddMethodOption? selectedMethod;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = "";
    [ObservableProperty] private string resultMessage = "";
    [ObservableProperty] private bool hasResult;

    private bool _addedAnything;

    // ---- Método "arquivo" ----
    [ObservableProperty] private string fileName = "";
    [ObservableProperty] private string filePath = "";
    [ObservableProperty] private string fileArguments = "";
    [ObservableProperty] private string fileWorkingDirectory = "";
    [ObservableProperty] private bool fileIsApplication;

    // ---- Método "pasta" ----
    [ObservableProperty] private string folderPath = "";
    [ObservableProperty] private bool rememberFolder = true;

    // ---- Método "emulador" ----
    [ObservableProperty] private string emulatorName = "";
    [ObservableProperty] private string emulatorExecutable = "";
    [ObservableProperty] private string emulatorRoms = "";
    [ObservableProperty] private string emulatorExtensions = "";
    [ObservableProperty] private string emulatorArguments = "\"{rom}\"";
    [ObservableProperty] private string emulatorPlatform = "";

    public ObservableCollection<EmulatorEntry> ExistingEmulators { get; } = new();
    [ObservableProperty] private EmulatorEntry? selectedExistingEmulator;

    public AddGamesViewModel(GameLibraryService library, ProfileStoreService profiles, GameArtService art)
    {
        _library = library;
        _profiles = profiles;
        _art = art;

        Methods.Add(new AddMethodOption(AddGameMethod.Stores, "", "GH_AddStores", "GH_AddStoresHint"));
        Methods.Add(new AddMethodOption(AddGameMethod.Folder, "", "GH_AddFolder", "GH_AddFolderHint"));
        Methods.Add(new AddMethodOption(AddGameMethod.File, "", "GH_AddFile", "GH_AddFileHint"));
        Methods.Add(new AddMethodOption(AddGameMethod.Emulator, "", "GH_AddEmulator", "GH_AddEmulatorHint"));
        Methods.Add(new AddMethodOption(AddGameMethod.Shortcuts, "", "GH_AddShortcuts", "GH_AddShortcutsHint"));

        selectedMethod = Methods[0];

        foreach (var emulator in library.Emulators) ExistingEmulators.Add(emulator);
    }

    /// <summary>Qual painel aparece à direita (usado pelos gatilhos de visibilidade do XAML).</summary>
    public AddGameMethod CurrentMethod => SelectedMethod?.Method ?? AddGameMethod.Stores;

    partial void OnSelectedMethodChanged(AddMethodOption? value)
    {
        HasResult = false;
        StatusMessage = "";
        OnPropertyChanged(nameof(CurrentMethod));
    }

    partial void OnSelectedExistingEmulatorChanged(EmulatorEntry? value)
    {
        if (value is null) return;
        _loadingExistingEmulator = true;
        EmulatorName = value.Name;
        EmulatorExecutable = value.Executable;
        EmulatorRoms = value.RomsFolder;
        EmulatorExtensions = string.Join(", ", value.Extensions);
        EmulatorArguments = value.ArgumentsTemplate;
        EmulatorPlatform = value.Platform;

        // Reflete o emulador no seletor sem deixar o preset reescrever os ajustes salvos.
        var preset = EmulatorPresets.FindByExecutable(value.Executable);
        SelectedEmulatorChoice = EmulatorChoices.FirstOrDefault(c => c.Preset?.Name == preset?.Name)
                                 ?? EmulatorChoices.Last();
        _loadingExistingEmulator = false;
    }

    // Carregar um emulador já cadastrado não é o usuário escolhendo um executável novo: sem esta
    // trava, o preset sobrescreveria os argumentos que ele mesmo ajustou da última vez.
    private bool _loadingExistingEmulator;

    // =====================================================================================
    //  Escolha do emulador
    // =====================================================================================

    /// <summary>
    /// Emuladores de Switch oferecidos na escolha rápida, mais a opção "outro" para quem quiser
    /// cadastrar um emulador de qualquer outro console à mão.
    /// </summary>
    public IReadOnlyList<EmulatorChoice> EmulatorChoices { get; } =
        EmulatorPresets.Switch.Select(p => new EmulatorChoice(p))
            .Append(new EmulatorChoice(null))
            .ToList();

    [ObservableProperty] private EmulatorChoice? selectedEmulatorChoice;

    /// <summary>
    /// Escolher o emulador é o que dispensa o usuário de saber a linha de comando dele: o preset
    /// define extensões, argumentos e plataforma de uma vez. Cada emulador tem sua própria
    /// sintaxe — o Ryujinx aceita o caminho da ROM solto, o Eden e o Citron exigem <c>-g</c> — e
    /// errar isso faz o emulador abrir vazio em vez de carregar o jogo, sem nada na tela
    /// explicando o motivo.
    /// </summary>
    partial void OnSelectedEmulatorChoiceChanged(EmulatorChoice? value)
    {
        OnPropertyChanged(nameof(RequiresManualSetup));
        if (value?.Preset is null)
        {
            // "Outro emulador": nada a preencher, e os campos técnicos passam a ser necessários.
            ExecutableHint = "";
            DetectedEmulatorNote = "";
            return;
        }
        if (_loadingExistingEmulator) return;

        var preset = value.Preset;
        EmulatorName = preset.Name;
        EmulatorPlatform = preset.Platform;
        EmulatorExtensions = preset.Extensions;
        EmulatorArguments = preset.Arguments;

        ExecutableHint = preset.ExecutableHintKey is not null ? Loc.S(preset.ExecutableHintKey) : "";
        DetectedEmulatorNote = preset.NoteKey is not null ? Loc.S(preset.NoteKey) : "";
    }

    /// <summary>
    /// Reconhece o emulador pelo executável, para quem chegou pelo botão Procurar sem escolher
    /// antes. Só age quando ainda não há um emulador selecionado — escolhido à mão, a escolha manda.
    /// </summary>
    partial void OnEmulatorExecutableChanged(string value)
    {
        if (_loadingExistingEmulator || SelectedEmulatorChoice?.Preset is not null) return;

        var preset = EmulatorPresets.FindByExecutable(value);
        if (preset is null) return;

        var choice = EmulatorChoices.FirstOrDefault(c => c.Preset?.Name == preset.Name);
        if (choice is not null) SelectedEmulatorChoice = choice; // preenche o resto pelo preset
    }

    /// <summary>Qual arquivo escolher, quando o nome não é óbvio (o citron-cmd.exe, por exemplo).</summary>
    [ObservableProperty] private string executableHint = "";

    public bool HasExecutableHint => !string.IsNullOrEmpty(ExecutableHint);

    partial void OnExecutableHintChanged(string value) => OnPropertyChanged(nameof(HasExecutableHint));

    /// <summary>Observação do emulador escolhido, quando há alguma pegadinha conhecida.</summary>
    [ObservableProperty] private string detectedEmulatorNote = "";

    public bool HasDetectedEmulatorNote => !string.IsNullOrEmpty(DetectedEmulatorNote);

    partial void OnDetectedEmulatorNoteChanged(string value) =>
        OnPropertyChanged(nameof(HasDetectedEmulatorNote));

    /// <summary>
    /// Os campos técnicos (extensões, argumentos, plataforma) ficam recolhidos: com o emulador
    /// escolhido eles já vêm certos, e quem não conhece a sintaxe não deveria precisar encará-los.
    /// Continuam ali, editáveis, para casos que o catálogo não cobre.
    /// </summary>
    [ObservableProperty] private bool showAdvancedEmulatorFields;

    /// <summary>Com "Outro emulador", os campos técnicos são o único jeito de configurar: abrem sozinhos.</summary>
    public bool RequiresManualSetup => SelectedEmulatorChoice is not null && SelectedEmulatorChoice.Preset is null;

    // =====================================================================================
    //  Execução
    // =====================================================================================

    [RelayCommand]
    private async Task RunAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        HasResult = false;

        try
        {
            switch (CurrentMethod)
            {
                case AddGameMethod.Stores: await RunStoresAsync(); break;
                case AddGameMethod.Folder: await RunFolderAsync(); break;
                case AddGameMethod.File: RunFile(); break;
                case AddGameMethod.Emulator: await RunEmulatorAsync(); break;
                case AddGameMethod.Shortcuts: await RunShortcutsAsync(); break;
            }
        }
        catch (Exception ex)
        {
            ResultMessage = Loc.F("GH_ScanFailed", ex.Message);
            HasResult = true;
        }
        finally
        {
            IsBusy = false;
            StatusMessage = "";
        }
    }

    private async Task RunStoresAsync()
    {
        var progress = new Progress<string>(source => StatusMessage = Loc.F("GH_ScanningSource", source));
        var result = await _library.ScanStoresAsync(progress);
        ReportScan(result);
    }

    private async Task RunFolderAsync()
    {
        if (string.IsNullOrWhiteSpace(FolderPath) || !Directory.Exists(FolderPath))
        {
            ResultMessage = Loc.S("GH_FolderRequired");
            HasResult = true;
            return;
        }

        StatusMessage = Loc.F("GH_ScanningSource", Path.GetFileName(FolderPath));
        var result = await _library.ScanFolderAsync(FolderPath, RememberFolder);
        ReportScan(result);
    }

    private void RunFile()
    {
        if (string.IsNullOrWhiteSpace(FilePath))
        {
            ResultMessage = Loc.S("GH_NameAndExeRequired");
            HasResult = true;
            return;
        }

        string name = string.IsNullOrWhiteSpace(FileName)
            ? Path.GetFileNameWithoutExtension(FilePath)
            : FileName.Trim();

        var entry = new GameEntry
        {
            Name = name,
            Executable = FilePath.Trim(),
            Arguments = FileArguments.Trim(),
            WorkingDirectory = string.IsNullOrWhiteSpace(FileWorkingDirectory)
                ? Path.GetDirectoryName(FilePath) ?? ""
                : FileWorkingDirectory.Trim(),
            Launcher = FileIsApplication ? LauncherKind.Application : LauncherKind.Manual,
            AutoDetected = false,
        };

        if (!entry.Executable.Contains("://"))
            entry.KnownProcessName = Path.GetFileNameWithoutExtension(entry.Executable);

        _library.AddGame(entry);
        _ = _art.EnsureArtAsync(entry).ContinueWith(_ => _library.Save());

        _addedAnything = true;
        ResultMessage = Loc.F("GH_AddedOne", entry.Name);
        HasResult = true;

        // Limpa para permitir adicionar outro em seguida, sem fechar e reabrir.
        FileName = FilePath = FileArguments = FileWorkingDirectory = "";
    }

    private async Task RunEmulatorAsync()
    {
        if (string.IsNullOrWhiteSpace(EmulatorExecutable))
        {
            ResultMessage = Loc.S("GH_ExeRequired");
            HasResult = true;
            return;
        }

        // O nome não é mais pedido na tela: vem do emulador escolhido e, em "Outro emulador",
        // do próprio arquivo — é só o rótulo do emulador na biblioteca.
        if (string.IsNullOrWhiteSpace(EmulatorName))
            EmulatorName = Path.GetFileNameWithoutExtension(EmulatorExecutable);

        if (string.IsNullOrWhiteSpace(EmulatorRoms) || !Directory.Exists(EmulatorRoms))
        {
            ResultMessage = Loc.S("GH_RomsRequired");
            HasResult = true;
            return;
        }

        var emulator = SelectedExistingEmulator ?? new EmulatorEntry();
        emulator.Name = EmulatorName.Trim();
        emulator.Executable = EmulatorExecutable.Trim();
        emulator.Directory = Path.GetDirectoryName(EmulatorExecutable) ?? "";
        emulator.RomsFolder = EmulatorRoms.Trim();
        emulator.Extensions = EmulatorExtensions
            .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.Trim().TrimStart('.').ToLowerInvariant())
            .Where(e => e.Length > 0)
            .Distinct()
            .ToList();
        emulator.ArgumentsTemplate = string.IsNullOrWhiteSpace(EmulatorArguments) ? "\"{rom}\"" : EmulatorArguments.Trim();
        emulator.Platform = EmulatorPlatform.Trim();

        if (emulator.Extensions.Count == 0)
        {
            ResultMessage = Loc.S("GH_ExtensionsRequired");
            HasResult = true;
            return;
        }

        _library.AddOrUpdateEmulator(emulator);

        StatusMessage = Loc.F("GH_ScanningSource", emulator.Name);
        var result = await _library.ScanEmulatorAsync(emulator);
        ReportScan(result);

        if (!ExistingEmulators.Contains(emulator)) ExistingEmulators.Add(emulator);
    }

    private async Task RunShortcutsAsync()
    {
        StatusMessage = Loc.S("GH_Scanning");
        var result = await _library.ScanShortcutsAsync();
        ReportScan(result);
    }

    private void ReportScan(ScanResult result)
    {
        if (result.Added > 0) _addedAnything = true;

        ResultMessage = result.Added == 0 && result.Updated == 0
            ? Loc.S("GH_ScanNothing")
            : Loc.F("GH_ScanResult", result.Added, result.Updated, result.Removed);
        HasResult = true;
    }

    // =====================================================================================
    //  Seletores
    // =====================================================================================

    [RelayCommand]
    private void BrowseFile()
    {
        string? file = PickFileRequested?.Invoke(
            Loc.S("GH_PickExecutable"),
            "Executáveis e atalhos (*.exe;*.lnk;*.url;*.bat;*.cmd)|*.exe;*.lnk;*.url;*.bat;*.cmd|Todos os arquivos (*.*)|*.*");
        if (file is null) return;

        // Um atalho é resolvido para o programa real, para a detecção do processo e a arte
        // funcionarem — senão apontaríamos para o .lnk.
        if (file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var (target, args, workDir) = ShortcutScanner.ResolveShortcut(file);
            if (target is not null)
            {
                FilePath = target;
                if (!string.IsNullOrWhiteSpace(args)) FileArguments = args;
                FileWorkingDirectory = workDir ?? Path.GetDirectoryName(target) ?? "";
                if (string.IsNullOrWhiteSpace(FileName)) FileName = Path.GetFileNameWithoutExtension(file);
                return;
            }
        }

        FilePath = file;
        if (string.IsNullOrWhiteSpace(FileName)) FileName = Path.GetFileNameWithoutExtension(file);
        if (string.IsNullOrWhiteSpace(FileWorkingDirectory))
            FileWorkingDirectory = Path.GetDirectoryName(file) ?? "";
    }

    [RelayCommand]
    private void BrowseFolder()
    {
        string? folder = PickFolderRequested?.Invoke();
        if (folder is not null) FolderPath = folder;
    }

    [RelayCommand]
    private void BrowseWorkingDirectory()
    {
        string? folder = PickFolderRequested?.Invoke();
        if (folder is not null) FileWorkingDirectory = folder;
    }

    [RelayCommand]
    private void BrowseEmulator()
    {
        string? file = PickFileRequested?.Invoke(Loc.S("GH_PickEmulator"),
            "Executáveis (*.exe)|*.exe|Todos os arquivos (*.*)|*.*");
        if (file is null) return;
        EmulatorExecutable = file;
        if (string.IsNullOrWhiteSpace(EmulatorName)) EmulatorName = Path.GetFileNameWithoutExtension(file);
    }

    [RelayCommand]
    private void BrowseRoms()
    {
        string? folder = PickFolderRequested?.Invoke();
        if (folder is not null) EmulatorRoms = folder;
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(_addedAnything);
}
