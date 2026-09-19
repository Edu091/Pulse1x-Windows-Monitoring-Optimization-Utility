using System.IO;
using System.Text.Json;
using Pulse1x.App.Models.WinCustom;

namespace Pulse1x.App.Services.WinCustom;

/// <summary>
/// Uma tentativa de aplicar personalização a um alvo, registrada em disco ANTES de a aplicação
/// acontecer. Se o Pulse (ou o Explorer) morrer durante a tentativa, o registro sobrevive — é
/// assim que a proteção descobre, na volta, que aquele alvo é perigoso.
/// </summary>
public class ApplyAttempt
{
    public string Target { get; set; } = "";
    /// <summary>Tentativas consecutivas que não chegaram a ser confirmadas como bem-sucedidas.</summary>
    public int FailureCount { get; set; }
    public DateTime LastAttempt { get; set; } = DateTime.Now;
    /// <summary>Alvo bloqueado pela proteção — não será mais aplicado até o usuário liberar.</summary>
    public bool Quarantined { get; set; }
    public string? LastError { get; set; }
}

/// <summary>
/// Proteção contra falhas: impede que uma personalização problemática entre num ciclo infinito de
/// crash e reaplicação.
///
/// Funciona como uma "caixa-preta". Antes de aplicar qualquer coisa a um alvo, o motor chama
/// <see cref="BeginAttempt"/>, que grava a tentativa em disco na hora. Se a aplicação terminar
/// bem, <see cref="CommitSuccess"/> zera o contador. Se o processo morrer no meio — Explorer
/// caindo, Menu Iniciar travando, reinício forçado — a tentativa fica registrada como não
/// confirmada e, na próxima abertura, o contador daquele alvo já sobe sozinho.
///
/// Ao alcançar <see cref="MaxFailures"/> o alvo é posto em quarentena: o motor para de aplicá-lo
/// e o restaura ao padrão, mantendo os demais componentes funcionando normalmente. É exatamente o
/// comportamento pedido — interromper a personalização culpada e restaurar o componente afetado,
/// sem derrubar o sistema inteiro.
/// </summary>
public class CustomizationWatchdog
{
    /// <summary>Falhas consecutivas toleradas antes da quarentena.</summary>
    public const int MaxFailures = 3;

    private readonly string _filePath;
    private readonly Dictionary<string, ApplyAttempt> _attempts = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    /// <summary>Avisa quando um alvo entra em quarentena, para a interface poder informar.</summary>
    public event Action<string>? TargetQuarantined;

    public CustomizationWatchdog(string fileName = "wincustom-watchdog.json")
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pulse1x");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, fileName);
        Load();
        PromoteUnconfirmedAttempts();
    }

    // =====================================================================================
    //  Ciclo de uma tentativa
    // =====================================================================================

    /// <summary>
    /// Registra que vamos tentar aplicar algo a <paramref name="target"/>. Devolve false se o
    /// alvo estiver em quarentena — nesse caso o motor deve pular o alvo em silêncio.
    /// </summary>
    public bool BeginAttempt(string target)
    {
        lock (_gate)
        {
            var attempt = GetOrCreate(target);
            if (attempt.Quarantined) return false;

            attempt.LastAttempt = DateTime.Now;
            // "InFlight" é representado por uma falha otimista: ela é desfeita pelo
            // CommitSuccess. Se o processo morrer antes, a falha permanece — que é o ponto.
            attempt.FailureCount++;
            Save();

            if (attempt.FailureCount >= MaxFailures)
            {
                attempt.Quarantined = true;
                Save();
                TargetQuarantined?.Invoke(target);
                return false;
            }

            return true;
        }
    }

    /// <summary>Confirma que a aplicação terminou sem derrubar nada — zera o contador do alvo.</summary>
    public void CommitSuccess(string target)
    {
        lock (_gate)
        {
            var attempt = GetOrCreate(target);
            attempt.FailureCount = 0;
            attempt.LastError = null;
            Save();
        }
    }

    /// <summary>Registra uma falha explícita (a API recusou, a janela sumiu).</summary>
    public void RecordFailure(string target, string? error)
    {
        lock (_gate)
        {
            var attempt = GetOrCreate(target);
            attempt.LastError = error;
            Save();

            if (attempt.FailureCount >= MaxFailures && !attempt.Quarantined)
            {
                attempt.Quarantined = true;
                Save();
                TargetQuarantined?.Invoke(target);
            }
        }
    }

    // =====================================================================================
    //  Consulta e liberação
    // =====================================================================================

    public bool IsQuarantined(string target)
    {
        lock (_gate) return _attempts.TryGetValue(target, out var a) && a.Quarantined;
    }

    public IReadOnlyList<ApplyAttempt> Quarantined()
    {
        lock (_gate) return _attempts.Values.Where(a => a.Quarantined).ToList();
    }

    /// <summary>Tira um alvo da quarentena — o usuário assumindo que quer tentar de novo.</summary>
    public void Release(string target)
    {
        lock (_gate)
        {
            if (!_attempts.TryGetValue(target, out var attempt)) return;
            attempt.Quarantined = false;
            attempt.FailureCount = 0;
            attempt.LastError = null;
            Save();
        }
    }

    /// <summary>Libera todos — usado depois de uma restauração completa.</summary>
    public void ReleaseAll()
    {
        lock (_gate)
        {
            _attempts.Clear();
            Save();
        }
    }

    // =====================================================================================
    //  Persistência
    // =====================================================================================

    private ApplyAttempt GetOrCreate(string target)
    {
        if (!_attempts.TryGetValue(target, out var attempt))
        {
            attempt = new ApplyAttempt { Target = target };
            _attempts[target] = attempt;
        }
        return attempt;
    }

    /// <summary>
    /// Na abertura, qualquer tentativa que ficou sem confirmação já está contada como falha (o
    /// BeginAttempt incrementa antes de aplicar). Aqui apenas promovemos à quarentena as que
    /// passaram do limite enquanto o app estava fechado — o caso do crash em cadeia.
    /// </summary>
    private void PromoteUnconfirmedAttempts()
    {
        lock (_gate)
        {
            bool changed = false;
            foreach (var attempt in _attempts.Values)
            {
                if (attempt.Quarantined || attempt.FailureCount < MaxFailures) continue;
                attempt.Quarantined = true;
                changed = true;
            }
            if (changed) Save();
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return;
            var list = JsonSerializer.Deserialize<List<ApplyAttempt>>(File.ReadAllText(_filePath));
            if (list is null) return;
            foreach (var attempt in list)
                if (!string.IsNullOrEmpty(attempt.Target))
                    _attempts[attempt.Target] = attempt;
        }
        catch
        {
            // Sem histórico confiável, começamos limpos — a proteção volta a aprender do zero.
        }
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(_filePath,
                JsonSerializer.Serialize(_attempts.Values.ToList(), new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* a proteção em memória continua valendo nesta sessão */ }
    }
}
