using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pulse1x.App.Localization;
using Pulse1x.App.Services;

namespace Pulse1x.App.ViewModels;

public partial class SpecialCommandsViewModel : ObservableObject
{
    private readonly SpecialCommandsService _service;

    // Recolhe/expande o cartão inteiro de Comandos Especiais.
    [ObservableProperty] private bool isSectionExpanded;
    [ObservableProperty] private bool isExplainerExpanded;

    // Painel de saída compartilhado (comandos longos: SFC, DISM, etc.).
    [ObservableProperty] private bool isRunningCommand;
    [ObservableProperty] private string currentOperation = "";
    [ObservableProperty] private bool hasOutput;
    [ObservableProperty] private string outputLog = "";

    // Manutenção Rápida.
    [ObservableProperty] private bool isMaintenanceRunning;
    [ObservableProperty] private double maintenanceProgress;
    [ObservableProperty] private string maintenanceStepText = "";

    // Ponto de restauração.
    [ObservableProperty] private string restorePointName = Loc.S("Special_ManualRestorePointName");
    [ObservableProperty] private bool isCreatingRestorePoint;
    [ObservableProperty] private string restorePointStatus = "";

    // Rede.
    [ObservableProperty] private bool showNetworkInfo;
    [ObservableProperty] private string networkInfoText = "";

    // Relatório.
    [ObservableProperty] private string reportStatus = "";

    public ObservableCollection<MaintenanceStepViewModel> MaintenanceSteps { get; } = new();

    private (ActivePowerPlan Plan, SpecialCommandViewModel Card)[] _powerPlanCards = Array.Empty<(ActivePowerPlan, SpecialCommandViewModel)>();

    public ObservableCollection<SpecialCommandViewModel> RepairCommands { get; } = new();
    public ObservableCollection<SpecialCommandViewModel> PerformanceCommands { get; } = new();
    public ObservableCollection<SpecialCommandViewModel> RecoveryCommands { get; } = new();
    public ObservableCollection<SpecialCommandViewModel> WindowsTools { get; } = new();
    public ObservableCollection<SpecialCommandViewModel> NetworkCommands { get; } = new();

    public SpecialCommandsViewModel(SpecialCommandsService service)
    {
        _service = service;
        BuildCommands();
        Loc.Instance.LanguageChanged += BuildCommands;
    }

    private void BuildCommands()
    {
        RepairCommands.Clear();
        PerformanceCommands.Clear();
        RecoveryCommands.Clear();
        WindowsTools.Clear();
        NetworkCommands.Clear();

        // ---- Reparo do Sistema ----
        RepairCommands.Add(new SpecialCommandViewModel("🛡️", Loc.S("Special_SfcTitle"),
            Loc.S("Special_SfcDesc"), Loc.S("Special_Run"),
            c => RunLongCommandAsync(c, Loc.S("Special_SfcOpTitle"), "sfc", "/scannow",
                Loc.S("Special_SfcConfirm"))));

        RepairCommands.Add(new SpecialCommandViewModel("🩺", Loc.S("Special_DismRestoreTitle"),
            Loc.S("Special_DismRestoreDesc"), Loc.S("Special_Run"),
            c => RunLongCommandAsync(c, Loc.S("Special_DismRestoreOpTitle"), "dism", "/Online /Cleanup-Image /RestoreHealth",
                Loc.S("Special_DismRestoreConfirm"))));

        RepairCommands.Add(new SpecialCommandViewModel("💽", Loc.S("Special_ChkdskTitle"),
            Loc.S("Special_ChkdskDesc"), Loc.S("Special_Run"),
            c => RunLongCommandAsync(c, Loc.S("Special_ChkdskOpTitle"), "chkdsk", "C:",
                Loc.S("Special_ChkdskConfirm"))));

        RepairCommands.Add(new SpecialCommandViewModel("🧹", Loc.S("Special_DismCleanupTitle"),
            Loc.S("Special_DismCleanupDesc"), Loc.S("Special_Run"),
            c => RunLongCommandAsync(c, Loc.S("Special_DismCleanupOpTitle"), "dism", "/Online /Cleanup-Image /StartComponentCleanup",
                Loc.S("Special_DismCleanupConfirm"))));

        // ---- Desempenho ----
        var balancedCard = new SpecialCommandViewModel("⚖️", Loc.S("Special_BalancedPlanTitle"),
            Loc.S("Special_BalancedPlanDesc"), Loc.S("Special_Activate"),
            c => SetPowerPlanAsync(c, Loc.S("Special_BalancedPlanTitle"), _service.SetBalancedPlanAsync,
                Loc.S("Special_BalancedPlanConfirm")), isToggle: true);

        var highPerfCard = new SpecialCommandViewModel("🚀", Loc.S("Special_HighPerfTitle"),
            Loc.S("Special_HighPerfDesc"), Loc.S("Special_Activate"),
            c => SetPowerPlanAsync(c, Loc.S("Special_HighPerfTitle"), _service.SetHighPerformancePlanAsync,
                Loc.S("Special_HighPerfConfirm")), isToggle: true);

        var ultimateCard = new SpecialCommandViewModel("⚡", Loc.S("Special_UltimateTitle"),
            Loc.S("Special_UltimateDesc"), Loc.S("Special_Activate"),
            c => SetPowerPlanAsync(c, Loc.S("Special_UltimateTitle"), _service.SetUltimatePlanAsync,
                Loc.S("Special_UltimateConfirm")), isToggle: true);

        _powerPlanCards = new[]
        {
            (ActivePowerPlan.Balanced, balancedCard),
            (ActivePowerPlan.HighPerformance, highPerfCard),
            (ActivePowerPlan.Ultimate, ultimateCard),
        };

        foreach (var (_, card) in _powerPlanCards)
        {
            card.Completed += RefreshActivePowerPlanAsync;
            card.ToggledOff += OnPowerPlanToggledOffAsync;
        }

        PerformanceCommands.Add(balancedCard);
        PerformanceCommands.Add(highPerfCard);
        PerformanceCommands.Add(ultimateCard);

        PerformanceCommands.Add(new SpecialCommandViewModel("🗜️", Loc.S("Special_OptimizeDrivesTitle"),
            Loc.S("Special_OptimizeDrivesDesc"), Loc.S("Special_Run"),
            c => OpenToolCard(c, "dfrgui.exe")));

        _ = RefreshActivePowerPlanAsync();

        // ---- Recuperação ----
        RecoveryCommands.Add(new SpecialCommandViewModel("⏮️", Loc.S("Special_SystemRestoreTitle"),
            Loc.S("Special_SystemRestoreDesc"), Loc.S("Special_Open"),
            c => OpenToolCard(c, "rstrui.exe")));

        // ---- Ferramentas do Windows ----
        WindowsTools.Add(new SpecialCommandViewModel("🗝️", Loc.S("Special_GodModeTitle"),
            Loc.S("Special_GodModeDesc"), Loc.S("Special_Create"),
            c => CreateGodModeCard(c)));
        WindowsTools.Add(new SpecialCommandViewModel("🔌", Loc.S("Special_DeviceManagerTitle"),
            Loc.S("Special_DeviceManagerDesc"), Loc.S("Special_Open"), c => OpenToolCard(c, "devmgmt.msc")));
        WindowsTools.Add(new SpecialCommandViewModel("🗂️", Loc.S("Special_DiskManagementTitle"),
            Loc.S("Special_DiskManagementDesc"), Loc.S("Special_Open"), c => OpenToolCard(c, "diskmgmt.msc")));
        WindowsTools.Add(new SpecialCommandViewModel("📊", Loc.S("Special_ResourceMonitorTitle"),
            Loc.S("Special_ResourceMonitorDesc"), Loc.S("Special_Open"), c => OpenToolCard(c, "resmon.exe")));
        WindowsTools.Add(new SpecialCommandViewModel("📈", Loc.S("Special_PerfMonitorTitle"),
            Loc.S("Special_PerfMonitorDesc"), Loc.S("Special_Open"), c => OpenToolCard(c, "perfmon.exe")));
        WindowsTools.Add(new SpecialCommandViewModel("📋", Loc.S("Special_EventViewerTitle"),
            Loc.S("Special_EventViewerDesc"), Loc.S("Special_Open"), c => OpenToolCard(c, "eventvwr.msc")));
        WindowsTools.Add(new SpecialCommandViewModel("⚙️", Loc.S("Special_ServicesTitle"),
            Loc.S("Special_ServicesDesc"), Loc.S("Special_Open"), c => OpenToolCard(c, "services.msc")));
        WindowsTools.Add(new SpecialCommandViewModel("📜", Loc.S("Special_GroupPolicyTitle"),
            Loc.S("Special_GroupPolicyDesc"), Loc.S("Special_Open"),
            c => OpenToolCard(c, "gpedit.msc", Loc.S("Special_GroupPolicyUnavailable"))));
        WindowsTools.Add(new SpecialCommandViewModel("🧠", Loc.S("Special_MemoryDiagnosticTitle"),
            Loc.S("Special_MemoryDiagnosticDesc"), Loc.S("Special_Open"), c => OpenToolCard(c, "mdsched.exe")));
        WindowsTools.Add(new SpecialCommandViewModel("🧰", Loc.S("Special_TaskManagerTitle"),
            Loc.S("Special_TaskManagerDesc"), Loc.S("Special_Open"), c => OpenToolCard(c, "taskmgr.exe")));

        // ---- Rede ----
        NetworkCommands.Add(new SpecialCommandViewModel("🔄", Loc.S("Special_RenewNetworkTitle"),
            Loc.S("Special_RenewNetworkDesc"), Loc.S("Special_Run"),
            c => RunLongCommandAsync(c, Loc.S("Special_RenewNetworkOpTitle"), "ipconfig", "/renew", null, _service.RenewNetworkAsync)));

        NetworkCommands.Add(new SpecialCommandViewModel("♻️", Loc.S("Special_ResetNetworkTitle"),
            Loc.S("Special_ResetNetworkDesc"), Loc.S("Special_Run"),
            c => RunLongCommandAsync(c, Loc.S("Special_ResetNetworkOpTitle"), "netsh", "winsock reset",
                Loc.S("Special_ResetNetworkConfirm"),
                _service.ResetNetworkAsync)));
    }

    // ===================== Execução de comandos longos (saída compartilhada) =====================

    private async Task RunLongCommandAsync(
        SpecialCommandViewModel card, string title, string exe, string args,
        string? confirmMessage, Func<IProgress<string>, Task<CommandResult>>? customRunner = null)
    {
        if (IsRunningCommand || IsMaintenanceRunning)
        {
            card.SetStatus(CommandStatus.Warning, Loc.S("Special_WaitCurrentOperation"));
            return;
        }

        if (confirmMessage is not null)
        {
            var confirm = System.Windows.MessageBox.Show(confirmMessage, title,
                System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
            if (confirm != System.Windows.MessageBoxResult.Yes) return;
        }

        IsRunningCommand = true;
        CurrentOperation = title;
        HasOutput = true;
        OutputLog = "";
        card.SetStatus(CommandStatus.None, "");

        var progress = new Progress<string>(AppendOutput);

        try
        {
            var result = customRunner is not null
                ? await customRunner(progress)
                : await _service.RunProcessAsync(exe, args, progress);

            card.SetStatus(
                result.ExitCode == 0 ? CommandStatus.Success : CommandStatus.Warning,
                result.ExitCode == 0 ? Loc.S("Special_CompletedSuccess") : Loc.F("Special_CompletedWarnings", result.ExitCode));
        }
        catch (Exception ex)
        {
            card.SetStatus(CommandStatus.Error, Loc.S("Special_FailedToRun") + ex.Message);
        }
        finally
        {
            IsRunningCommand = false;
            CurrentOperation = "";
        }
    }

    private void AppendOutput(string line)
    {
        string clean = line.Replace("\0", "").TrimEnd();
        if (clean.Length == 0) return;

        OutputLog += clean + "\n";
        if (OutputLog.Length > 12000)
            OutputLog = OutputLog[^12000..];
    }

    // ===================== Abrir ferramentas =====================

    private void OpenToolCard(SpecialCommandViewModel card, string fileName, string? unavailableMessage = null)
    {
        try
        {
            var process = _service.OpenTool(fileName);
            card.SetStatus(CommandStatus.Success, Loc.S("Special_ToolOpened"));

            if (process is not null)
            {
                process.EnableRaisingEvents = true;
                process.Exited += (_, _) => System.Windows.Application.Current.Dispatcher.Invoke(
                    () => card.SetStatus(CommandStatus.None, ""));
            }
        }
        catch (Exception ex)
        {
            card.SetStatus(CommandStatus.Error, unavailableMessage ?? (Loc.S("Special_CouldNotOpen") + ex.Message));
        }
    }

    private void CreateGodModeCard(SpecialCommandViewModel card)
    {
        try
        {
            _service.CreateGodMode();
            card.SetStatus(CommandStatus.Success, Loc.S("Special_FolderCreatedOnDesktop"));
        }
        catch (Exception ex)
        {
            card.SetStatus(CommandStatus.Error, Loc.S("Special_CouldNotCreateFolder") + ex.Message);
        }
    }

    // Consulta o plano de energia realmente ativo no Windows e ajusta os três interruptores
    // para refletir a realidade (só o do plano ativo fica aceso), sem disparar nova ativação.
    private async Task RefreshActivePowerPlanAsync()
    {
        var active = await _service.GetActivePowerPlanAsync();
        foreach (var (plan, card) in _powerPlanCards)
        {
            bool isActive = plan == active;
            card.SetToggleSilently(isActive);

            // Limpa a mensagem "Plano de energia ativado." dos cartões que não são mais
            // o plano ativo — ela não deveria continuar aparecendo depois de trocar de plano.
            if (!isActive) card.SetStatus(CommandStatus.None, "");
        }
    }

    // Quando o usuário DESLIGA o interruptor de um plano, o Windows precisa de algum plano
    // ativo no lugar — voltamos ao Balanceado (padrão do Windows), de forma silenciosa.
    // O próprio Balanceado é a linha de base: desligá-lo apenas o reacende.
    private async Task OnPowerPlanToggledOffAsync(SpecialCommandViewModel card)
    {
        var (_, balancedCard) = _powerPlanCards[0]; // Balanceado é o primeiro da lista

        if (card == balancedCard)
        {
            card.SetToggleSilently(true);
            return;
        }

        card.SetStatus(CommandStatus.None, "");
        await _service.SetBalancedPlanAsync();
        await RefreshActivePowerPlanAsync();
        balancedCard.SetStatus(CommandStatus.Success, Loc.S("Special_PowerPlanActivated"));
    }

    private async Task SetPowerPlanAsync(SpecialCommandViewModel card, string title, Func<Task<CommandResult>> action, string confirmMessage)
    {
        if (IsRunningCommand || IsMaintenanceRunning)
        {
            card.SetStatus(CommandStatus.Warning, Loc.S("Special_WaitCurrentOperation"));
            return;
        }

        var confirm = System.Windows.MessageBox.Show(confirmMessage, title,
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        try
        {
            var result = await action();
            card.SetStatus(result.ExitCode == 0 ? CommandStatus.Success : CommandStatus.Warning,
                result.ExitCode == 0 ? Loc.S("Special_PowerPlanActivated") : Loc.S("Special_PowerPlanFailed"));
        }
        catch (Exception ex)
        {
            card.SetStatus(CommandStatus.Error, Loc.S("Special_FailedShort") + ex.Message);
        }
    }

    // ===================== Ponto de restauração =====================

    [RelayCommand]
    private async Task CreateRestorePointAsync()
    {
        if (IsCreatingRestorePoint || IsMaintenanceRunning) return;

        IsCreatingRestorePoint = true;
        RestorePointStatus = Loc.S("Special_CreatingRestorePoint");
        try
        {
            string name = string.IsNullOrWhiteSpace(RestorePointName) ? Loc.S("Special_ManualRestorePointName") : RestorePointName.Trim();
            var result = await _service.CreateRestorePointAsync(name);
            RestorePointStatus = result.ExitCode == 0
                ? "✅ " + Loc.S("Special_RestorePointSuccess")
                : "⚠️ " + Loc.S("Special_RestorePointFailed");
        }
        catch (Exception ex)
        {
            RestorePointStatus = "❌ " + Loc.S("Special_RestorePointError") + ex.Message;
        }
        finally
        {
            IsCreatingRestorePoint = false;
        }
    }

    // ===================== Rede: informações =====================

    [RelayCommand]
    private void ShowNetwork()
    {
        NetworkInfoText = _service.GetNetworkInfo();
        ShowNetworkInfo = true;
    }

    // ===================== Relatório =====================

    [RelayCommand]
    private void GenerateReport(string format)
    {
        try
        {
            if (format is "TXT" or "HTML")
            {
                bool html = format == "HTML";
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = $"Relatorio_Pulse1x_{DateTime.Now:yyyyMMdd_HHmm}",
                    DefaultExt = html ? ".html" : ".txt",
                    Filter = html ? Loc.S("Special_HtmlFilter") : Loc.S("Special_TxtFilter"),
                };
                if (dialog.ShowDialog() != true) return;

                File.WriteAllText(dialog.FileName, html ? _service.BuildReportHtml() : _service.BuildReportText(),
                    System.Text.Encoding.UTF8);
                ReportStatus = "✅ " + Loc.F("Special_ReportSaved", format, dialog.FileName);
            }
            else // PDF
            {
                PrintReportToPdf();
            }
        }
        catch (Exception ex)
        {
            ReportStatus = "❌ " + Loc.S("Special_ReportFailed") + ex.Message;
        }
    }

    private void PrintReportToPdf()
    {
        var doc = new System.Windows.Documents.FlowDocument
        {
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 11,
            PagePadding = new System.Windows.Thickness(40),
        };
        doc.Blocks.Add(new System.Windows.Documents.Paragraph(
            new System.Windows.Documents.Run(_service.BuildReportText())));

        var dlg = new System.Windows.Controls.PrintDialog();
        if (dlg.ShowDialog() != true) return;

        doc.PageHeight = dlg.PrintableAreaHeight;
        doc.PageWidth = dlg.PrintableAreaWidth;
        dlg.PrintDocument(((System.Windows.Documents.IDocumentPaginatorSource)doc).DocumentPaginator,
            Loc.S("Special_SystemReportDocTitle"));
        ReportStatus = "✅ " + Loc.S("Special_ReportSentToPrint");
    }

    // ===================== Manutenção Rápida =====================

    [RelayCommand]
    private async Task RunQuickMaintenanceAsync()
    {
        if (IsMaintenanceRunning || IsRunningCommand) return;

        var confirm = System.Windows.MessageBox.Show(
            Loc.S("Special_QuickMaintenanceConfirm"),
            Loc.S("Special_QuickMaintenanceTitle"), System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        var steps = new (string title, Func<IProgress<string>, Task<CommandResult>> run)[]
        {
            (Loc.S("Special_StepRestorePoint"), _ => _service.CreateRestorePointAsync("Antes da Manutenção Rápida - Pulse1x")),
            (Loc.S("Special_StepSfc"), p => _service.RunProcessAsync("sfc", "/scannow", p)),
            (Loc.S("Special_StepDismRestore"), p => _service.RunProcessAsync("dism", "/Online /Cleanup-Image /RestoreHealth", p)),
            (Loc.S("Special_StepDismCleanup"), p => _service.RunProcessAsync("dism", "/Online /Cleanup-Image /StartComponentCleanup", p)),
            (Loc.S("Special_StepOptimizeDrives"), p => _service.RunProcessAsync("defrag", "/C /O", p)),
        };

        MaintenanceSteps.Clear();
        foreach (var s in steps)
            MaintenanceSteps.Add(new MaintenanceStepViewModel(s.title));

        IsMaintenanceRunning = true;
        HasOutput = true;
        OutputLog = "";
        MaintenanceProgress = 0;
        var progress = new Progress<string>(AppendOutput);

        try
        {
            for (int i = 0; i < steps.Length; i++)
            {
                var stepVm = MaintenanceSteps[i];
                stepVm.SetRunning();
                MaintenanceStepText = Loc.F("Special_StepProgress", i + 1, steps.Length, steps[i].title);

                try
                {
                    var result = await steps[i].run(progress);
                    stepVm.SetDone(result.ExitCode == 0);
                }
                catch (Exception ex)
                {
                    stepVm.SetDone(false);
                    AppendOutput(Loc.S("Special_ErrorPrefix") + ex.Message);
                }

                MaintenanceProgress = (i + 1) / (double)steps.Length * 100;
            }

            MaintenanceStepText = Loc.S("Special_QuickMaintenanceDone");
        }
        finally
        {
            IsMaintenanceRunning = false;
        }
    }
}

public enum MaintenanceStepStatus { Pending, Running, Ok, Failed }

public partial class MaintenanceStepViewModel : ObservableObject
{
    public string Title { get; }

    [ObservableProperty] private MaintenanceStepStatus status = MaintenanceStepStatus.Pending;
    [ObservableProperty] private string icon = "⏳";

    public MaintenanceStepViewModel(string title) => Title = title;

    public void SetRunning()
    {
        Status = MaintenanceStepStatus.Running;
        Icon = "▶️";
    }

    public void SetDone(bool ok)
    {
        Status = ok ? MaintenanceStepStatus.Ok : MaintenanceStepStatus.Failed;
        Icon = ok ? "✅" : "⚠️";
    }
}

public partial class SpecialCommandViewModel : ObservableObject
{
    public string Icon { get; }
    public string Title { get; }
    public string Description { get; }
    public string ButtonText { get; }

    /// <summary>Comandos de "Ativar" (ex.: planos de energia) usam um interruptor em vez de botão.</summary>
    public bool IsToggleCommand { get; }

    private readonly Func<SpecialCommandViewModel, Task> _execute;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowButton))]
    [NotifyPropertyChangedFor(nameof(ShowToggle))]
    [NotifyPropertyChangedFor(nameof(CanToggle))]
    [NotifyPropertyChangedFor(nameof(ShowButtonRing))]
    private bool isRunning;

    [ObservableProperty] private bool hasStatus;
    [ObservableProperty] private string statusMessage = "";
    [ObservableProperty] private bool isToggleOn;

    public bool ShowButton => !IsToggleCommand && !IsRunning;
    public bool ShowToggle => IsToggleCommand && !IsRunning;

    /// <summary>O interruptor fica visível, mas desabilitado enquanto o comando executa
    /// (evita o "piscar"/voltar sozinho de esconder e reexibir o controle).</summary>
    public bool CanToggle => !IsRunning;

    /// <summary>Anel de progresso para os cartões que usam botão (não interruptor).</summary>
    public bool ShowButtonRing => !IsToggleCommand && IsRunning;

    public SpecialCommandViewModel(string icon, string title, string description, string buttonText,
        Func<SpecialCommandViewModel, Task> execute, bool isToggle = false)
    {
        Icon = icon;
        Title = title;
        Description = description;
        ButtonText = buttonText;
        IsToggleCommand = isToggle;
        _execute = execute;
    }

    private bool _suppressToggleAction;

    /// <summary>Disparado quando o usuário DESLIGA o interruptor. Quem trata (o ViewModel)
    /// decide o que ativar no lugar, já que o Windows sempre tem algum plano ativo.</summary>
    public event Func<SpecialCommandViewModel, Task>? ToggledOff;

    partial void OnIsToggleOnChanged(bool value)
    {
        if (_suppressToggleAction) return;

        if (value)
        {
            if (!IsRunning) _ = Run();
        }
        else if (ToggledOff is not null)
        {
            _ = ToggledOff.Invoke(this);
        }
    }

    /// <summary>Ajusta o interruptor sem disparar a ativação — usado para refletir o plano
    /// de energia realmente ativo no Windows (consultado via powercfg).</summary>
    public void SetToggleSilently(bool value)
    {
        _suppressToggleAction = true;
        IsToggleOn = value;
        _suppressToggleAction = false;
    }

    // Sobrecarga para ações síncronas (abrir ferramenta, criar pasta).
    public SpecialCommandViewModel(string icon, string title, string description, string buttonText,
        Action<SpecialCommandViewModel> execute)
        : this(icon, title, description, buttonText, c => { execute(c); return Task.CompletedTask; })
    {
    }

    /// <summary>Disparado depois que a ação termina — usado pelos cartões de plano de
    /// energia para reconsultar qual plano está realmente ativo no Windows.</summary>
    public event Func<Task>? Completed;

    [RelayCommand]
    private async Task Run()
    {
        if (IsRunning) return;
        IsRunning = true;
        try { await _execute(this); }
        catch (Exception ex) { SetStatus(CommandStatus.Error, ex.Message); }
        finally
        {
            IsRunning = false;
            if (Completed is not null) await Completed.Invoke();
        }
    }

    public void SetStatus(CommandStatus status, string message)
    {
        string prefix = status switch
        {
            CommandStatus.Success => "✅ ",
            CommandStatus.Warning => "⚠️ ",
            CommandStatus.Error => "❌ ",
            _ => ""
        };
        StatusMessage = prefix + message;
        HasStatus = message.Length > 0;
    }
}
