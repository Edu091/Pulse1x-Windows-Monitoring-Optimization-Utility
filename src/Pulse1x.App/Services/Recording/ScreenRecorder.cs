using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pulse1x.App.Services.Recording;

public enum RecorderState { Idle, Starting, Recording, Paused, Stopping }

/// <summary>Erro de gravação com a chave do texto traduzido e um detalhe técnico opcional.</summary>
public sealed class RecordingException : Exception
{
    public string MessageKey { get; }
    public string? Detail { get; }
    public RecordingException(string messageKey, string? detail = null) : base(messageKey + (detail is null ? "" : ": " + detail))
    {
        MessageKey = messageKey;
        Detail = detail;
    }
}

/// <summary>Tudo o que uma gravação precisa saber antes de começar.</summary>
public sealed record RecordingPlan(
    string FfmpegPath,
    RecordingSettings Settings,
    CaptureMonitor Monitor,
    bool HasDdagrab,
    IReadOnlyList<string> Encoders); // em ordem de preferência, já testados

/// <summary>
/// Grava a tela com o ffmpeg. Cada trecho entre "iniciar/continuar" e "pausar/terminar" é um
/// segmento .mkv separado; o áudio é gravado à parte (<see cref="AudioCapture"/>) e só escreve
/// enquanto há vídeo sendo gravado. Ao terminar, os segmentos são unidos sem recodificar e o áudio
/// é misturado, gerando um único arquivo.
///
/// Se a combinação escolhida não abrir (driver sem NVENC, monitor em outra GPU, ffmpeg antigo),
/// tenta a próxima: os outros encoders, depois a CPU e por fim a captura GDI.
/// </summary>
public sealed class ScreenRecorder : IDisposable
{
    private static readonly Regex FrameRegex = new(@"frame=\s*(\d+)", RegexOptions.Compiled);
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(15);

    private readonly SemaphoreSlim _gate = new(1, 1);
    // Cada segmento conta a partir do "Press [q]" do ffmpeg, o mesmo instante em que o áudio
    // começa a ser escrito: o tempo na tela bate com a duração real do vídeo.
    private long _segmentStart;
    private bool _clockRunning;
    private TimeSpan _accumulated;

    private RecordingPlan? _plan;
    private CaptureGeometry _geometry;
    private (CaptureMethod Method, string Encoder)? _locked;
    private string? _tempDir;
    private readonly List<string> _segments = new();
    private Segment? _current;
    private readonly List<AudioCapture> _audio = new();
    private AudioCapture? _mic;
    private int _autoRestarts;
    private bool _micMuted;

    public RecorderState State { get; private set; } = RecorderState.Idle;

    /// <summary>Disparado em qualquer thread a cada mudança de estado.</summary>
    public event Action? StateChanged;

    /// <summary>A gravação parou sozinha (o ffmpeg caiu e não voltou). Traz o arquivo salvo, se houver.</summary>
    public event Action<string?, RecordingException>? StoppedUnexpectedly;

    /// <summary>Encoder em uso (ex.: "h264_nvenc") e se caiu num plano B.</summary>
    public string? ActiveEncoder => _locked?.Encoder;
    public CaptureMethod? ActiveMethod => _locked?.Method;
    public bool UsedFallback { get; private set; }

    /// <summary>Tamanho final do vídeo.</summary>
    public (int Width, int Height) OutputSize => (_geometry.OutWidth, _geometry.OutHeight);

    public string? OutputPath { get; private set; }

    public TimeSpan Elapsed => _accumulated + (State == RecorderState.Recording && _clockRunning ? Stopwatch.GetElapsedTime(_segmentStart) : TimeSpan.Zero);

    public bool HasMicrophone => _mic is not null;

    public bool MicMuted
    {
        get => _micMuted;
        set
        {
            _micMuted = value;
            if (_mic is not null) _mic.Muted = value;
        }
    }

    // =====================================================================================
    //  Iniciar / pausar / continuar / terminar
    // =====================================================================================

    public async Task StartAsync(RecordingPlan plan)
    {
        await _gate.WaitAsync();
        try
        {
            if (State != RecorderState.Idle) return;
            SetState(RecorderState.Starting);

            // Cópia das configurações: mexer na tela durante a gravação não muda o arquivo em curso.
            var settings = JsonSerializer.Deserialize<RecordingSettings>(JsonSerializer.Serialize(plan.Settings))!;
            settings.Normalize();
            _plan = plan with { Settings = settings };
            _geometry = RecordingCommandBuilder.Geometry(settings, plan.Monitor.Width, plan.Monitor.Height);
            _locked = null;
            UsedFallback = false;
            _segments.Clear();
            _accumulated = TimeSpan.Zero;
            _clockRunning = false;
            _autoRestarts = 0;

            string folder = settings.EffectiveOutputFolder;
            try { Directory.CreateDirectory(folder); }
            catch (Exception ex) { throw new RecordingException("Rec_ErrFolder", ex.Message); }

            DateTime now = DateTime.Now;
            OutputPath = UniquePath(Path.Combine(folder, RecordingCommandBuilder.FileName(now, settings.Container)));

            // Os segmentos ficam numa pasta oculta ao lado do vídeo final: mesmo disco (juntar é rápido)
            // e, se o PC desligar no meio, os trechos já gravados continuam lá.
            _tempDir = Path.Combine(folder, $".pulse1x-rec-{now:yyyyMMdd-HHmmss}");
            Directory.CreateDirectory(_tempDir);
            try { File.SetAttributes(_tempDir, FileAttributes.Directory | FileAttributes.Hidden); } catch { }

            StartAudio(settings);

            try
            {
                await StartSegmentAsync(allowFallback: true);
            }
            catch
            {
                DisposeAudio();
                TryDeleteDir(_tempDir);
                _tempDir = null;
                OutputPath = null;
                SetState(RecorderState.Idle);
                throw;
            }

            StartClock();
            SetState(RecorderState.Recording);
        }
        finally { _gate.Release(); }
    }

    public async Task PauseAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (State != RecorderState.Recording) return;
            await EndSegmentAsync();
            SetState(RecorderState.Paused);
        }
        finally { _gate.Release(); }
    }

    public async Task ResumeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (State != RecorderState.Paused) return;
            SetState(RecorderState.Starting);
            try
            {
                await StartSegmentAsync(allowFallback: false);
            }
            catch
            {
                SetState(RecorderState.Paused);
                throw;
            }
            StartClock();
            SetState(RecorderState.Recording);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Termina e devolve o caminho do vídeo pronto.</summary>
    public async Task<string> StopAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (State is not (RecorderState.Recording or RecorderState.Paused)) throw new RecordingException("Rec_ErrNotRecording");
            if (State == RecorderState.Recording) await EndSegmentAsync();
            SetState(RecorderState.Stopping);
            try
            {
                return await FinalizeAsync();
            }
            finally
            {
                SetState(RecorderState.Idle);
            }
        }
        finally { _gate.Release(); }
    }

    // =====================================================================================
    //  Segmentos
    // =====================================================================================

    private async Task StartSegmentAsync(bool allowFallback)
    {
        var plan = _plan!;
        var attempts = new List<(CaptureMethod, string)>();
        if (_locked is { } locked) attempts.Add(locked);
        if (allowFallback || attempts.Count == 0)
        {
            if (plan.HasDdagrab)
                foreach (var enc in plan.Encoders) attempts.Add((CaptureMethod.Ddagrab, enc));
            if (!plan.Encoders.Contains("libx264") && plan.HasDdagrab) attempts.Add((CaptureMethod.Ddagrab, "libx264"));
            attempts.Add((CaptureMethod.Gdigrab, "libx264"));
            attempts = attempts.Distinct().ToList();
        }

        string? lastError = null;
        var marks = _audio.Select(a => a.Position).ToArray();
        for (int i = 0; i < attempts.Count; i++)
        {
            var (method, encoder) = attempts[i];
            string path = Path.Combine(_tempDir!, $"part{_segments.Count + 1:D3}.mkv");
            string args = RecordingCommandBuilder.SegmentArgs(plan.Settings, plan.Monitor, _geometry, method, encoder, path);

            RecordingLog.Write($"segment start: {method}/{encoder} | {args}");
            var segment = new Segment(plan.FfmpegPath, args, path);
            segment.PressQ += () => { foreach (var a in _audio) a.Writing = true; };
            if (await segment.StartAsync(StartTimeout))
            {
                if (_locked is null && i > 0) UsedFallback = true;
                _locked = (method, encoder);
                _current = segment;
                _segments.Add(path);
                segment.Exited += () => OnSegmentExited(segment);
                return;
            }

            // Falhou: o áudio talvez já tenha começado a escrever — descarta.
            for (int k = 0; k < _audio.Count; k++) { _audio[k].Writing = false; _audio[k].TruncateTo(marks[k]); }
            lastError = segment.ErrorTail;
            RecordingLog.Write($"segment failed: {method}/{encoder} | {lastError.Replace(Environment.NewLine, " / ")}");
            segment.Dispose();
            TryDelete(path);
        }
        throw new RecordingException("Rec_ErrStart", lastError);
    }

    private async Task EndSegmentAsync()
    {
        foreach (var a in _audio) a.Writing = false;
        StopClock();

        var segment = _current;
        _current = null;
        if (segment is null) return;
        await segment.StopAsync();
        segment.Dispose();
    }

    /// <summary>
    /// O ffmpeg saiu sem ninguém pedir (troca de resolução de um jogo em tela cheia, driver
    /// reiniciado). Tenta continuar num novo segmento; se não der, salva o que já foi gravado.
    /// </summary>
    private async void OnSegmentExited(Segment segment)
    {
        if (!ReferenceEquals(segment, _current) || segment.StopRequested) return;
        await _gate.WaitAsync();
        try
        {
            if (!ReferenceEquals(segment, _current) || State != RecorderState.Recording) return;
            foreach (var a in _audio) a.Writing = false;
            StopClock();
            _current = null;
            segment.Dispose();

            if (++_autoRestarts <= 5)
            {
                await Task.Delay(500);
                try
                {
                    SetState(RecorderState.Starting);
                    await StartSegmentAsync(allowFallback: false);
                    StartClock();
                    SetState(RecorderState.Recording);
                    return;
                }
                catch { /* cai para salvar o que existe */ }
            }

            SetState(RecorderState.Stopping);
            string? saved = null;
            try { saved = await FinalizeAsync(); }
            catch { }
            SetState(RecorderState.Idle);
            RecordingLog.Write($"recording stopped unexpectedly: {segment.ErrorTail.Replace(Environment.NewLine, " / ")}");
            StoppedUnexpectedly?.Invoke(saved, new RecordingException("Rec_ErrCrashed", segment.ErrorTail));
        }
        finally { _gate.Release(); }
    }

    // =====================================================================================
    //  Áudio e montagem final
    // =====================================================================================

    private void StartAudio(RecordingSettings s)
    {
        _audio.Clear();
        _mic = null;
        if (s.RecordSystemAudio)
        {
            try
            {
                var sys = AudioCapture.CreateSystem(Path.Combine(_tempDir!, "system.pcm"), s.SystemVolume);
                sys.Start();
                _audio.Add(sys);
            }
            catch { /* sem dispositivo de saída: grava sem o som do PC */ }
        }
        if (s.RecordMicrophone)
        {
            try
            {
                var mic = AudioCapture.CreateMicrophone(Path.Combine(_tempDir!, "mic.pcm"), s.MicrophoneId, s.MicrophoneVolume);
                mic.Muted = _micMuted;
                mic.Start();
                _audio.Add(mic);
                _mic = mic;
            }
            catch { }
        }
    }

    private void DisposeAudio()
    {
        foreach (var a in _audio)
            try { a.Dispose(); } catch { }
        _audio.Clear();
        _mic = null;
    }

    private async Task<string> FinalizeAsync()
    {
        var plan = _plan!;
        var tracks = _audio.Select(a => a.Track).ToList();
        DisposeAudio();

        var parts = _segments.Where(p => File.Exists(p) && new FileInfo(p).Length > 1024).ToList();
        if (parts.Count == 0)
        {
            TryDeleteDir(_tempDir);
            throw new RecordingException("Rec_ErrEmpty");
        }

        // Faixa de áudio vazia (dispositivo sumiu) quebraria a mistura: fica de fora.
        tracks = tracks.Where(t => File.Exists(t.Path) && new FileInfo(t.Path).Length > 0).ToList();

        string list = Path.Combine(_tempDir!, "parts.txt");
        await File.WriteAllTextAsync(list, RecordingCommandBuilder.ConcatList(parts), new UTF8Encoding(false));

        string output = OutputPath!;
        string args = RecordingCommandBuilder.FinalizeArgs(list, tracks, plan.Settings.AudioOffsetMs,
            plan.Settings.Codec, plan.Settings.Container, output);
        var result = await FfmpegService.RunAsync(plan.FfmpegPath, args, TimeSpan.FromMinutes(30));

        if (result.ExitCode != 0 || !File.Exists(output) || new FileInfo(output).Length == 0)
        {
            // Plano B: só o vídeo, para não perder a gravação por causa do áudio.
            if (tracks.Count > 0)
            {
                args = RecordingCommandBuilder.FinalizeArgs(list, Array.Empty<AudioTrack>(), 0,
                    plan.Settings.Codec, plan.Settings.Container, output);
                var retry = await FfmpegService.RunAsync(plan.FfmpegPath, args, TimeSpan.FromMinutes(30));
                if (retry.ExitCode == 0 && File.Exists(output))
                {
                    TryDeleteDir(_tempDir);
                    _tempDir = null;
                    return output;
                }
            }
            // Os trechos continuam na pasta oculta: o erro diz onde estão.
            RecordingLog.Write($"finalize failed (exit {result.ExitCode}): {Tail(result.Output).Replace(Environment.NewLine, " / ")}");
            throw new RecordingException("Rec_ErrFinalize", _tempDir + Environment.NewLine + Tail(result.Output));
        }

        TryDeleteDir(_tempDir);
        _tempDir = null;
        return output;
    }

    private void StartClock()
    {
        _segmentStart = _current?.PressQAt is long t and > 0 ? t : Stopwatch.GetTimestamp();
        _clockRunning = true;
    }

    private void StopClock()
    {
        if (_clockRunning) _accumulated += Stopwatch.GetElapsedTime(_segmentStart);
        _clockRunning = false;
    }

    private void SetState(RecorderState state)
    {
        State = state;
        try { StateChanged?.Invoke(); } catch { }
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        string dir = Path.GetDirectoryName(path)!, name = Path.GetFileNameWithoutExtension(path), ext = Path.GetExtension(path);
        for (int i = 2; ; i++)
        {
            string candidate = Path.Combine(dir, $"{name}_{i}{ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    private static string Tail(string text, int lines = 6) =>
        string.Join(Environment.NewLine, text.Split('\n', StringSplitOptions.RemoveEmptyEntries).TakeLast(lines).Select(l => l.TrimEnd('\r')));

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void TryDeleteDir(string? path)
    {
        try { if (path is not null && Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
    }

    public void Dispose()
    {
        // Fechar o app no meio de uma gravação: para o ffmpeg (o .mkv já gravado é válido) e
        // deixa os trechos na pasta oculta, sem bloquear o encerramento juntando arquivos.
        try { _current?.StopAsync().Wait(TimeSpan.FromSeconds(5)); } catch { }
        _current?.Dispose();
        DisposeAudio();
    }

    // =====================================================================================
    //  Um processo do ffmpeg (um segmento)
    // =====================================================================================

    private sealed class Segment : IDisposable
    {
        private readonly Process _process;
        private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly LinkedList<string> _tail = new();
        private bool _pressQSeen;

        public event Action? PressQ;
        public long PressQAt { get; private set; }
        public event Action? Exited;
        public bool StopRequested { get; private set; }
        public string Path { get; }

        public Segment(string exe, string args, string path)
        {
            Path = path;
            _process = new Process
            {
                StartInfo = new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    StandardErrorEncoding = Encoding.UTF8,
                },
                EnableRaisingEvents = true,
            };
            _process.ErrorDataReceived += (_, e) => OnLine(e.Data);
            _process.OutputDataReceived += (_, _) => { };
            _process.Exited += (_, _) =>
            {
                _ready.TrySetResult(false);
                Exited?.Invoke();
            };
        }

        public string ErrorTail
        {
            get
            {
                lock (_tail)
                    return string.Join(Environment.NewLine, _tail.Where(l => !l.StartsWith("frame=", StringComparison.Ordinal)).TakeLast(6));
            }
        }

        private void OnLine(string? line)
        {
            if (line is null) return;
            lock (_tail)
            {
                _tail.AddLast(line);
                while (_tail.Count > 40) _tail.RemoveFirst();
            }
            if (!_pressQSeen && line.Contains("Press [q] to stop", StringComparison.Ordinal))
            {
                _pressQSeen = true;
                PressQAt = Stopwatch.GetTimestamp();
                PressQ?.Invoke();
            }
            var m = FrameRegex.Match(line);
            if (m.Success && int.TryParse(m.Groups[1].Value, out int frames) && frames > 0)
            {
                if (!_pressQSeen) { _pressQSeen = true; PressQAt = Stopwatch.GetTimestamp(); PressQ?.Invoke(); }
                _ready.TrySetResult(true);
            }
        }

        /// <summary>Inicia e espera o primeiro quadro codificado. false = não abriu.</summary>
        public async Task<bool> StartAsync(TimeSpan timeout)
        {
            try
            {
                _process.Start();
                // Prioridade acima do normal: o encoder não perde quadros quando um jogo disputa a CPU.
                try { _process.PriorityClass = ProcessPriorityClass.AboveNormal; } catch { }
                _process.BeginErrorReadLine();
                _process.BeginOutputReadLine();
            }
            catch (Exception ex)
            {
                lock (_tail) _tail.AddLast(ex.Message);
                return false;
            }

            var done = await Task.WhenAny(_ready.Task, Task.Delay(timeout));
            if (done == _ready.Task && _ready.Task.Result) return true;
            await StopAsync();
            return false;
        }

        /// <summary>Pede para o ffmpeg terminar (tecla q) e espera ele fechar o arquivo.</summary>
        public async Task StopAsync()
        {
            StopRequested = true;
            try
            {
                if (_process.HasExited) return;
                await _process.StandardInput.WriteAsync('q');
                await _process.StandardInput.FlushAsync();
            }
            catch { }
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await _process.WaitForExitAsync(cts.Token);
            }
            catch
            {
                try { _process.Kill(entireProcessTree: true); } catch { }
            }
        }

        public void Dispose()
        {
            try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); } catch { }
            _process.Dispose();
        }
    }
}
