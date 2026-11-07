using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Pulse1x.App.Services.Sound;

/// <summary>
/// Memória compartilhada com o plugin de voz (Pulse1xVoice.dll), que roda dentro do serviço de
/// áudio do Windows. O Pulse1x escreve aqui as amostras dos sons da mesa; o plugin as mistura à
/// voz do microfone — por isso os sons chegam à call em qualquer app, sem microfone virtual.
///
/// Layout (fixo, espelhado em native/Pulse1xVoice/pulse1x_voice.c):
///   0 magic | 4 version | 8 capacity | 12 sampleRate | 16 writePos | 24 readPos | 32 flushSeq
///   36 volume | 40 consumerRate | 44..79 reservado | 80 dados (float, anel de "capacity" amostras)
/// </summary>
public sealed unsafe class SoundboardChannel : IDisposable
{
    public const string GlobalName = @"Global\Pulse1xSoundboard";
    public const uint Magic = 0x31425350; // 'PSB1'
    public const int HeaderBytes = 80;
    public const int DefaultCapacity = 1 << 20; // ~21 s a 48 kHz, ~65 s a 16 kHz

    private readonly IntPtr _map;
    private readonly byte* _view;
    private readonly object _gate = new();

    /// <param name="name">Nome do mapeamento; o padrão (Global\) exige o Pulse1x elevado.</param>
    public SoundboardChannel(string name = GlobalName, int capacity = DefaultCapacity)
    {
        // Leitura e escrita para o sistema, administradores e LOCAL SERVICE (a conta do audiodg).
        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW("D:(A;;GA;;;SY)(A;;GA;;;BA)(A;;GRGW;;;LS)", 1,
                out IntPtr sd, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var sa = new SecurityAttributes { nLength = Marshal.SizeOf<SecurityAttributes>(), lpSecurityDescriptor = sd };
            long size = HeaderBytes + (long)capacity * sizeof(float);
            _map = CreateFileMappingW(new IntPtr(-1), ref sa, 0x04 /* PAGE_READWRITE */,
                (uint)(size >> 32), (uint)size, name);
            if (_map == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { LocalFree(sd); }

        _view = (byte*)MapViewOfFile(_map, 0x0002 | 0x0004 /* WRITE | READ */, 0, 0, UIntPtr.Zero);
        if (_view == null)
        {
            int err = Marshal.GetLastWin32Error();
            CloseHandle(_map);
            throw new Win32Exception(err);
        }

        // Um mapeamento já existente (o plugin ainda o segura de uma execução anterior) é reaproveitado.
        if (*(uint*)_view != Magic)
        {
            *(uint*)(_view + 4) = 1;
            *(uint*)(_view + 8) = (uint)capacity;
            *(uint*)(_view + 12) = 0;
            *(float*)(_view + 36) = 1f;
            Thread.MemoryBarrier();
            *(uint*)_view = Magic;
        }
    }

    private uint Capacity => *(uint*)(_view + 8);
    private long* WritePos => (long*)(_view + 16);
    private long* ReadPos => (long*)(_view + 24);
    private float* Data => (float*)(_view + HeaderBytes);

    /// <summary>Taxa de amostragem em que o plugin está processando (0 = plugin ainda não leu nada).</summary>
    public int ConsumerRate => Volatile.Read(ref *(int*)(_view + 40));

    /// <summary>Quanto o plugin já consumiu; avança enquanto ele está ativo no microfone.</summary>
    public long Consumed => Interlocked.Read(ref *ReadPos);

    public float Volume
    {
        get => *(float*)(_view + 36);
        set => *(float*)(_view + 36) = Math.Clamp(value, 0f, 2f);
    }

    /// <summary>
    /// Toca as amostras (mono, na taxa <paramref name="sampleRate"/>) a partir de agora. Se outro som
    /// ainda estiver tocando, os dois se misturam em vez de entrar na fila.
    /// </summary>
    public void Play(ReadOnlySpan<float> samples, int sampleRate)
    {
        if (samples.IsEmpty) return;
        lock (_gate)
        {
            long cap = Capacity;
            if (*(uint*)(_view + 12) != (uint)sampleRate)
            {
                // Taxa mudou: o que estava pendente foi feito para outra taxa e é descartado.
                *(uint*)(_view + 12) = (uint)sampleRate;
                Interlocked.Exchange(ref *WritePos, Interlocked.Read(ref *ReadPos));
            }

            long r = Interlocked.Read(ref *ReadPos);
            long w = Interlocked.Read(ref *WritePos);
            if (w < r) w = r;
            long n = Math.Min(samples.Length, cap - sampleRate / 10);
            // Começa um pouco à frente do que o plugin está lendo, para não escrever onde ele lê.
            long start = r + sampleRate * 30L / 1000;
            for (long i = w; i < start; i++) Data[i % cap] = 0f;

            for (long i = 0; i < n; i++)
            {
                long idx = start + i;
                long slot = idx % cap;
                Data[slot] = idx < w ? Data[slot] + samples[(int)i] : samples[(int)i];
            }
            Thread.MemoryBarrier();
            Interlocked.Exchange(ref *WritePos, Math.Max(w, start + n));
        }
    }

    /// <summary>Corta tudo o que estiver tocando (o plugin pula para o fim do que foi escrito).</summary>
    public void StopAll() => Interlocked.Increment(ref *(int*)(_view + 32));

    public void Dispose()
    {
        UnmapViewOfFile((IntPtr)_view);
        CloseHandle(_map);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        public int bInheritHandle;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string sddl, int revision, out IntPtr sd, IntPtr size);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFileMappingW(IntPtr file, ref SecurityAttributes sa, uint protect, uint sizeHigh, uint sizeLow, string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr MapViewOfFile(IntPtr map, uint access, uint offsetHigh, uint offsetLow, UIntPtr bytes);

    [DllImport("kernel32.dll")]
    private static extern bool UnmapViewOfFile(IntPtr view);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr mem);
}
