using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pulse1x.App.Localization;
using Pulse1x.App.Models;
using Pulse1x.App.Services;

namespace Pulse1x.App.ViewModels;

/// <summary>
/// Orquestra o Diagnóstico Inteligente: dispara a análise (somente quando o usuário pede),
/// guarda o último relatório em cache, expõe os botões de ação (que reutilizam as ferramentas
/// seguras já existentes) e exporta o relatório em TXT/PDF/JSON.
/// </summary>
public partial class HealthViewModel : ObservableObject
{
    private readonly HealthDiagnosticsService _diagnostics;
    private readonly SpecialCommandsService _commands;
    private readonly DiskCleanupService _cleanup;
    private readonly Action _openOptimizations;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReport))]
    [NotifyPropertyChangedFor(nameof(ShowIntro))]
    private HealthReport? report;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowIntro))]
    private bool isRunning;

    [ObservableProperty] private string progressText = "";

    [ObservableProperty] private bool isActionRunning;
    [ObservableProperty] private string actionStatus = "";

    // Recolhe a análise de volta, deixando só o resumo da nota geral visível.
    [ObservableProperty] private bool isReportCollapsed;

    public bool HasReport => Report is not null;
    public bool ShowIntro => Report is null && !IsRunning;

    public HealthViewModel(
        HealthDiagnosticsService diagnostics,
        SpecialCommandsService commands,
        DiskCleanupService cleanup,
        Action openOptimizations)
    {
        _diagnostics = diagnostics;
        _commands = commands;
        _cleanup = cleanup;
        _openOptimizations = openOptimizations;
    }

    // ===================== Diagnóstico =====================

    [RelayCommand]
    private async Task RunDiagnosticAsync()
    {
        if (IsRunning) return;

        IsRunning = true;
        ProgressText = Loc.S("HealthVm_StartingDiagnostic");
        ActionStatus = "";
        IsReportCollapsed = false;

        try
        {
            var progress = new Progress<string>(msg => ProgressText = msg);
            var result = await _diagnostics.RunAsync(progress);
            Report = result;
        }
        catch (Exception ex)
        {
            ProgressText = Loc.S("HealthVm_DiagnosticFailed") + ex.Message;
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand]
    private void ToggleReportCollapsed() => IsReportCollapsed = !IsReportCollapsed;

    // ===================== Botões de ação =====================

    [RelayCommand]
    private void OpenOptimizations() => _openOptimizations();

    [RelayCommand]
    private void OpenWindowsTools()
    {
        // As ferramentas nativas do Windows ficam na categoria Otimização.
        _openOptimizations();
    }

    [RelayCommand]
    private void ManageStartup()
    {
        try { _commands.OpenTool("taskmgr.exe", "/7"); }
        catch
        {
            try { _commands.OpenTool("ms-settings:startupapps"); } catch { }
        }
    }

    [RelayCommand]
    private async Task RunSfcAsync()
    {
        await RunConsoleActionAsync(
            Loc.S("HealthVm_RunSfc"),
            Loc.S("HealthVm_RunSfcConfirm"),
            "sfc", "/scannow", Loc.S("HealthVm_SfcOpName"));
    }

    [RelayCommand]
    private async Task RunDismAsync()
    {
        await RunConsoleActionAsync(
            Loc.S("HealthVm_RunDism"),
            Loc.S("HealthVm_RunDismConfirm"),
            "dism", "/Online /Cleanup-Image /RestoreHealth", Loc.S("HealthVm_DismOpName"));
    }

    private async Task RunConsoleActionAsync(string title, string confirmMessage, string exe, string args, string operationName)
    {
        if (IsActionRunning) return;

        var confirm = System.Windows.MessageBox.Show(confirmMessage, title,
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        IsActionRunning = true;
        ActionStatus = Loc.F("HealthVm_Running", operationName);
        try
        {
            var result = await _commands.RunProcessAsync(exe, args);
            ActionStatus = result.ExitCode == 0
                ? "✅ " + Loc.F("HealthVm_CompletedSuccess", operationName)
                : "⚠️ " + Loc.F("HealthVm_CompletedWarnings", operationName, result.ExitCode);
        }
        catch (Exception ex)
        {
            ActionStatus = "❌ " + Loc.F("HealthVm_FailedToRun", operationName, ex.Message);
        }
        finally
        {
            IsActionRunning = false;
        }
    }

    [RelayCommand]
    private async Task ClearTempFilesAsync()
    {
        if (IsActionRunning) return;

        var confirm = System.Windows.MessageBox.Show(
            Loc.S("HealthVm_ClearTempConfirm"),
            Loc.S("HealthVm_ClearTempTitle"),
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        IsActionRunning = true;
        ActionStatus = Loc.S("HealthVm_ClearingTemp");
        try
        {
            var categories = _cleanup.GetCategories()
                .Where(c => c.Key is "win_temp" or "win_cache")
                .ToList();
            var result = await _cleanup.CleanAsync(categories);
            double freedMb = result.FreedBytes / (1024.0 * 1024);
            ActionStatus = "✅ " + Loc.F("HealthVm_CleanupDone", $"{freedMb:0}", result.FilesRemoved);
        }
        catch (Exception ex)
        {
            ActionStatus = "❌ " + Loc.S("HealthVm_CleanupFailed") + ex.Message;
        }
        finally
        {
            IsActionRunning = false;
        }
    }

    [RelayCommand]
    private async Task FixAutomaticallyAsync()
    {
        if (IsActionRunning) return;

        var confirm = System.Windows.MessageBox.Show(
            Loc.S("HealthVm_AutoFixConfirm"),
            Loc.S("HealthVm_AutoFixTitle"),
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        IsActionRunning = true;
        try
        {
            ActionStatus = Loc.S("HealthVm_ClearingTemp");
            var categories = _cleanup.GetCategories()
                .Where(c => c.Key is "win_temp" or "win_cache")
                .ToList();
            var result = await _cleanup.CleanAsync(categories);
            double freedMb = result.FreedBytes / (1024.0 * 1024);

            ActionStatus = Loc.S("HealthVm_RenewingDns");
            await _commands.RunProcessAsync("ipconfig", "/flushdns");

            ActionStatus = "✅ " + Loc.F("HealthVm_AutoFixDone", $"{freedMb:0}");
        }
        catch (Exception ex)
        {
            ActionStatus = "❌ " + Loc.S("HealthVm_AutoFixFailed") + ex.Message;
        }
        finally
        {
            IsActionRunning = false;
        }
    }

    // ===================== Exportação =====================

    [RelayCommand]
    private void ExportTxt()
    {
        if (Report is null) return;
        SaveTextFile(HealthReportExporter.BuildText(Report), "txt", Loc.S("HealthVm_TxtFilter"));
    }

    [RelayCommand]
    private void ExportJson()
    {
        if (Report is null) return;
        SaveTextFile(HealthReportExporter.BuildJson(Report), "json", Loc.S("HealthVm_JsonFilter"));
    }

    [RelayCommand]
    private void ExportPdf()
    {
        if (Report is null) return;
        try
        {
            var doc = new System.Windows.Documents.FlowDocument
            {
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 11,
                PagePadding = new System.Windows.Thickness(40),
            };
            doc.Blocks.Add(new System.Windows.Documents.Paragraph(
                new System.Windows.Documents.Run(HealthReportExporter.BuildText(Report))));

            var dlg = new System.Windows.Controls.PrintDialog();
            if (dlg.ShowDialog() != true) return;

            doc.PageHeight = dlg.PrintableAreaHeight;
            doc.PageWidth = dlg.PrintableAreaWidth;
            dlg.PrintDocument(((System.Windows.Documents.IDocumentPaginatorSource)doc).DocumentPaginator,
                Loc.S("HealthVm_PrintDocTitle"));
            ActionStatus = "✅ " + Loc.S("HealthVm_PrintSent");
        }
        catch (Exception ex)
        {
            ActionStatus = "❌ " + Loc.S("HealthVm_PdfFailed") + ex.Message;
        }
    }

    private void SaveTextFile(string content, string ext, string filter)
    {
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = $"Diagnostico_Pulse1x_{DateTime.Now:yyyyMMdd_HHmm}",
                DefaultExt = "." + ext,
                Filter = filter,
            };
            if (dialog.ShowDialog() != true) return;

            File.WriteAllText(dialog.FileName, content, System.Text.Encoding.UTF8);
            ActionStatus = "✅ " + Loc.F("HealthVm_ReportSaved", dialog.FileName);
        }
        catch (Exception ex)
        {
            ActionStatus = "❌ " + Loc.S("HealthVm_SaveFailed") + ex.Message;
        }
    }
}
