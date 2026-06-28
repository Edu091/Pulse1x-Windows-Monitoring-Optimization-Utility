namespace Pulse1x.App.Models;

/// <summary>Nível de segurança de uma categoria de limpeza.</summary>
public enum CleanupSafety
{
    TotallySafe,        // 🟢 Totalmente Seguro
    Safe,               // 🟡 Seguro
    RequiresConfirmation // 🔴 Requer confirmação
}

/// <summary>
/// Um alvo concreto de varredura/limpeza: uma pasta + um filtro de arquivos.
/// </summary>
/// <param name="Label">Rótulo amigável exibido nos detalhes.</param>
/// <param name="Directory">Pasta a varrer (já com variáveis de ambiente expandidas).</param>
/// <param name="Pattern">Filtro de nome (ex.: "*", "*.log", "thumbcache_*.db").</param>
/// <param name="Recursive">Varre subpastas?</param>
/// <param name="MinAgeDays">Se &gt; 0, só considera arquivos mais antigos que N dias.</param>
/// <param name="RemoveEmptyDirs">Após apagar arquivos, remove subpastas vazias (não a raiz).</param>
public record CleanupTarget(
    string Label,
    string Directory,
    string Pattern,
    bool Recursive,
    int MinAgeDays,
    bool RemoveEmptyDirs);

/// <summary>Definição estática de uma categoria de limpeza.</summary>
public class CleanupCategoryDefinition
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public CleanupSafety Safety { get; init; }
    public bool IsRecycleBin { get; init; }
    public IReadOnlyList<CleanupTarget> Targets { get; init; } = Array.Empty<CleanupTarget>();
}

/// <summary>Item de detalhe (um alvo) com tamanho/contagem após a análise.</summary>
public record CleanupDetail(string Label, long Bytes, int FileCount);

/// <summary>Resultado da análise de uma categoria.</summary>
public record CleanupCategoryResult(string Key, long Bytes, int FileCount, IReadOnlyList<CleanupDetail> Details);

/// <summary>Progresso reportado durante a limpeza.</summary>
public record CleanupProgress(string CurrentCategory, double Fraction, long FreedBytes, int FilesProcessed);

/// <summary>Resultado final da limpeza.</summary>
public record CleanupRunResult(long FreedBytes, int FilesRemoved, IReadOnlyList<string> CleanedCategories, TimeSpan Elapsed, DateTime FinishedAt);

/// <summary>Espaço do disco do sistema.</summary>
public record DiskSpaceInfo(long TotalBytes, long UsedBytes, long FreeBytes, double UsagePercent);

/// <summary>Um arquivo grande encontrado na varredura de "Limpeza Inteligente".</summary>
public record LargeFileInfo(string Path, long Bytes, DateTime LastModified);

/// <summary>Um aplicativo instalado (do registro de desinstalação do Windows).</summary>
public record InstalledAppInfo(string Name, string Publisher, long Bytes, DateTime? InstallDate, string UninstallCommand);
