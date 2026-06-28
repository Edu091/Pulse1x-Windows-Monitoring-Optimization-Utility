using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pulse1x.App.Localization;
using Pulse1x.App.Models;
using Pulse1x.App.Services;

namespace Pulse1x.App.ViewModels;

public partial class DiskCleanupViewModel : ObservableObject
{
    private readonly DiskCleanupService _service;
    private IReadOnlyList<CleanupCategoryDefinition> _definitions = Array.Empty<CleanupCategoryDefinition>();

    // Espaço do disco.
    [ObservableProperty] private string diskTotalText = "--";
    [ObservableProperty] private string diskUsedText = "--";
    [ObservableProperty] private string diskFreeText = "--";
    [ObservableProperty] private double diskUsagePercent;

    // Resumo da análise.
    [ObservableProperty] private bool hasAnalyzed;
    [ObservableProperty] private string recoverableText = "--";
    [ObservableProperty] private string fileCountText = "--";
    [ObservableProperty] private string selectedRecoverableText = Loc.S("DiskCleanup_NothingSelected");

    // Estado da análise.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    private bool isAnalyzing;

    // Estado da limpeza.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    private bool isCleaning;
    [ObservableProperty] private double cleanProgress;
    [ObservableProperty] private string currentCategoryText = "";
    [ObservableProperty] private string realtimeFreedText = "";
    [ObservableProperty] private string remainingTimeText = "";

    // Resultado.
    [ObservableProperty] private bool hasResult;
    [ObservableProperty] private string resultMessage = "";
    [ObservableProperty] private string resultDetails = "";

    // Explicação recolhível.
    [ObservableProperty] private bool isExplainerExpanded;

    // Modo "Limpeza Inteligente": busca de arquivos grandes.
    [ObservableProperty] private bool isLargeFileMode;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    private bool isScanningLargeFiles;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    private bool isDeletingLarge;
    [ObservableProperty] private bool hasScannedLargeFiles;
    [ObservableProperty] private bool largeFilesEmpty;
    [ObservableProperty] private string selectedLargeText = Loc.S("DiskCleanup_NoFileSelected");
    [ObservableProperty] private bool hasApps;

    // Controla se o cartão está expandido (mostra análise/varredura/resultado).
    [ObservableProperty] private bool isExpanded;

    // True enquanto qualquer operação está em curso (desabilita os botões de ação).
    public bool IsBusy => IsAnalyzing || IsCleaning || IsScanningLargeFiles || IsDeletingLarge;

    public ObservableCollection<CleanupCategoryViewModel> Categories { get; } = new();
    public ObservableCollection<LargeFileViewModel> LargeFiles { get; } = new();
    public ObservableCollection<InstalledAppViewModel> InstalledApps { get; } = new();

    public DiskCleanupViewModel(DiskCleanupService service)
    {
        _service = service;
        RefreshDiskSpace();
    }

    private void RefreshDiskSpace()
    {
        var info = _service.GetSystemDriveSpace();
        DiskTotalText = ByteFormatter.Format(info.TotalBytes);
        DiskUsedText = ByteFormatter.Format(info.UsedBytes);
        DiskFreeText = ByteFormatter.Format(info.FreeBytes);
        DiskUsagePercent = info.UsagePercent;
    }

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        if (IsAnalyzing || IsCleaning) return;

        IsAnalyzing = true;
        HasResult = false;
        Categories.Clear();
        IsLargeFileMode = false;
        HasScannedLargeFiles = false;
        LargeFiles.Clear();

        try
        {
            _definitions = _service.GetCategories();
            var results = await _service.AnalyzeAsync(_definitions);
            var byKey = results.ToDictionary(r => r.Key);

            foreach (var def in _definitions)
            {
                if (!byKey.TryGetValue(def.Key, out var result)) continue;

                // A Lixeira aparece mesmo vazia; as demais só aparecem se tiverem algo.
                if (!def.IsRecycleBin && result.FileCount == 0) continue;

                var vm = new CleanupCategoryViewModel(def, result) { SelectionChanged = OnSelectionChanged };
                // Pré-seleciona o que é totalmente seguro.
                vm.IsSelected = def.Safety == CleanupSafety.TotallySafe && result.Bytes > 0;
                Categories.Add(vm);
            }

            long totalBytes = Categories.Sum(c => c.Bytes);
            int totalFiles = Categories.Sum(c => c.FileCount);
            RecoverableText = ByteFormatter.Format(totalBytes);
            FileCountText = Loc.F("DiskCleanup_FilesCount", totalFiles.ToString("N0"));
            HasAnalyzed = true;
            IsExpanded = true;
            RefreshDiskSpace();
            OnSelectionChanged();
        }
        finally
        {
            IsAnalyzing = false;
            CleanCommand.NotifyCanExecuteChanged();
        }
    }

    private void OnSelectionChanged()
    {
        long selected = Categories.Where(c => c.IsSelected).Sum(c => c.Bytes);
        SelectedRecoverableText = selected > 0
            ? Loc.F("DiskCleanup_EstimatedToRecover", ByteFormatter.Format(selected))
            : Loc.S("DiskCleanup_NothingSelected");
        CleanCommand.NotifyCanExecuteChanged();
    }

    private bool CanClean() => HasAnalyzed && !IsCleaning && !IsAnalyzing && Categories.Any(c => c.IsSelected);

    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task CleanAsync()
    {
        if (!CanClean()) return;

        var selectedKeys = Categories.Where(c => c.IsSelected).Select(c => c.Key).ToHashSet();
        var selectedDefs = _definitions.Where(d => selectedKeys.Contains(d.Key)).ToList();

        IsCleaning = true;
        HasResult = false;
        CleanProgress = 0;
        CurrentCategoryText = "";
        RealtimeFreedText = "";
        RemainingTimeText = "";

        var sw = Stopwatch.StartNew();
        var progress = new Progress<CleanupProgress>(p =>
        {
            CleanProgress = p.Fraction * 100;
            CurrentCategoryText = Loc.F("DiskCleanup_Cleaning", p.CurrentCategory);
            RealtimeFreedText = Loc.F("DiskCleanup_FreedSoFar", ByteFormatter.Format(p.FreedBytes), p.FilesProcessed.ToString("N0"));

            double elapsed = sw.Elapsed.TotalSeconds;
            if (p.Fraction > 0.02 && p.Fraction < 1)
            {
                double remaining = elapsed * (1 - p.Fraction) / p.Fraction;
                RemainingTimeText = remaining >= 1 ? Loc.F("DiskCleanup_TimeRemaining", $"{remaining:0}") : Loc.S("DiskCleanup_AlmostThere");
            }
            else
            {
                RemainingTimeText = "";
            }
        });

        try
        {
            var result = await _service.CleanAsync(selectedDefs, progress);

            ResultMessage = result.FilesRemoved > 0
                ? Loc.F("DiskCleanup_SuccessMessage", ByteFormatter.Format(result.FreedBytes))
                : Loc.S("DiskCleanup_NothingToRemoveMessage");

            string cats = result.CleanedCategories.Count > 0 ? string.Join(", ", result.CleanedCategories) : "—";
            ResultDetails = Loc.F("DiskCleanup_ResultDetails",
                result.FilesRemoved.ToString("N0"), $"{result.Elapsed.TotalSeconds:0.0}", $"{result.FinishedAt:HH:mm:ss}", cats);
            HasResult = true;
        }
        catch
        {
            ResultMessage = Loc.S("DiskCleanup_FailedMessage");
            ResultDetails = "";
            HasResult = true;
        }
        finally
        {
            IsCleaning = false;
            CleanProgress = 0;
            HasAnalyzed = false;
            Categories.Clear();
            IsExpanded = true; // mantém o resultado visível; "Recuar" recolhe.
            RefreshDiskSpace();
            CleanCommand.NotifyCanExecuteChanged();
        }
    }

    // "Limpeza Inteligente": procura arquivos grandes nas pastas pessoais e sugere apagar.
    [RelayCommand]
    private async Task SmartCleanAsync()
    {
        if (IsBusy) return;

        IsScanningLargeFiles = true;
        IsLargeFileMode = true;
        HasAnalyzed = false;
        Categories.Clear();
        HasResult = false;
        HasScannedLargeFiles = false;
        LargeFiles.Clear();
        InstalledApps.Clear();

        try
        {
            const long minBytes = 100L * 1024 * 1024; // 100 MB
            var files = await _service.ScanLargeFilesAsync(minBytes, 100);

            foreach (var f in files)
                LargeFiles.Add(new LargeFileViewModel(f) { SelectionChanged = OnLargeSelectionChanged });

            // Aplicativos instalados (por tamanho) — sugestão de desinstalação dos maiores.
            var apps = await _service.GetInstalledApplicationsAsync();
            foreach (var a in apps.Take(40))
                InstalledApps.Add(new InstalledAppViewModel(a));
            HasApps = InstalledApps.Count > 0;

            LargeFilesEmpty = LargeFiles.Count == 0;
            HasScannedLargeFiles = true;
            IsExpanded = true;
            OnLargeSelectionChanged();
        }
        finally
        {
            IsScanningLargeFiles = false;
        }
    }

    [RelayCommand]
    private void UninstallApp(InstalledAppViewModel? app)
    {
        if (app is null) return;

        var confirm = System.Windows.MessageBox.Show(
            Loc.F("DiskCleanup_UninstallConfirmMessage", app.Name),
            Loc.S("DiskCleanup_UninstallAppTitle"), System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        try
        {
            _service.LaunchUninstaller(app.UninstallCommand);
        }
        catch
        {
            System.Windows.MessageBox.Show(
                Loc.S("DiskCleanup_UninstallFailedMessage"),
                Loc.S("DiskCleanup_UninstallAppTitle"), System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    private void OnLargeSelectionChanged()
    {
        var selected = LargeFiles.Where(f => f.IsSelected).ToList();
        long bytes = selected.Sum(f => f.Bytes);
        SelectedLargeText = selected.Count > 0
            ? Loc.F("DiskCleanup_SelectedCount", selected.Count, ByteFormatter.Format(bytes))
            : Loc.S("DiskCleanup_NoFileSelected");
        DeleteLargeFilesCommand.NotifyCanExecuteChanged();
    }

    private bool CanDeleteLarge() => HasScannedLargeFiles && !IsBusy && LargeFiles.Any(f => f.IsSelected);

    [RelayCommand(CanExecute = nameof(CanDeleteLarge))]
    private async Task DeleteLargeFilesAsync()
    {
        var selected = LargeFiles.Where(f => f.IsSelected).ToList();
        if (selected.Count == 0) return;

        long totalBytes = selected.Sum(f => f.Bytes);
        var confirm = System.Windows.MessageBox.Show(
            Loc.F("DiskCleanup_DeleteConfirmMessage", selected.Count, ByteFormatter.Format(totalBytes)),
            Loc.S("DiskCleanup_ConfirmDeletionTitle"), System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        IsDeletingLarge = true;
        try
        {
            var payload = selected.Select(f => (f.FullPath, f.Bytes)).ToList();
            var (freed, removed) = await _service.RecycleFilesAsync(payload);

            foreach (var f in selected.Where(f => !System.IO.File.Exists(f.FullPath)).ToList())
                LargeFiles.Remove(f);

            ResultMessage = removed > 0
                ? Loc.F("DiskCleanup_LargeFilesRemovedMessage", removed, ByteFormatter.Format(freed))
                : Loc.S("DiskCleanup_NoFileRemovedMessage");
            ResultDetails = "";
            HasResult = true;
            LargeFilesEmpty = LargeFiles.Count == 0;
            RefreshDiskSpace();
            OnLargeSelectionChanged();
        }
        finally
        {
            IsDeletingLarge = false;
            DeleteLargeFilesCommand.NotifyCanExecuteChanged();
        }
    }

    // "Recuar": recolhe o cartão de volta ao estado inicial.
    [RelayCommand]
    private void Collapse()
    {
        HasAnalyzed = false;
        Categories.Clear();
        HasScannedLargeFiles = false;
        IsLargeFileMode = false;
        LargeFiles.Clear();
        InstalledApps.Clear();
        HasApps = false;
        LargeFilesEmpty = false;
        HasResult = false;
        IsExpanded = false;
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var c in Categories)
            c.IsSelected = c.Bytes > 0 || c.IsRecycleBin;
        OnSelectionChanged();
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var c in Categories)
            c.IsSelected = false;
        OnSelectionChanged();
    }
}

public partial class CleanupCategoryViewModel : ObservableObject
{
    public string Key { get; }
    public string Title { get; }
    public string Description { get; }
    public CleanupSafety Safety { get; }
    public bool IsRecycleBin { get; }
    public long Bytes { get; }
    public int FileCount { get; }
    public string SizeText { get; }
    public string SafetyLabel { get; }
    public ObservableCollection<CleanupDetailViewModel> Details { get; } = new();

    public Action? SelectionChanged { get; set; }

    [ObservableProperty] private bool isSelected;
    [ObservableProperty] private bool isExpanded;

    public CleanupCategoryViewModel(CleanupCategoryDefinition def, CleanupCategoryResult result)
    {
        Key = def.Key;
        Title = def.Title;
        Description = def.Description;
        Safety = def.Safety;
        IsRecycleBin = def.IsRecycleBin;
        Bytes = result.Bytes;
        FileCount = result.FileCount;
        SizeText = ByteFormatter.Format(result.Bytes);
        SafetyLabel = def.Safety switch
        {
            CleanupSafety.TotallySafe => "🟢 " + Loc.S("DiskCleanup_SafetyTotallySafe"),
            CleanupSafety.Safe => "🟡 " + Loc.S("DiskCleanup_SafetySafe"),
            _ => "🔴 " + Loc.S("DiskCleanup_SafetyRequiresConfirmation")
        };

        foreach (var d in result.Details)
            Details.Add(new CleanupDetailViewModel(d.Label, ByteFormatter.Format(d.Bytes), d.FileCount));
    }

    partial void OnIsSelectedChanged(bool value) => SelectionChanged?.Invoke();
}

public class CleanupDetailViewModel
{
    public string Label { get; }
    public string SizeText { get; }
    public string CountText { get; }

    public CleanupDetailViewModel(string label, string sizeText, int count)
    {
        Label = label;
        SizeText = sizeText;
        CountText = Loc.F("DiskCleanup_FilesCount", count.ToString("N0"));
    }
}

public partial class LargeFileViewModel : ObservableObject
{
    public string FullPath { get; }
    public string FileName { get; }
    public string FolderText { get; }
    public string SizeText { get; }
    public string ModifiedText { get; }
    public long Bytes { get; }

    public Action? SelectionChanged { get; set; }

    [ObservableProperty] private bool isSelected;

    public LargeFileViewModel(LargeFileInfo info)
    {
        FullPath = info.Path;
        FileName = System.IO.Path.GetFileName(info.Path);
        FolderText = System.IO.Path.GetDirectoryName(info.Path) ?? "";
        Bytes = info.Bytes;
        SizeText = ByteFormatter.Format(info.Bytes);
        ModifiedText = info.LastModified.ToString("dd/MM/yyyy");
    }

    partial void OnIsSelectedChanged(bool value) => SelectionChanged?.Invoke();
}

public class InstalledAppViewModel
{
    public string Name { get; }
    public string SizeText { get; }
    public string SubtitleText { get; }
    public string UninstallCommand { get; }

    public InstalledAppViewModel(InstalledAppInfo info)
    {
        Name = info.Name;
        SizeText = ByteFormatter.Format(info.Bytes);
        UninstallCommand = info.UninstallCommand;

        string publisher = string.IsNullOrWhiteSpace(info.Publisher) ? "" : info.Publisher;
        string date = info.InstallDate is { } d ? Loc.F("DiskCleanup_InstalledOn", $"{d:dd/MM/yyyy}") : "";
        SubtitleText = string.Join(" • ", new[] { publisher, date }.Where(s => s.Length > 0));
    }
}

internal static class ByteFormatter
{
    public static string Format(long bytes)
    {
        if (bytes <= 0) return "0 MB";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        int i = 0;
        while (value >= 1024 && i < units.Length - 1)
        {
            value /= 1024;
            i++;
        }
        return i == 0 ? $"{value:0} {units[i]}" : $"{value:0.0} {units[i]}";
    }
}
