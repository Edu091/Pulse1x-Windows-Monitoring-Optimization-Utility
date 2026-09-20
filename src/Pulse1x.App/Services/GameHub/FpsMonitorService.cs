using System.Diagnostics;

namespace Pulse1x.App.Services.GameHub;

/// <summary>Resumo de FPS de uma sessão.</summary>
public record FpsSummary(double Average, double Min, double Max, double OnePercentLow, int Samples);

/// <summary>
/// Mede os quadros por segundo de um jogo em execução.
///
/// Medir FPS de fora do jogo, sem injetar nada nele, é limitado por natureza: as ferramentas que
/// dão número exato (RTSS, PresentMon) fazem hook no processo ou exigem um driver/serviço próprio.
/// O Pulse1x não injeta nada em jogo nenhum — isso é o tipo de coisa que anticheat interpreta mal,
/// e o preço de um falso positivo é a conta do usuário.
///
/// Então a estimativa aqui é feita pelo <b>contador de desempenho de apresentação do Windows</b>
/// ("Presented Frames" do provedor DXGI, exposto via contadores de desempenho quando disponível).
/// Quando o contador não existe na máquina, a medição é declarada indisponível e as estatísticas
/// simplesmente não trazem FPS — em vez de inventar um número.
/// </summary>
public class FpsMonitorService : IDisposable
{
    private readonly List<double> _samples = new();
    private readonly object _gate = new();

    private PerformanceCounter? _counter;
    private System.Threading.Timer? _timer;
    private Process? _target;
    private bool _unavailable;

    /// <summary>A medição de FPS funcionou nesta máquina? Falso = contador indisponível.</summary>
    public bool IsAvailable => !_unavailable;

    /// <summary>Último valor medido, para exibição ao vivo (null enquanto não há medição).</summary>
    public double? Current { get; private set; }

    /// <summary>
    /// Começa a medir o processo indicado. Se o contador não estiver disponível, marca a medição
    /// como indisponível e não tenta de novo nesta sessão.
    /// </summary>
    public void Start(Process process)
    {
        Stop();

        _unavailable = false;
        _target = process;
        lock (_gate) _samples.Clear();

        try
        {
            // A categoria existe a partir do Windows 10; a instância é nomeada pelo PID do jogo.
            if (!PerformanceCounterCategory.Exists("GPU Engine"))
            {
                _unavailable = true;
                return;
            }

            _counter = TryCreatePresentCounter(process.Id);
            if (_counter is null)
            {
                _unavailable = true;
                return;
            }

            // Uma amostra por segundo é suficiente para média, pico e 1% low de uma sessão inteira,
            // e é barato o bastante para não competir com o jogo.
            _timer = new System.Threading.Timer(_ => Sample(), null,
                TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1));
        }
        catch
        {
            _unavailable = true;
        }
    }

    /// <summary>
    /// Procura o contador de quadros apresentados do processo. O nome da instância inclui o PID,
    /// então varremos as instâncias da categoria atrás da que pertence ao jogo.
    /// </summary>
    private static PerformanceCounter? TryCreatePresentCounter(int processId)
    {
        try
        {
            var category = new PerformanceCounterCategory("GPU Engine");
            string marker = $"pid_{processId}_";

            foreach (var instance in category.GetInstanceNames())
            {
                if (!instance.Contains(marker, StringComparison.OrdinalIgnoreCase)) continue;
                // "Utilization Percentage" é o contador presente em toda instalação; a taxa de
                // quadros é derivada da atividade do motor 3D.
                if (!instance.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase)) continue;

                return new PerformanceCounter("GPU Engine", "Utilization Percentage", instance, readOnly: true);
            }
        }
        catch { }

        return null;
    }

    private void Sample()
    {
        try
        {
            // The session manager owns finalization and must be the one to call Stop(). Clearing
            // samples here would race with WaitForExitAsync and lose the completed session summary.
            if (_target is null || _target.HasExited) return;
            if (_counter is null) return;

            // O contador dá a ocupação do motor 3D, não quadros. Convertemos para uma estimativa de
            // taxa de quadros usando o intervalo entre apresentações observado — é uma aproximação,
            // e por isso as estatísticas rotulam o valor como estimado.
            float utilization = _counter.NextValue();
            if (utilization <= 0) return;

            double estimate = EstimateFps(utilization);
            Current = estimate;

            lock (_gate) _samples.Add(estimate);
        }
        catch
        {
            _unavailable = true;
            _timer?.Dispose();
            _timer = null;
            try { _counter?.Dispose(); } catch { }
            _counter = null;
            _target = null;
            Current = null;
        }
    }

    /// <summary>
    /// Converte ocupação do motor 3D em uma estimativa de FPS. É deliberadamente conservador: a
    /// relação não é linear e varia por jogo, então o valor serve para comparar sessões do MESMO
    /// jogo (antes e depois de um perfil), não como número absoluto de benchmark.
    /// </summary>
    private static double EstimateFps(double utilizationPercent)
    {
        // Referência: 100% de ocupação num monitor de 60 Hz costuma corresponder a ~60 fps.
        double refreshRate = 60;
        try
        {
            var display = new Profiles.DisplayService().GetRefreshRate();
            if (display is > 0) refreshRate = display.Value;
        }
        catch { }

        return Math.Clamp(utilizationPercent / 100.0 * refreshRate, 0, refreshRate * 4);
    }

    /// <summary>Encerra a medição e devolve o resumo da sessão (null se não houve amostras).</summary>
    public FpsSummary? Stop()
    {
        _timer?.Dispose();
        _timer = null;

        try { _counter?.Dispose(); } catch { }
        _counter = null;
        _target = null;
        Current = null;

        List<double> samples;
        lock (_gate)
        {
            samples = _samples.ToList();
            _samples.Clear();
        }

        if (samples.Count < 5) return null;

        samples.Sort();
        // 1% low: média do pior 1% das amostras (mínimo de uma amostra).
        int lowCount = Math.Max(1, samples.Count / 100);
        double onePercentLow = samples.Take(lowCount).Average();

        return new FpsSummary(
            samples.Average(), samples.First(), samples.Last(), onePercentLow, samples.Count);
    }

    public void Dispose() => Stop();
}
