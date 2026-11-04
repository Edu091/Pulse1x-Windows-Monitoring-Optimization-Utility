using System.IO;
using System.Text.Json;

namespace Pulse1x.App.Services.Benchmark;

/// <summary>
/// Histórico das execuções do benchmark (benchmark-history.json, ao lado do settings.json). Serve
/// para comparar o PC antes e depois de uma otimização, de uma troca de peça ou de um driver novo.
/// </summary>
public sealed class BenchmarkHistoryService
{
    private const int MaxRuns = 30;
    private readonly string _path;
    private readonly object _gate = new();
    private List<BenchmarkRun> _runs;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public BenchmarkHistoryService(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pulse1x", "benchmark-history.json");
        _runs = Load();
    }

    /// <summary>Execuções da mais recente para a mais antiga.</summary>
    public IReadOnlyList<BenchmarkRun> Runs
    {
        get { lock (_gate) return _runs.OrderByDescending(r => r.When).ToList(); }
    }

    public void Add(BenchmarkRun run)
    {
        lock (_gate)
        {
            _runs.Add(run);
            _runs = _runs.OrderByDescending(r => r.When).Take(MaxRuns).ToList();
            Save();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _runs.Clear();
            Save();
        }
    }

    /// <summary>Resultado mais recente de uma área, opcionalmente ignorando a execução atual.</summary>
    public BenchmarkResult? Latest(BenchmarkKind kind, BenchmarkRun? except = null) =>
        Runs.Where(r => !ReferenceEquals(r, except)).Select(r => r.Get(kind)).FirstOrDefault(r => r is not null);

    private List<BenchmarkRun> Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<List<BenchmarkRun>>(File.ReadAllText(_path), Json) ?? new();
        }
        catch { /* histórico corrompido: recomeça vazio em vez de impedir o benchmark */ }
        return new();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(_runs, Json));
        }
        catch { /* sem disco gravável o resultado só não fica no histórico */ }
    }
}
