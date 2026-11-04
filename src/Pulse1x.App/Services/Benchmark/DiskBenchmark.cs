using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Pulse1x.App.Services.Benchmark;

/// <summary>Disco que o usuário pode testar (uma letra de unidade).</summary>
public sealed record DiskTarget(string Root, string Label, string Model, string MediaKind, long FreeBytes, long TotalBytes)
{
    public string Display => $"{Root.TrimEnd('\\')} {Label} — {Model} ({MediaKind}) · {FreeBytes / 1e9:N0} GB livres".Replace("  ", " ");
    public override string ToString() => Display;
}

/// <summary>
/// Benchmark de SSD/HD no estilo do CrystalDiskMark: leitura e escrita sequenciais (blocos de 1 MiB,
/// 8 pedidos em paralelo), 4K aleatório com fila longa (32 pedidos — o que SSDs NVMe fazem de melhor)
/// e 4K aleatório com um pedido por vez (o que mais pesa no dia a dia e no carregamento de jogos).
///
/// O arquivo de teste é aberto sem o cache do Windows (FILE_FLAG_NO_BUFFERING + WRITE_THROUGH),
/// senão a "velocidade do disco" seria a da RAM, e com DeleteOnClose: some mesmo se o app fechar no
/// meio do teste.
/// </summary>
public static class DiskBenchmark
{
    private const int Align = 4096;
    private const FileOptions NoBuffering = (FileOptions)0x20000000;

    // Máquina de referência: SSD NVMe SM2P41C8 512 GB (PCIe 4.0) do Acer Nitro ANV15-51.
    private const double RefSeqRead = 5240, RefSeqWrite = 4300, RefRnd32Read = 1250, RefRnd32Write = 1100, RefRnd1Read = 50;

    /// <summary>Unidades fixas e removíveis prontas, com modelo e tipo (NVMe, SSD, HD, USB).</summary>
    public static List<DiskTarget> ListDrives()
    {
        var media = DiskMediaByLetter();
        var list = new List<DiskTarget>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady || d.DriveType is not (DriveType.Fixed or DriveType.Removable)) continue;
                char letter = char.ToUpperInvariant(d.Name[0]);
                var (model, kind) = media.TryGetValue(letter, out var m) ? m : ("", d.DriveType == DriveType.Removable ? "USB" : "?");
                list.Add(new DiskTarget(d.RootDirectory.FullName, d.VolumeLabel, model.Length > 0 ? model : d.DriveFormat, kind, d.AvailableFreeSpace, d.TotalSize));
            }
            catch { /* unidade que sumiu ou sem permissão: fica de fora */ }
        }
        return list;
    }

    /// <summary>Letra -> (modelo, tipo), pelo WMI de armazenamento (partição -> disco físico).</summary>
    private static Dictionary<char, (string Model, string Kind)> DiskMediaByLetter()
    {
        var result = new Dictionary<char, (string, string)>();
        try
        {
            var disks = new Dictionary<string, (string, string)>();
            using (var s = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage", "SELECT DeviceId, FriendlyName, MediaType, BusType FROM MSFT_PhysicalDisk"))
                foreach (ManagementObject mo in s.Get())
                {
                    ushort mediaType = Convert.ToUInt16(mo["MediaType"] ?? 0);
                    ushort bus = Convert.ToUInt16(mo["BusType"] ?? 0);
                    string kind = bus == 7 ? "USB" : bus == 17 ? "NVMe SSD" : mediaType == 4 ? "SSD" : mediaType == 3 ? "HD" : "?";
                    disks[mo["DeviceId"]?.ToString() ?? ""] = ((mo["FriendlyName"] as string ?? "").Trim(), kind);
                }
            using (var s = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage", "SELECT DriveLetter, DiskNumber FROM MSFT_Partition"))
                foreach (ManagementObject mo in s.Get())
                {
                    if (mo["DriveLetter"] is not char letter || letter == '\0') continue;
                    if (disks.TryGetValue(mo["DiskNumber"]?.ToString() ?? "", out var info))
                        result[char.ToUpperInvariant(letter)] = info;
                }
        }
        catch { /* sem WMI de armazenamento: a lista sai sem modelo/tipo */ }
        return result;
    }

    /// <summary>Tamanho do arquivo de teste: 1 GiB, ou menos em discos quase cheios.</summary>
    public static long TestSize(DiskTarget target) =>
        target.FreeBytes >= 4L << 30 ? 1L << 30 : target.FreeBytes >= 1L << 30 ? 256L << 20 : 0;

    public static BenchmarkResult Run(DiskTarget target, TimeSpan perTest, CancellationToken ct, IProgress<double>? progress = null)
    {
        long size = TestSize(target);
        if (size == 0) return Failed(target, "Bench_DiskNoSpace");

        using var file = DiskTestFile.Create(target, size, out var error);
        if (file is null) return Failed(target, "Bench_DiskNoAccess", error);

        // Leituras primeiro, escritas por último e mais curtas: depois de gigabytes escritos, SSDs sem
        // DRAM (e com cache SLC pequeno) passam a reorganizar dados por dentro, e até as leituras
        // feitas em seguida caíam para um terço na mesma máquina.
        progress?.Report(0.15);
        // Respiro depois de gravar o arquivo: o controlador termina de mover o que estava no cache SLC.
        ct.WaitHandle.WaitOne(TimeSpan.FromSeconds(2));
        ct.ThrowIfCancellationRequested();
        double seqRead = file.Measure(blockSize: 1 << 20, queueDepth: 8, random: false, write: false, perTest, ct); progress?.Report(0.3);
        double rnd32Read = file.Measure(4096, 32, true, false, perTest, ct); progress?.Report(0.5);
        double rnd1Read = file.Measure(4096, 1, true, false, perTest, ct); progress?.Report(0.7);
        var writeTime = TimeSpan.FromTicks(perTest.Ticks * 2 / 3);
        double seqWrite = file.Measure(1 << 20, 8, false, true, writeTime, ct); progress?.Report(0.85);
        double rnd32Write = file.Measure(4096, 32, true, true, writeTime, ct); progress?.Report(1);

        return Result(target, seqRead, seqWrite, rnd32Read, rnd32Write, rnd1Read);
    }

    internal static BenchmarkResult Result(DiskTarget target, double seqRead, double seqWrite, double rnd32Read, double rnd32Write, double rnd1Read)
    {
        var parts = new List<(double, double, bool)> { (seqRead, RefSeqRead, true), (rnd32Read, RefRnd32Read, true), (rnd1Read, RefRnd1Read, true) };
        if (seqWrite > 0) parts.Add((seqWrite, RefSeqWrite, true));
        if (rnd32Write > 0) parts.Add((rnd32Write, RefRnd32Write, true));
        var metrics = new List<BenchmarkMetric> { new("Bench_DiskSeqRead", seqRead, "MB/s", 0) };
        if (seqWrite > 0) metrics.Add(new("Bench_DiskSeqWrite", seqWrite, "MB/s", 0));
        metrics.Add(new("Bench_DiskRnd32Read", rnd32Read, "MB/s", 0));
        if (rnd32Write > 0) metrics.Add(new("Bench_DiskRnd32Write", rnd32Write, "MB/s", 0));
        metrics.Add(new("Bench_DiskRnd1Read", rnd1Read, "MB/s", 1));
        return new BenchmarkResult
        {
            Kind = BenchmarkKind.Disk,
            Score = BenchmarkScoring.Score(parts),
            Device = $"{target.Root.TrimEnd('\\')} — {target.Model} ({target.MediaKind})",
            Metrics = metrics,
        };
    }

    /// <summary>
    /// Stress do disco: grava o arquivo de teste uma vez e daí em diante só LÊ (sequencial + 4K
    /// aleatório). Escrita contínua por minutos gastaria a vida útil do SSD sem ensinar nada a mais.
    /// </summary>
    public static IStressSession StartStress(DiskTarget target)
    {
        long size = TestSize(target);
        if (size == 0) throw new InvalidOperationException(Localization.Loc.S("Bench_DiskNoSpace"));
        var file = DiskTestFile.Create(target, size, out var error)
                   ?? throw new InvalidOperationException(Localization.Loc.S("Bench_DiskNoAccess") + (error is null ? "" : $" ({error})"));
        return new Stress(target, file);
    }

    private sealed class Stress : IStressSession
    {
        private readonly DiskTarget _target;
        private readonly DiskTestFile _file;
        public Stress(DiskTarget target, DiskTestFile file) { _target = target; _file = file; }

        public BenchmarkResult Sample(CancellationToken ct)
        {
            var half = TimeSpan.FromMilliseconds(500);
            double seq = _file.Measure(1 << 20, 8, false, false, half, ct);
            double rnd = _file.Measure(4096, 32, true, false, half, ct);
            return new BenchmarkResult
            {
                Kind = BenchmarkKind.Disk,
                Score = ReadOnlyScore(seq, rnd),
                Device = $"{_target.Root.TrimEnd('\\')} — {_target.Model} ({_target.MediaKind})",
                Metrics = new() { new("Bench_DiskSeqRead", seq, "MB/s", 0), new("Bench_DiskRnd32Read", rnd, "MB/s", 0) },
            };
        }

        public void Dispose() => _file.Dispose();
    }

    /// <summary>Pontuação só de leitura, usada pelo stress (que não escreve para não gastar o SSD).</summary>
    internal static int ReadOnlyScore(double seqRead, double rnd32Read) =>
        BenchmarkScoring.Score(new[] { (seqRead, RefSeqRead, true), (rnd32Read, RefRnd32Read, true) });

    private static BenchmarkResult Failed(DiskTarget target, string errorKey, string? detail = null) => new()
    {
        Kind = BenchmarkKind.Disk,
        Device = $"{target.Root.TrimEnd('\\')} — {target.Model}",
        Error = Localization.Loc.S(errorKey) + (detail is null ? "" : $" ({detail})"),
    };

    /// <summary>Arquivo de teste aberto sem cache, com memória alinhada para os pedidos.</summary>
    internal sealed class DiskTestFile : IDisposable
    {
        private readonly SafeFileHandle _handle;
        private readonly long _size;
        private readonly unsafe byte* _buffer;
        private const int BufferBytes = 32 << 20; // 32 pedidos de até 1 MiB

        private unsafe DiskTestFile(SafeFileHandle handle, long size)
        {
            _handle = handle;
            _size = size;
            _buffer = (byte*)NativeMemory.AlignedAlloc(BufferBytes, Align);
            // Dados aleatórios: SSDs com compressão (alguns SandForce, Phison) não podem "trapacear".
            Random.Shared.NextBytes(new Span<byte>(_buffer, BufferBytes));
        }

        /// <summary>
        /// Cria o arquivo no disco escolhido. Tenta a raiz, depois uma pasta própria e, no disco do
        /// sistema, a pasta temporária do usuário — a raiz do C: só aceita escrita de administrador.
        /// </summary>
        public static DiskTestFile? Create(DiskTarget target, long size, out string? error)
        {
            error = null;
            var folders = new List<string> { target.Root, Path.Combine(target.Root, BenchFolder) };
            string temp = Path.GetTempPath();
            if (string.Equals(Path.GetPathRoot(temp), target.Root, StringComparison.OrdinalIgnoreCase)) folders.Add(temp);

            foreach (var folder in folders)
            {
                SafeFileHandle? handle = null;
                try
                {
                    Directory.CreateDirectory(folder);
                    handle = File.OpenHandle(Path.Combine(folder, "Pulse1x-benchmark.tmp"), FileMode.Create, FileAccess.ReadWrite, FileShare.None,
                        FileOptions.Asynchronous | FileOptions.WriteThrough | FileOptions.DeleteOnClose | NoBuffering, preallocationSize: size);
                    var file = new DiskTestFile(handle, size);
                    try { file.Fill(); }
                    catch { file.Dispose(); throw; }
                    return file;
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    handle?.Dispose();
                    error = ex.Message;
                }
                finally
                {
                    // A pasta própria só serve de abrigo ao arquivo; vazia, sai junto.
                    if (folder.EndsWith(BenchFolder, StringComparison.Ordinal))
                        try { if (!Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder); } catch { }
                }
            }
            return null;
        }

        private const string BenchFolder = "Pulse1xBench";

        /// <summary>Escreve o arquivo inteiro uma vez, para as leituras não caírem em área vazia (que SSDs respondem sem ler).</summary>
        private unsafe void Fill()
        {
            const int block = 4 << 20;
            using var mem = new NativeMemoryManager(_buffer, block);
            for (long offset = 0; offset < _size; offset += block)
                RandomAccess.Write(_handle, mem.Memory.Span[..(int)Math.Min(block, _size - offset)], offset);
        }

        /// <summary>Executa pedidos pelo tempo dado e devolve MB/s (1 MB = 1.000.000 bytes, como os fabricantes).</summary>
        public double Measure(int blockSize, int queueDepth, bool random, bool write, TimeSpan duration, CancellationToken ct)
        {
            long blocks = _size / blockSize;
            long nextSequential = 0;
            long bytes = 0;
            var sw = Stopwatch.StartNew();

            async Task Worker(int slot)
            {
                using var mem = Slot(slot, blockSize);
                var rng = new Random(slot * 7919 + blockSize);
                while (sw.Elapsed < duration && !ct.IsCancellationRequested)
                {
                    long block = random ? rng.NextInt64(blocks) : Interlocked.Increment(ref nextSequential) % blocks;
                    long offset = block * blockSize;
                    if (write) await RandomAccess.WriteAsync(_handle, mem.Memory, offset, ct).ConfigureAwait(false);
                    else await RandomAccess.ReadAsync(_handle, mem.Memory, offset, ct).ConfigureAwait(false);
                    Interlocked.Add(ref bytes, blockSize);
                }
            }

            try
            {
                Task.WaitAll(Enumerable.Range(0, queueDepth).Select(i => Task.Run(() => Worker(i), ct)).ToArray());
            }
            catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is OperationCanceledException))
            {
                throw new OperationCanceledException(ct);
            }
            ct.ThrowIfCancellationRequested();
            return Interlocked.Read(ref bytes) / sw.Elapsed.TotalSeconds / 1e6;
        }

        /// <summary>Fatia de 1 MiB do buffer alinhado para o pedido <paramref name="slot"/>.</summary>
        private unsafe NativeMemoryManager Slot(int slot, int length) => new(_buffer + (long)slot * (1 << 20), length);

        public unsafe void Dispose()
        {
            _handle.Dispose(); // DeleteOnClose apaga o arquivo aqui
            NativeMemory.AlignedFree(_buffer);
        }
    }

    /// <summary>Expõe memória nativa alinhada como Memory&lt;byte&gt; para as APIs de E/S assíncrona.</summary>
    private sealed unsafe class NativeMemoryManager : MemoryManager<byte>
    {
        private readonly byte* _pointer;
        private readonly int _length;
        public NativeMemoryManager(byte* pointer, int length) { _pointer = pointer; _length = length; }
        public override Span<byte> GetSpan() => new(_pointer, _length);
        public override MemoryHandle Pin(int elementIndex = 0) => new(_pointer + elementIndex);
        public override void Unpin() { }
        protected override void Dispose(bool disposing) { }
    }
}
