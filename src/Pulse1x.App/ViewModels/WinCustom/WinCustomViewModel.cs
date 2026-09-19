using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pulse1x.App.Localization;
using Pulse1x.App.Models.WinCustom;
using Pulse1x.App.Services;
using Pulse1x.App.Services.WinCustom;

namespace Pulse1x.App.ViewModels.WinCustom;

/// <summary>Um componente na lista lateral da seção (Barra de tarefas, Menu Iniciar...).</summary>
public partial class ComponentTab : ObservableObject
{
    public WinComponent Component { get; }
    public string Icon { get; }

    /// <summary>Editores das regiões deste componente. A taskbar tem um só; o Explorer, vários.</summary>
    public ObservableCollection<AppearanceEditorViewModel> Editors { get; } = new();

    /// <summary>Componente inteiro depende de injeção — a interface marca com o selo BETA.</summary>
    [ObservableProperty] private bool isBeta;

    public ComponentTab(WinComponent component, string icon)
    {
        Component = component;
        Icon = icon;
    }

    public string Title => Loc.S("WinCustom_Component_" + Component);

    public void RefreshLabels()
    {
        OnPropertyChanged(nameof(Title));
        foreach (var editor in Editors) editor.RefreshLabels();
    }
}

/// <summary>
/// ViewModel da seção Personalização do Windows: biblioteca de temas, editores por componente e
/// região, sincronização, status e todas as ações de restauração.
/// </summary>
public partial class WinCustomViewModel : ObservableObject
{
    private readonly WinCustomizationEngine _engine;
    private readonly CustomizationHostService _host;
    private readonly SettingsService _settings;

    /// <summary>Evita reaplicar o tema a cada propriedade durante a montagem dos editores.</summary>
    private bool _loading = true;

    public WinCustomViewModel(
        WinCustomizationEngine engine,
        CustomizationHostService host,
        SettingsService settings)
    {
        _engine = engine;
        _host = host;
        _settings = settings;

        var prefs = settings.Current.WinCustom;
        enabled = prefs.Enabled;
        startWithWindows = prefs.StartWithWindows;
        restoreLastTheme = prefs.RestoreLastTheme;
        reapplyOnExplorerRestart = prefs.ReapplyOnExplorerRestart;
        enableInjection = prefs.EnableInjection;
        syncAppearance = prefs.SyncAppearance;

        BuildComponentTabs();
        ReloadThemes();

        _engine.StateChanged += OnEngineStateChanged;
        _engine.Watchdog.TargetQuarantined += OnTargetQuarantined;
        Loc.Instance.LanguageChanged += RefreshLabels;

        _loading = false;
        RefreshStatus();
    }

    // =====================================================================================
    //  Biblioteca de temas
    // =====================================================================================

    public ObservableCollection<WinTheme> Themes { get; } = new();

    [ObservableProperty] private WinTheme? selectedTheme;

    [ObservableProperty] private string statusText = "";
    [ObservableProperty] private string stateText = "";
    [ObservableProperty] private string activeThemeName = "";
    [ObservableProperty] private string hostStatusText = "";
    [ObservableProperty] private string startupStatusText = "";
    [ObservableProperty] private string compatibilityText = "";
    [ObservableProperty] private bool hasQuarantine;
    [ObservableProperty] private string quarantineText = "";

    private void ReloadThemes()
    {
        Themes.Clear();
        foreach (var theme in _engine.Store.Themes) Themes.Add(theme);

        var active = _engine.ActiveTheme;
        selectedTheme = active ?? Themes.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedTheme));

        BindEditorsToTheme();
    }

    partial void OnSelectedThemeChanged(WinTheme? value)
    {
        if (_loading || value is null) return;
        BindEditorsToTheme();
        OnPropertyChanged(nameof(CanEditSelectedTheme));

        // Selecionar um tema NÃO o aplica — é só uma pré-visualização. O status precisa ser
        // reavaliado mesmo assim, senão continuaria mostrando o tema anterior como "atual"
        // enquanto a lista já aponta para outro.
        RefreshStatus();
    }

    /// <summary>Presets de fábrica são somente leitura — a interface oferece duplicar.</summary>
    public bool CanEditSelectedTheme => SelectedTheme is { IsBuiltIn: false };

    // =====================================================================================
    //  Componentes e regiões
    // =====================================================================================

    public ObservableCollection<ComponentTab> Components { get; } = new();

    [ObservableProperty] private ComponentTab? selectedComponent;

    /// <summary>
    /// Monta as abas e, dentro de cada uma, um editor por região. As regiões do Explorer, do
    /// Menu Iniciar e das Configurações existem aqui mesmo sem o motor de injeção: elas apenas
    /// aparecem desabilitadas, com o motivo — que é o comportamento pedido para incompatibilidade.
    /// </summary>
    private void BuildComponentTabs()
    {
        Components.Clear();

        Components.Add(new ComponentTab(WinComponent.Taskbar, "Window24"));
        Components.Add(new ComponentTab(WinComponent.StartMenu, "Grid24") { IsBeta = true });
        Components.Add(new ComponentTab(WinComponent.Explorer, "Folder24"));
        Components.Add(new ComponentTab(WinComponent.Settings, "Settings24") { IsBeta = true });

        selectedComponent = Components[0];
    }

    /// <summary>
    /// Liga os editores ao tema selecionado. Recriamos os editores a cada troca de tema porque
    /// eles editam DIRETAMENTE os objetos do tema — é isso que faz cada tema guardar sua própria
    /// combinação de estilos por região.
    /// </summary>
    private void BindEditorsToTheme()
    {
        var theme = SelectedTheme;
        if (theme is null) return;

        var caps = _engine.Capabilities;
        caps.InjectionActive = EnableInjection && InjectionHostAvailable;

        foreach (var tab in Components) tab.Editors.Clear();

        // ---- Barra de tarefas: um único alvo ----
        var taskbarTab = Components.First(c => c.Component == WinComponent.Taskbar);
        taskbarTab.Editors.Add(NewEditor(
            "Taskbar", "WinCustom_Region_Taskbar",
            WinTheme.Get(theme.Components, WinComponent.Taskbar),
            kind => caps.ForTaskbar(kind)));

        // ---- Explorer: uma região por área, estilos independentes ----
        var explorerTab = Components.First(c => c.Component == WinComponent.Explorer);
        foreach (var region in Enum.GetValues<ExplorerRegion>())
        {
            var captured = region;
            explorerTab.Editors.Add(NewEditor(
                $"Explorer.{region}", "WinCustom_ExplorerRegion_" + region,
                WinTheme.Get(theme.Explorer, region),
                kind => caps.ForExplorer(captured, kind)));
        }

        // ---- Menu Iniciar ----
        var startTab = Components.First(c => c.Component == WinComponent.StartMenu);
        foreach (var region in Enum.GetValues<StartRegion>())
        {
            var captured = region;
            startTab.Editors.Add(NewEditor(
                $"Start.{region}", "WinCustom_StartRegion_" + region,
                WinTheme.Get(theme.Start, region),
                kind => caps.ForStart(captured, kind)));
        }

        // ---- Configurações do Windows ----
        var settingsTab = Components.First(c => c.Component == WinComponent.Settings);
        foreach (var region in Enum.GetValues<SettingsRegion>())
        {
            var captured = region;
            settingsTab.Editors.Add(NewEditor(
                $"Settings.{region}", "WinCustom_SettingsRegion_" + region,
                WinTheme.Get(theme.Settings, region),
                kind => caps.ForSettings(captured, kind)));
        }
    }

    private AppearanceEditorViewModel NewEditor(
        string targetId, string titleKey, ComponentAppearance appearance,
        Func<AppearanceKind, Capability> capability)
    {
        var editor = new AppearanceEditorViewModel(targetId, titleKey, appearance, capability, OnEditorChanged);
        editor.PickImageRequested += () => PickImageRequested?.Invoke();
        return editor;
    }

    /// <summary>Pedido de seleção de imagem, repassado ao code-behind da página.</summary>
    public event Func<string?>? PickImageRequested;

    /// <summary>
    /// Um ajuste mudou: salva o tema e, se ele estiver aplicado, reaplica na hora — é o que dá a
    /// sensação de edição ao vivo, sem um botão "Aplicar" para cada slider.
    /// </summary>
    private void OnEditorChanged()
    {
        if (_loading || SelectedTheme is null) return;

        if (SyncAppearance) PropagateSync();

        _engine.Store.Save(SelectedTheme);

        if (_settings.Current.WinCustom.ActiveThemeId == SelectedTheme.Id && Enabled)
            _engine.ApplyActiveTheme();
    }

    // =====================================================================================
    //  Sincronização entre componentes
    // =====================================================================================

    [ObservableProperty] private bool syncAppearance;

    partial void OnSyncAppearanceChanged(bool value)
    {
        _settings.Current.WinCustom.SyncAppearance = value;
        _settings.Save();
        if (_loading) return;
        if (value) PropagateSync();
    }

    /// <summary>
    /// Copia o estilo do alvo de referência (a barra de tarefas) para o alvo "janela inteira" de
    /// cada outro componente. Só o estilo é copiado; cada alvo mantém sua identidade, e desligar
    /// a sincronização devolve a liberdade de editar região por região.
    /// </summary>
    private void PropagateSync()
    {
        var theme = SelectedTheme;
        if (theme is null) return;

        var source = WinTheme.Get(theme.Components, WinComponent.Taskbar);

        WinTheme.Get(theme.Explorer, ExplorerRegion.Window).CopyStyleFrom(source);
        WinTheme.Get(theme.Start, StartRegion.Window).CopyStyleFrom(source);
        WinTheme.Get(theme.Settings, SettingsRegion.Window).CopyStyleFrom(source);

        // Os editores em tela precisam refletir a mudança imediatamente.
        foreach (var tab in Components)
            foreach (var editor in tab.Editors)
                if (editor.TargetId is "Explorer.Window" or "Start.Window" or "Settings.Window")
                    editor.Reload();
    }

    // =====================================================================================
    //  Preferências do subsistema
    // =====================================================================================

    [ObservableProperty] private bool enabled;
    [ObservableProperty] private bool startWithWindows;
    [ObservableProperty] private bool restoreLastTheme;
    [ObservableProperty] private bool reapplyOnExplorerRestart;
    [ObservableProperty] private bool enableInjection;

    /// <summary>Se a DLL do motor de injeção está presente nesta instalação.</summary>
    public bool InjectionHostAvailable => InjectionHost.IsAvailable;

    /// <summary>Explicação de por que a parte beta está indisponível (quando for o caso).</summary>
    public string InjectionUnavailableText => Loc.S("WinCustom_InjectionMissing");

    // Todos os setters abaixo saem cedo durante a montagem da tela. Sem essa guarda ANTES de
    // qualquer escrita, apenas abrir a seção já gravaria preferências e registraria o app na
    // inicialização do Windows — alterar o sistema tem de ser sempre um ato explícito do usuário.

    partial void OnEnabledChanged(bool value)
    {
        if (_loading) return;
        if (value) _engine.Start();
        else _engine.RestoreAll(clearTheme: false);

        _settings.Current.WinCustom.Enabled = value;
        _settings.Save();
        RefreshStatus();
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_loading) return;
        _settings.Current.WinCustom.StartWithWindows = value;
        _settings.Save();
        _host.SetRegistered(value);
        RefreshStatus();
    }

    partial void OnRestoreLastThemeChanged(bool value)
    {
        if (_loading) return;
        _settings.Current.WinCustom.RestoreLastTheme = value;
        _settings.Save();
    }

    partial void OnReapplyOnExplorerRestartChanged(bool value)
    {
        if (_loading) return;
        _settings.Current.WinCustom.ReapplyOnExplorerRestart = value;
        _settings.Save();
        if (value) _engine.StartExplorerWatch();
        else _engine.StopExplorerWatch();
    }

    partial void OnEnableInjectionChanged(bool value)
    {
        if (_loading) return;
        _settings.Current.WinCustom.EnableInjection = value;
        _settings.Save();

        _engine.Capabilities.InjectionActive = value && InjectionHostAvailable;
        foreach (var tab in Components)
            foreach (var editor in tab.Editors)
                editor.RefreshAvailability();

        if (Enabled) _engine.ApplyActiveTheme();
        RefreshStatus();
    }

    // =====================================================================================
    //  Comandos da biblioteca
    // =====================================================================================

    /// <summary>
    /// Pedido de confirmação com reversão automática, atendido pelo code-behind. Recebe o nome do
    /// tema e devolve true se o usuário quiser mantê-lo.
    /// </summary>
    public event Func<string, bool>? ConfirmChangeRequested;

    /// <summary>
    /// Aplica o tema selecionado e pede confirmação: a personalização entra em vigor na hora, o
    /// usuário vê o resultado de verdade e tem alguns segundos para mantê-la. Sem confirmação, o
    /// estado anterior volta sozinho.
    ///
    /// A ordem importa — aplicamos ANTES de perguntar, porque a pergunta só faz sentido depois de
    /// o usuário poder ver o que mudou.
    /// </summary>
    [RelayCommand]
    private void ApplyTheme()
    {
        if (SelectedTheme is null) return;

        // Guarda o estado anterior para poder voltar exatamente a ele.
        string previousThemeId = _settings.Current.WinCustom.ActiveThemeId;
        bool previousEnabled = _settings.Current.WinCustom.Enabled;

        _engine.SetActiveTheme(SelectedTheme);
        enabled = true;
        OnPropertyChanged(nameof(Enabled));
        RefreshStatus();

        // O tema "Windows Default" não altera nada, então não há o que confirmar.
        if (SelectedTheme.Id == "builtin-windows-default") return;

        bool keep = ConfirmChangeRequested?.Invoke(SelectedTheme.Name) ?? true;
        if (keep)
        {
            StatusText = Loc.F("WinCustom_Applied", SelectedTheme.Name);
            return;
        }

        RollbackTo(previousThemeId, previousEnabled);
    }

    /// <summary>
    /// Desfaz uma aplicação não confirmada, devolvendo o tema anterior (ou o visual padrão, se
    /// não havia nenhum).
    /// </summary>
    private void RollbackTo(string previousThemeId, bool previousEnabled)
    {
        var previous = _engine.Store.FindById(previousThemeId);

        if (previous is not null && previousEnabled)
        {
            _engine.SetActiveTheme(previous);
            selectedTheme = previous;
            OnPropertyChanged(nameof(SelectedTheme));
            BindEditorsToTheme();
        }
        else
        {
            // Não havia personalização antes: voltar ao padrão do Windows por inteiro.
            _engine.RestoreAll();
            enabled = false;
            OnPropertyChanged(nameof(Enabled));
        }

        StatusText = Loc.S("WinCustom_Reverted");
        RefreshStatus();
    }

    [RelayCommand]
    private void CreateTheme()
    {
        var theme = _engine.Store.Create(Loc.S("WinCustom_NewThemeName"));
        ReloadThemes();
        SelectedTheme = Themes.FirstOrDefault(t => t.Id == theme.Id);
    }

    [RelayCommand]
    private void DuplicateTheme()
    {
        if (SelectedTheme is null) return;
        var copy = _engine.Store.Duplicate(SelectedTheme);
        ReloadThemes();
        SelectedTheme = Themes.FirstOrDefault(t => t.Id == copy.Id);
    }

    [RelayCommand]
    private void DeleteTheme()
    {
        if (SelectedTheme is null || SelectedTheme.IsBuiltIn) return;

        // Excluir o tema aplicado restaura o Windows antes — nunca deixamos um efeito órfão.
        if (_settings.Current.WinCustom.ActiveThemeId == SelectedTheme.Id)
            _engine.RestoreAll();

        _engine.Store.Delete(SelectedTheme);
        ReloadThemes();
        RefreshStatus();
    }

    /// <summary>Renomeia o tema selecionado; o nome vem de um diálogo do code-behind.</summary>
    public void RenameSelected(string newName)
    {
        if (SelectedTheme is null) return;
        if (!_engine.Store.Rename(SelectedTheme, newName)) return;

        var id = SelectedTheme.Id;
        ReloadThemes();
        SelectedTheme = Themes.FirstOrDefault(t => t.Id == id);
        RefreshStatus();
    }

    public void ExportSelected(string path)
    {
        if (SelectedTheme is null) return;
        _engine.Store.Export(SelectedTheme, path);
        StatusText = Loc.S("WinCustom_Exported");
    }

    public void ImportFrom(string path)
    {
        var theme = _engine.Store.Import(path);
        if (theme is null)
        {
            StatusText = Loc.S("WinCustom_ImportFailed");
            return;
        }
        ReloadThemes();
        SelectedTheme = Themes.FirstOrDefault(t => t.Id == theme.Id);
        StatusText = Loc.S("WinCustom_Imported");
    }

    // =====================================================================================
    //  Controle do subsistema
    // =====================================================================================

    [RelayCommand]
    private void RestartCustomization()
    {
        _engine.Restart();
        StatusText = Loc.S("WinCustom_Restarted");
        RefreshStatus();
    }

    [RelayCommand]
    private void PauseCustomization()
    {
        _engine.Pause();
        StatusText = Loc.S("WinCustom_Paused");
        RefreshStatus();
    }

    [RelayCommand]
    private void ResumeCustomization()
    {
        _engine.Resume();
        StatusText = Loc.S("WinCustom_Resumed");
        RefreshStatus();
    }

    [RelayCommand]
    private void ReapplyTheme()
    {
        _engine.ApplyActiveTheme();
        StatusText = Loc.S("WinCustom_Reapplied");
        RefreshStatus();
    }

    // =====================================================================================
    //  Restauração
    // =====================================================================================

    [RelayCommand]
    private void RestoreTaskbar()
    {
        _engine.RestoreTaskbar();
        StatusText = Loc.S("WinCustom_RestoredTaskbar");
        RefreshStatus();
    }

    [RelayCommand]
    private void RestoreExplorer()
    {
        _engine.RestoreExplorer();
        StatusText = Loc.S("WinCustom_RestoredExplorer");
        RefreshStatus();
    }

    [RelayCommand]
    private void RestoreStartMenu()
    {
        _engine.RestoreStartMenu();
        StatusText = Loc.S("WinCustom_RestoredStart");
        RefreshStatus();
    }

    [RelayCommand]
    private void RestoreSettings()
    {
        _engine.RestoreSettings();
        StatusText = Loc.S("WinCustom_RestoredSettings");
        RefreshStatus();
    }

    /// <summary>Restaura TUDO e desliga o subsistema. O code-behind confirma antes de chamar.</summary>
    public void RestoreEverything()
    {
        _engine.RestoreAll();
        _host.SetRegistered(false);

        enabled = false;
        startWithWindows = false;
        enableInjection = false;
        OnPropertyChanged(nameof(Enabled));
        OnPropertyChanged(nameof(StartWithWindows));
        OnPropertyChanged(nameof(EnableInjection));

        ReloadThemes();
        StatusText = Loc.S("WinCustom_RestoredAll");
        RefreshStatus();
    }

    /// <summary>Reinicia o Explorer — usado quando algo ficou visualmente preso.</summary>
    [RelayCommand]
    private void RestartExplorer()
    {
        CustomizationHostService.RestartExplorer();
        StatusText = Loc.S("WinCustom_ExplorerRestarted");
    }

    /// <summary>Gera e revela o script de recuperação que funciona sem o Pulse.</summary>
    [RelayCommand]
    private void CreateEmergencyScript()
    {
        _host.RevealEmergencyScript();
        StatusText = Loc.F("WinCustom_EmergencyCreated", CustomizationHostService.EmergencyScriptPath);
    }

    /// <summary>Libera todos os alvos em quarentena, a pedido do usuário.</summary>
    [RelayCommand]
    private void ClearQuarantine()
    {
        _engine.Watchdog.ReleaseAll();
        RefreshStatus();
        if (Enabled) _engine.ApplyActiveTheme();
    }

    // =====================================================================================
    //  Status
    // =====================================================================================

    private void OnEngineStateChanged() => RefreshStatus();

    private void OnTargetQuarantined(string target)
    {
        // A proteção desligou um alvo: o usuário precisa saber qual e por quê.
        QuarantineText = Loc.F("WinCustom_QuarantinedTarget", target);
        HasQuarantine = true;
    }

    public void RefreshStatus()
    {
        StateText = _engine.State switch
        {
            CustomizationState.Active => Loc.S("WinCustom_StateActive"),
            CustomizationState.Paused => Loc.S("WinCustom_StatePaused"),
            CustomizationState.Unsupported => Loc.S("WinCustom_StateUnsupported"),
            _ => Loc.S("WinCustom_StateStopped"),
        };

        ActiveThemeName = _engine.ActiveTheme?.Name ?? Loc.S("WinCustom_NoTheme");

        startupStatusText = _host.IsRegistered ? Loc.S("WinCustom_Yes") : Loc.S("WinCustom_No");
        OnPropertyChanged(nameof(StartupStatusText));

        hostStatusText = _engine.State == CustomizationState.Active
            ? Loc.S("WinCustom_HostRunning")
            : Loc.S("WinCustom_HostStopped");
        OnPropertyChanged(nameof(HostStatusText));

        var caps = _engine.Capabilities;
        compatibilityText = caps.IsWindows11
            ? Loc.F("WinCustom_CompatOk", caps.Build)
            : Loc.F("WinCustom_CompatUnsupported", caps.Build);
        OnPropertyChanged(nameof(CompatibilityText));

        var quarantined = _engine.Watchdog.Quarantined();
        HasQuarantine = quarantined.Count > 0;
        if (HasQuarantine)
            QuarantineText = Loc.F("WinCustom_QuarantinedTarget",
                string.Join(", ", quarantined.Select(q => q.Target)));

        OnPropertyChanged(nameof(CanEditSelectedTheme));
        OnPropertyChanged(nameof(InjectionHostAvailable));
        OnPropertyChanged(nameof(InjectionUnavailableText));
    }

    private void RefreshLabels()
    {
        foreach (var tab in Components) tab.RefreshLabels();
        RefreshStatus();
    }
}
