using System.IO;
using System.Text.Json;

namespace Pulse1x.App.Services;

/// <summary>
/// Tipo de alteração registrada — define como a reversão é feita.
/// Serviços do Windows são desativados gravando o valor "Start" no Registro, portanto
/// também viram alterações do tipo <see cref="Registry"/> (a reversão restaura o Start).
/// </summary>
public enum ChangeKind
{
    Registry,
    Task,
    PowerCfg,
}

/// <summary>
/// Uma única alteração feita por uma otimização avançada. Guarda tudo o que é preciso para
/// desfazer a mudança individualmente: a chave exata, o valor antigo e o novo. É o registro
/// de auditoria exigido pelo "Sistema de Reversão".
/// </summary>
public class OptimizationChange
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OptimizationId { get; set; } = "";
    public string OptimizationTitle { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.Now;

    public ChangeKind Kind { get; set; } = ChangeKind.Registry;

    // ---- Registro ----
    public string Hive { get; set; } = "";      // "HKLM" | "HKCU"
    public string KeyPath { get; set; } = "";    // caminho da subchave (ou nome do serviço/tarefa/plano)
    public string ValueName { get; set; } = "";  // nome do valor (ou subtipo do PowerCfg)
    public string ValueKind { get; set; } = "DWord"; // "DWord" | "String"

    public string? OldValue { get; set; }        // null = o valor não existia antes
    public string? NewValue { get; set; }        // null = o valor foi removido

    public bool Reverted { get; set; }
    public DateTime? RevertedAt { get; set; }

    /// <summary>Descrição amigável do alvo da alteração, para exibição na tela.</summary>
    public string DisplayTarget => Kind switch
    {
        ChangeKind.Registry => $"{Hive}\\{KeyPath}\\{ValueName}",
        ChangeKind.Task => $"Tarefa agendada: {KeyPath}",
        ChangeKind.PowerCfg => "Plano de energia do Windows",
        _ => KeyPath,
    };

    /// <summary>Resumo "antes → depois" do valor alterado.</summary>
    public string DisplayChange => $"{OldValue ?? "(ausente)"} → {NewValue ?? "(removido)"}";
}

/// <summary>
/// Registro de auditoria e reversão de todas as alterações feitas pelas Otimizações Avançadas.
/// Cada alteração é persistida em disco (AppData\Pulse1x\optimization-changes.json) para que o
/// usuário possa desfazer qualquer mudança — individualmente ou todas — mesmo após reabrir o app.
/// </summary>
public class OptimizationChangeLog
{
    private readonly string _filePath;
    private readonly List<OptimizationChange> _changes = new();
    private readonly object _gate = new();

    // fileName permite registros de reversão separados por área (ex.: "network-changes.json"
    // para a categoria Latência), de modo que "Desfazer Tudo" de uma área não mexa na outra.
    public OptimizationChangeLog(string fileName = "optimization-changes.json")
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pulse1x");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, fileName);
        Load();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var list = JsonSerializer.Deserialize<List<OptimizationChange>>(json);
                if (list is not null)
                {
                    _changes.Clear();
                    _changes.AddRange(list);
                }
            }
        }
        catch
        {
            // Arquivo corrompido/inacessível: começa vazio (nenhuma alteração conhecida).
        }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_changes, new JsonSerializerOptions { WriteIndented = true });
            AtomicFile.WriteAllText(_filePath, json);
        }
        catch
        {
            // Sem permissão/disco cheio: a alteração em memória ainda é válida nesta sessão.
        }
    }

    public void Record(OptimizationChange change)
    {
        lock (_gate)
        {
            _changes.Add(change);
            Save();
        }
    }

    /// <summary>Alterações ainda ativas (não revertidas) de uma otimização.</summary>
    public IReadOnlyList<OptimizationChange> GetActive(string optimizationId)
    {
        lock (_gate)
            return _changes.Where(c => c.OptimizationId == optimizationId && !c.Reverted).ToList();
    }

    public bool HasActiveChanges(string optimizationId)
    {
        lock (_gate)
            return _changes.Any(c => c.OptimizationId == optimizationId && !c.Reverted);
    }

    /// <summary>Todas as alterações ativas, mais recentes primeiro — para o histórico de reversão.</summary>
    public IReadOnlyList<OptimizationChange> GetAllActive()
    {
        lock (_gate)
            return _changes.Where(c => !c.Reverted).OrderByDescending(c => c.Timestamp).ToList();
    }

    public void MarkReverted(OptimizationChange change)
    {
        lock (_gate)
        {
            change.Reverted = true;
            change.RevertedAt = DateTime.Now;
            Save();
        }
    }
}
