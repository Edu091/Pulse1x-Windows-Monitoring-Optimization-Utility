using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pulse1x.App.Localization;
using Pulse1x.App.Models.GameHub;
using Pulse1x.App.Services.GameHub;

namespace Pulse1x.App.ViewModels.GameHub;

/// <summary>
/// Adiciona ou edita um item da biblioteca à mão: jogo, aplicativo, atalho ou executável avulso.
/// É o caminho para tudo o que a detecção automática não encontra — e, por ser o mesmo modelo, um
/// item criado aqui usa os mesmos perfis, a mesma arte e o mesmo botão Jogar.
/// </summary>
public partial class GameEditorViewModel : ObservableObject
{
    private readonly GameLibraryService _library;
    private readonly GameArtService _art;
    private readonly GameEntry _entry;
    private readonly bool _isNew;

    public event Action<bool>? CloseRequested;

    /// <summary>Pedidos de seleção de arquivo/pasta, atendidos pelo code-behind da janela.</summary>
    public event Func<string, string, string?>? PickFileRequested;
    public event Func<string?>? PickFolderRequested;

    [ObservableProperty] private string name = "";
    [ObservableProperty] private string executable = "";
    [ObservableProperty] private string arguments = "";
    [ObservableProperty] private string workingDirectory = "";
    [ObservableProperty] private string category = "";
    [ObservableProperty] private bool isFavorite;
    [ObservableProperty] private bool isApplication;
    [ObservableProperty] private GameProfile? selectedProfile;
    [ObservableProperty] private string statusMessage = "";

    public ObservableCollection<GameProfile> Profiles { get; } = new();
    public ObservableCollection<string> Categories { get; } = new();

    public bool IsNew => _isNew;
    public string Title => _isNew ? Loc.S("GH_AddGameTitle") : Loc.S("GH_EditGameTitle");

    public GameEditorViewModel(GameEntry? entry, GameLibraryService library, ProfileStoreService profiles, GameArtService art)
    {
        _library = library;
        _art = art;
        _isNew = entry is null;
        _entry = entry ?? new GameEntry { Launcher = LauncherKind.Manual };

        name = _entry.Name;
        executable = _entry.Executable;
        arguments = _entry.Arguments;
        workingDirectory = _entry.WorkingDirectory;
        category = _entry.Category;
        isFavorite = _entry.IsFavorite;
        isApplication = _entry.Launcher == LauncherKind.Application;

        Profiles.Add(new GameProfile { Id = "", Name = Loc.S("GH_NoProfile") });
        foreach (var profile in profiles.Profiles) Profiles.Add(profile);
        selectedProfile = Profiles.FirstOrDefault(p => p.Id == _entry.ProfileId) ?? Profiles[0];

        foreach (var item in library.Categories) Categories.Add(item);
    }

    [RelayCommand]
    private void BrowseExecutable()
    {
        string? file = PickFileRequested?.Invoke(
            Loc.S("GH_PickExecutable"),
            "Executáveis (*.exe;*.lnk;*.bat;*.cmd)|*.exe;*.lnk;*.bat;*.cmd|Todos os arquivos (*.*)|*.*");
        if (file is null) return;

        // Um atalho é resolvido para o programa real: assim a detecção do processo e a arte
        // funcionam, em vez de apontarem para o .lnk.
        if (file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var (target, args, workDir) = ShortcutScanner.ResolveShortcut(file);
            if (target is not null)
            {
                Executable = target;
                if (!string.IsNullOrWhiteSpace(args)) Arguments = args;
                WorkingDirectory = workDir ?? Path.GetDirectoryName(target) ?? "";
                if (string.IsNullOrWhiteSpace(Name)) Name = Path.GetFileNameWithoutExtension(file);
                return;
            }
        }

        Executable = file;
        if (string.IsNullOrWhiteSpace(WorkingDirectory))
            WorkingDirectory = Path.GetDirectoryName(file) ?? "";
        if (string.IsNullOrWhiteSpace(Name))
            Name = Path.GetFileNameWithoutExtension(file);
    }

    [RelayCommand]
    private void BrowseWorkingDirectory()
    {
        string? folder = PickFolderRequested?.Invoke();
        if (folder is not null) WorkingDirectory = folder;
    }

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Executable))
        {
            StatusMessage = Loc.S("GH_NameAndExeRequired");
            return;
        }

        _entry.Name = Name.Trim();
        _entry.Executable = Executable.Trim();
        _entry.Arguments = Arguments.Trim();
        _entry.WorkingDirectory = WorkingDirectory.Trim();
        _entry.Category = Category.Trim();
        _entry.IsFavorite = IsFavorite;
        _entry.ProfileId = string.IsNullOrEmpty(SelectedProfile?.Id) ? null : SelectedProfile!.Id;

        // Um item editado à mão deixa de ser "detectado": a partir daqui a varredura não mexe nele.
        if (_entry.Launcher is LauncherKind.Manual or LauncherKind.Application or LauncherKind.Shortcut)
        {
            _entry.Launcher = IsApplication ? LauncherKind.Application : LauncherKind.Manual;
            _entry.AutoDetected = false;
        }

        if (string.IsNullOrEmpty(_entry.KnownProcessName) && !_entry.Executable.Contains("://"))
            _entry.KnownProcessName = Path.GetFileNameWithoutExtension(_entry.Executable);

        if (!string.IsNullOrWhiteSpace(_entry.Category)) _library.AddCategory(_entry.Category);

        if (_isNew) _library.AddGame(_entry);
        else _library.UpdateGame(_entry);

        _ = _art.EnsureArtAsync(_entry).ContinueWith(_ => _library.Save());

        CloseRequested?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);

    /// <summary>Descarta a arte atual para que a busca automática rode de novo neste item.</summary>
    [RelayCommand]
    private void ResetArt()
    {
        _art.ResetArt(_entry);
        _library.UpdateGame(_entry);
        _ = _art.EnsureArtAsync(_entry).ContinueWith(_ => _library.Save());
        StatusMessage = Loc.S("GH_ArtReset");
    }
}

/// <summary>
/// Cadastro de emuladores. Depois de informar o executável, a pasta de ROMs e as extensões, a
/// varredura transforma cada ROM num item comum da biblioteca — com capa, nome e botão Jogar.
/// </summary>
public partial class EmulatorEditorViewModel : ObservableObject
{
    private readonly GameLibraryService _library;

    public event Action<bool>? CloseRequested;
    public event Func<string, string, string?>? PickFileRequested;
    public event Func<string?>? PickFolderRequested;

    public ObservableCollection<EmulatorEntry> Emulators { get; } = new();
    public ObservableCollection<GameProfile> Profiles { get; } = new();

    [ObservableProperty] private EmulatorEntry? selected;
    [ObservableProperty] private string name = "";
    [ObservableProperty] private string executable = "";
    [ObservableProperty] private string directory = "";
    [ObservableProperty] private string romsFolder = "";
    [ObservableProperty] private string extensions = "";
    [ObservableProperty] private string argumentsTemplate = "\"{rom}\"";
    [ObservableProperty] private string platform = "";
    [ObservableProperty] private bool scanEnabled = true;
    [ObservableProperty] private GameProfile? defaultProfile;
    [ObservableProperty] private string statusMessage = "";

    public EmulatorEditorViewModel(GameLibraryService library, ProfileStoreService profiles)
    {
        _library = library;

        foreach (var emulator in library.Emulators) Emulators.Add(emulator);

        Profiles.Add(new GameProfile { Id = "", Name = Loc.S("GH_NoProfile") });
        foreach (var profile in profiles.Profiles) Profiles.Add(profile);
        defaultProfile = Profiles[0];

        Selected = Emulators.FirstOrDefault();
    }

    partial void OnSelectedChanged(EmulatorEntry? value)
    {
        if (value is null) { NewEmulator(); return; }

        Name = value.Name;
        Executable = value.Executable;
        Directory = value.Directory;
        RomsFolder = value.RomsFolder;
        Extensions = string.Join(", ", value.Extensions);
        ArgumentsTemplate = value.ArgumentsTemplate;
        Platform = value.Platform;
        ScanEnabled = value.ScanEnabled;
        DefaultProfile = Profiles.FirstOrDefault(p => p.Id == value.DefaultProfileId) ?? Profiles[0];
    }

    [RelayCommand]
    private void NewEmulator()
    {
        Selected = null;
        Name = "";
        Executable = "";
        Directory = "";
        RomsFolder = "";
        Extensions = "";
        ArgumentsTemplate = "\"{rom}\"";
        Platform = "";
        ScanEnabled = true;
        DefaultProfile = Profiles.FirstOrDefault();
    }

    [RelayCommand]
    private void BrowseExecutable()
    {
        string? file = PickFileRequested?.Invoke(Loc.S("GH_PickEmulator"), "Executáveis (*.exe)|*.exe|Todos os arquivos (*.*)|*.*");
        if (file is null) return;
        Executable = file;
        if (string.IsNullOrWhiteSpace(Directory)) Directory = Path.GetDirectoryName(file) ?? "";
        if (string.IsNullOrWhiteSpace(Name)) Name = Path.GetFileNameWithoutExtension(file);
    }

    [RelayCommand]
    private void BrowseRoms()
    {
        string? folder = PickFolderRequested?.Invoke();
        if (folder is not null) RomsFolder = folder;
    }

    [RelayCommand]
    private void BrowseDirectory()
    {
        string? folder = PickFolderRequested?.Invoke();
        if (folder is not null) Directory = folder;
    }

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Executable))
        {
            StatusMessage = Loc.S("GH_NameAndExeRequired");
            return;
        }

        var entry = Selected ?? new EmulatorEntry();
        entry.Name = Name.Trim();
        entry.Executable = Executable.Trim();
        entry.Directory = Directory.Trim();
        entry.RomsFolder = RomsFolder.Trim();
        entry.Extensions = Extensions
            .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.Trim().TrimStart('.').ToLowerInvariant())
            .Where(e => e.Length > 0)
            .Distinct()
            .ToList();
        entry.ArgumentsTemplate = string.IsNullOrWhiteSpace(ArgumentsTemplate) ? "\"{rom}\"" : ArgumentsTemplate.Trim();
        entry.Platform = Platform.Trim();
        entry.ScanEnabled = ScanEnabled;
        entry.DefaultProfileId = string.IsNullOrEmpty(DefaultProfile?.Id) ? null : DefaultProfile!.Id;

        _library.AddOrUpdateEmulator(entry);

        if (!Emulators.Contains(entry)) Emulators.Add(entry);
        Selected = entry;
        StatusMessage = Loc.F("GH_EmulatorSaved", entry.Name);
    }

    [RelayCommand]
    private void Delete()
    {
        if (Selected is null) return;

        var confirm = System.Windows.MessageBox.Show(
            Loc.F("GH_EmulatorRemoveConfirm", Selected.Name),
            Loc.S("GH_EmulatorsTitle"),
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);
        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        _library.RemoveEmulator(Selected.Id);
        Emulators.Remove(Selected);
        NewEmulator();
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(true);
}
