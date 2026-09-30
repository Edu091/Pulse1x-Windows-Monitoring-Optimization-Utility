using System.Diagnostics;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace Pulse1x.App.Services.GameHub;

/// <summary>Resumo de FPS de uma sessão. <paramref name="IsEstimated"/> = veio da ocupação da GPU,
/// não da contagem de quadros.</summary>
public record FpsSummary(double Average, double Min, double Max, double OnePercentLow, int Samples,
    bool IsEstimated = false);

/// <summary>
/// Mede os quadros por segundo de um jogo em execução.
///
/// O Pulse1x não injeta nada em jogo nenhum — é o tipo de coisa que anticheat interpreta mal, e o
/// preço de um falso positivo é a conta do usuário. Então o FPS é medido como o PresentMon faz:
/// uma sessão ETW em tempo real escuta os eventos que o próprio Windows emite a cada Present()
/// (provedores Microsoft-Windows-DXGI e Microsoft-Windows-D3D9), filtrados pelo PID do jogo. Cada
/// evento é um quadro entregue; contar quadros por segundo e medir o intervalo entre eles dá FPS e
/// 1% low reais, sem tocar no processo do jogo. O app roda elevado, que é o que o ETW exige.
///
/// Jogos que não apresentam via DirectX (alguns OpenGL/Vulkan) não geram esses eventos. Para eles,
/// depois de alguns segundos sem quadros, o valor volta a ser a estimativa antiga pela ocupação do
/// motor 3D da GPU — marcada como estimada, para as estatísticas não a venderem como medição.
/// </summary>
public class FpsMonitorService : IDisposable
{
    // Nome fixo: uma sessão ETW sobrevive ao processo que a criou. Se o Pulse1x cair com a sessão
    // aberta, o nome conhecido permite encerrá-la na próxima vez em vez de acumular sessões órfãs
    // (o Windows tem um limite global delas).
    private const string SessionName = "Pulse1x-FPS";

    private static readonly Guid DxgiProvider = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    private static readonly Guid D3D9Provider = new("783ACA0A-790E-4D7F-8451-AA850511C6B9");
    private const int DxgiPresentStart = 42;
    private const int DxgiPresentMultiplaneOverlayStart = 55;
    private const int D3D9PresentStart = 1;

    // Palavras-chave "Events" (0x1) e "Analytic" (bit 63): os eventos de Present ficam no canal
    // analítico e só são emitidos com esse bit ligado — são as mesmas que o PresentMon usa.
    private const ulong PresentKeywords = 0x8000000000000001;

    // DXGI_PRESENT_TEST: o jogo só pergunta se a janela está visível; nenhum quadro é mostrado.
    private const uint DxgiPresentTest = 0x1;

    /// <summary>Silêncio de quadros depois do qual o valor ao vivo passa a ser a estimativa.</summary>
    private static readonly TimeSpan PresentSilence = TimeSpan.FromSeconds(3);

    private readonly object _gate = new();
    private readonly Dictionary<(Guid Provider, ulong SwapChain), FrameTimeStatistics> _swapChains = new();
    private readonly List<double> _estimateSamples = new();
    // Uma instância só: antes era criado um DisplayService a cada amostra, uma vez por segundo.
    private readonly Profiles.DisplayService _display = new();

    private TraceEventSession? _session;
    private Thread? _etwThread;
    private PerformanceCounter? _counter;
    private System.Threading.Timer? _timer;
    private Process? _target;
    private int _targetPid;
    private long _startedTick;
    private long _lastPresentTick;
    private long _lastCounterAttemptTick;
    private bool _etwUnavailable;
    private bool _counterUnavailable;

    public FpsMonitorService()
    {
        // Sessão deixada para trás por uma execução anterior que caiu: encerra já na abertura.
        StopStaleSession();

        // Rede de segurança para saídas que não passam pelo OnExit do WPF (Environment.Exit, por
        // exemplo): a sessão ETW não pode ficar aberta depois que o app some.
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { StopEtw(); } catch { }
        };
    }

    /// <summary>A medição de FPS funcionou nesta máquina? Falso = nem ETW nem contador disponíveis.</summary>
    public bool IsAvailable => !_etwUnavailable || !_counterUnavailable;

    /// <summary>Último valor medido, para exibição ao vivo (null enquanto não há medição).</summary>
    public double? Current { get; private set; }

    /// <summary>O valor ao vivo é a estimativa pela GPU (sem eventos de Present do jogo)?</summary>
    public bool IsEstimated { get; private set; }

    /// <summary>
    /// Começa a medir o processo indicado: abre a sessão ETW para o PID dele e liga a amostragem de
    /// um em um segundo que alimenta o valor ao vivo (e a estimativa, se o ETW ficar mudo).
    /// </summary>
    public void Start(Process process)
    {
        Stop();

        _target = process;
        _targetPid = process.Id;
        _startedTick = Environment.TickCount64;
        _lastPresentTick = 0;
        _lastCounterAttemptTick = 0;
        _etwUnavailable = false;
        _counterUnavailable = false;
        IsEstimated = false;

        StartEtw(process.Id);

        // Uma amostra por segundo basta para o valor ao vivo e para a estimativa, e é barato o
        // bastante para não competir com o jogo. A contagem de quadros em si não depende dela.
        _timer = new System.Threading.Timer(_ => Sample(), null,
            TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1));
    }

    // =====================================================================================
    //  ETW
    // =====================================================================================

    private void StartEtw(int processId)
    {
        TraceEventSession? session = null;
        try
        {
            StopStaleSession();

            session = new TraceEventSession(SessionName) { StopOnDispose = true };

            // Filtro por PID no próprio kernel (Windows 8.1+): os eventos de outros processos nem
            // chegam a este app. O callback confere de novo, por garantia.
            session.EnableProvider(DxgiProvider, TraceEventLevel.Informational, PresentKeywords,
                new TraceEventProviderOptions
                {
                    ProcessIDFilter = new List<int> { processId },
                    EventIDsToEnable = new List<int> { DxgiPresentStart, DxgiPresentMultiplaneOverlayStart },
                });
            session.EnableProvider(D3D9Provider, TraceEventLevel.Informational, PresentKeywords,
                new TraceEventProviderOptions
                {
                    ProcessIDFilter = new List<int> { processId },
                    EventIDsToEnable = new List<int> { D3D9PresentStart },
                });

            var source = session.Source;
            source.AllEvents += OnEvent;

            _session = session;

            // Process() bloqueia até a sessão ser encerrada: roda numa thread própria, fora da
            // interface. Em segundo plano para nunca segurar o fechamento do app.
            _etwThread = new Thread(() =>
            {
                try { source.Process(); }
                catch { /* sessão encerrada ou falhou — o timer cai para a estimativa */ }
            })
            {
                IsBackground = true,
                Name = "Pulse1x FPS (ETW)",
            };
            _etwThread.Start();
        }
        catch
        {
            // Sem permissão, limite de sessões do sistema ou provedor recusado: segue só com a
            // estimativa, e a sessão parcialmente criada não pode ficar aberta.
            _etwUnavailable = true;
            _session = null;
            try { session?.Dispose(); } catch { }
            StopStaleSession();
        }
    }

    private void OnEvent(TraceEvent data)
    {
        if (data.ProcessID != _targetPid) return;

        int id = (int)data.ID;
        Guid provider = data.ProviderGuid;
        bool isDxgi = provider == DxgiProvider &&
                      (id == DxgiPresentStart || id == DxgiPresentMultiplaneOverlayStart);
        bool isD3D9 = provider == D3D9Provider && id == D3D9PresentStart;
        if (!isDxgi && !isD3D9) return;

        // O primeiro campo dos dois eventos é o ponteiro da swap chain. Um jogo pode ter mais de
        // uma (launcher embutido, janela de ferramenta): cada uma é contada à parte e o resumo usa
        // a que mais apresentou — somar todas inflaria o FPS.
        ulong swapChain = 0;
        byte[] payload = data.EventData();
        int pointerSize = data.PointerSize;
        if (payload.Length >= pointerSize)
        {
            swapChain = pointerSize == 8
                ? BitConverter.ToUInt64(payload, 0)
                : BitConverter.ToUInt32(payload, 0);
        }
        if (isDxgi && payload.Length >= pointerSize + 4 &&
            (BitConverter.ToUInt32(payload, pointerSize) & DxgiPresentTest) != 0)
        {
            return;
        }

        double timestamp = data.TimeStampRelativeMSec;
        lock (_gate)
        {
            var key = (provider, swapChain);
            if (!_swapChains.TryGetValue(key, out var stats))
                _swapChains[key] = stats = new FrameTimeStatistics();
            stats.AddPresent(timestamp);
        }
        Interlocked.Exchange(ref _lastPresentTick, Environment.TickCount64);
    }

    private FrameTimeStatistics? DominantSwapChain()
    {
        lock (_gate)
        {
            return _swapChains.Count == 0
                ? null
                : _swapChains.Values.MaxBy(s => s.FrameCount);
        }
    }

    private void StopEtw()
    {
        var session = _session;
        var thread = _etwThread;
        _session = null;
        _etwThread = null;

        try { session?.Dispose(); } catch { }
        // Dispose encerra a sessão e faz o Process() retornar; esperamos um pouco para o último
        // lote de eventos entrar na contagem antes do resumo.
        try { thread?.Join(TimeSpan.FromSeconds(2)); } catch { }
        StopStaleSession();
    }

    /// <summary>Encerra uma sessão "Pulse1x-FPS" que tenha ficado aberta, se houver.</summary>
    private static void StopStaleSession()
    {
        try
        {
            if (!TraceEventSession.GetActiveSessionNames().Contains(SessionName)) return;
            using var stale = new TraceEventSession(SessionName, TraceEventSessionOptions.Attach);
            stale.Stop(noThrow: true);
        }
        catch { }
    }

    // =====================================================================================
    //  Valor ao vivo e estimativa
    // =====================================================================================

    private void Sample()
    {
        try
        {
            // The session manager owns finalization and must be the one to call Stop(). Clearing
            // samples here would race with WaitForExitAsync and lose the completed session summary.
            if (_target is null || _target.HasExited) return;

            long now = Environment.TickCount64;
            long lastPresent = Interlocked.Read(ref _lastPresentTick);
            if (lastPresent > 0 && now - lastPresent < PresentSilence.TotalMilliseconds)
            {
                Current = DominantSwapChain()?.CurrentFps ?? Current;
                IsEstimated = false;
                return;
            }

            // Nos primeiros segundos o jogo pode simplesmente ainda não ter apresentado nada; só
            // depois do silêncio a estimativa entra no lugar.
            if (now - _startedTick < PresentSilence.TotalMilliseconds) return;

            double? estimate = SampleEstimate(now);
            if (estimate is null) return;

            Current = estimate;
            IsEstimated = true;
            lock (_gate) _estimateSamples.Add(estimate.Value);
        }
        catch { }
    }

    /// <summary>
    /// Estimativa pela ocupação do motor 3D da GPU. Não é taxa de quadros — a relação varia por
    /// jogo — e por isso só é usada quando não há eventos de Present, sempre marcada como estimada.
    /// </summary>
    private double? SampleEstimate(long now)
    {
        if (_counterUnavailable) return null;

        try
        {
            if (_counter is null)
            {
                // O contador por processo só aparece quando o jogo já usa a GPU; tentamos de novo
                // a cada poucos segundos em vez de desistir na primeira.
                if (now - _lastCounterAttemptTick < 5000) return null;
                _lastCounterAttemptTick = now;

                if (!PerformanceCounterCategory.Exists("GPU Engine"))
                {
                    _counterUnavailable = true;
                    return null;
                }
                _counter = TryCreate3DCounter(_targetPid);
                if (_counter is null) return null;
                _counter.NextValue(); // a primeira leitura de um contador de taxa é sempre 0
                return null;
            }

            float utilization = _counter.NextValue();
            if (utilization <= 0) return null;

            // Referência: 100% de ocupação num monitor de 60 Hz costuma corresponder a ~60 fps.
            double refreshRate = 60;
            try
            {
                var display = _display.GetRefreshRate();
                if (display is > 0) refreshRate = display.Value;
            }
            catch { }

            return Math.Clamp(utilization / 100.0 * refreshRate, 0, refreshRate * 4);
        }
        catch
        {
            _counterUnavailable = true;
            try { _counter?.Dispose(); } catch { }
            _counter = null;
            return null;
        }
    }

    /// <summary>
    /// Procura o contador de ocupação do motor 3D do processo. O nome da instância inclui o PID,
    /// então varremos as instâncias da categoria atrás da que pertence ao jogo.
    /// </summary>
    private static PerformanceCounter? TryCreate3DCounter(int processId)
    {
        try
        {
            var category = new PerformanceCounterCategory("GPU Engine");
            string marker = $"pid_{processId}_";

            foreach (var instance in category.GetInstanceNames())
            {
                if (!instance.Contains(marker, StringComparison.OrdinalIgnoreCase)) continue;
                if (!instance.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase)) continue;

                return new PerformanceCounter("GPU Engine", "Utilization Percentage", instance, readOnly: true);
            }
        }
        catch { }

        return null;
    }

    // =====================================================================================
    //  Encerramento
    // =====================================================================================

    /// <summary>
    /// Encerra a medição (sempre fecha a sessão ETW) e devolve o resumo da sessão: a contagem real
    /// de quadros quando houve eventos de Present suficientes, senão a estimativa marcada como tal,
    /// ou null se não houve medição.
    /// </summary>
    public FpsSummary? Stop()
    {
        _timer?.Dispose();
        _timer = null;

        StopEtw();

        try { _counter?.Dispose(); } catch { }
        _counter = null;
        _target = null;
        _targetPid = 0;
        Current = null;
        IsEstimated = false;

        FrameTimeStatistics? dominant;
        List<double> estimates;
        lock (_gate)
        {
            dominant = _swapChains.Count == 0 ? null : _swapChains.Values.MaxBy(s => s.FrameCount);
            _swapChains.Clear();
            estimates = _estimateSamples.ToList();
            _estimateSamples.Clear();
        }

        return dominant?.Summarize() ?? SummarizeEstimates(estimates);
    }

    private static FpsSummary? SummarizeEstimates(List<double> samples)
    {
        if (samples.Count < 5) return null;

        samples.Sort();
        // 1% low da estimativa: média do pior 1% das amostras (mínimo de uma amostra).
        int lowCount = Math.Max(1, samples.Count / 100);
        double onePercentLow = samples.Take(lowCount).Average();

        return new FpsSummary(samples.Average(), samples.First(), samples.Last(), onePercentLow,
            samples.Count, IsEstimated: true);
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
