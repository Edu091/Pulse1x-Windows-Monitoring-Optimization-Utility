using System.Diagnostics;
using System.Runtime.InteropServices;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Pulse1x.App.Services.Benchmark;

/// <summary>
/// Benchmark de placa de vídeo pelo Direct3D 11 (o mesmo caminho de boa parte dos jogos), com
/// compute shaders: poder de cálculo em ponto flutuante (GFLOPS) e largura de banda da memória de
/// vídeo (GB/s). Roda na GPU de alto desempenho — em notebooks híbridos, a dedicada.
///
/// Cada despacho é dimensionado para ~25 ms: longe do limite de 2 s em que o Windows reinicia o
/// driver (TDR), e longo o bastante para o custo de envio não distorcer a medida.
/// </summary>
public static class GpuBenchmark
{
    // Máquina de referência: GeForce RTX 3050 6GB Laptop GPU (com a janela em primeiro plano).
    private const double RefGflops = 5520, RefBandwidthGBs = 85.4;

    private const string FlopsShader = @"
cbuffer Params : register(b0) { uint Iterations; float A; float B; uint Pad; };
RWStructuredBuffer<float> Output : register(u0);
[numthreads(256, 1, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    float x0 = id.x * 1e-7f, x1 = x0 + 0.1f, x2 = x0 + 0.2f, x3 = x0 + 0.3f;
    float x4 = x0 + 0.4f, x5 = x0 + 0.5f, x6 = x0 + 0.6f, x7 = x0 + 0.7f;
    [loop] for (uint i = 0; i < Iterations; i++)
    {
        [unroll] for (uint k = 0; k < 4; k++)
        {
            x0 = mad(x0, A, B); x1 = mad(x1, A, B); x2 = mad(x2, A, B); x3 = mad(x3, A, B);
            x4 = mad(x4, A, B); x5 = mad(x5, A, B); x6 = mad(x6, A, B); x7 = mad(x7, A, B);
        }
    }
    Output[id.x] = x0 + x1 + x2 + x3 + x4 + x5 + x6 + x7;
}";
    private const int FlopsPerIteration = 4 * 8 * 2; // 32 mads, 2 operações cada

    private const string CopyShader = @"
cbuffer Params : register(b0) { uint Count; uint Stride; uint Pad0; uint Pad1; };
ByteAddressBuffer Src : register(t0);
RWByteAddressBuffer Dst : register(u0);
[numthreads(256, 1, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    for (uint i = id.x; i < Count; i += Stride)
        Dst.Store4(i * 16, Src.Load4(i * 16));
}";

    private const uint Groups = 4096;
    private const uint Threads = Groups * 256;

    [StructLayout(LayoutKind.Sequential)]
    private struct FlopsParams { public uint Iterations; public float A; public float B; public uint Pad; }

    [StructLayout(LayoutKind.Sequential)]
    private struct CopyParams { public uint Count; public uint Stride; public uint Pad0; public uint Pad1; }

    public static BenchmarkResult Run(TimeSpan perTest, CancellationToken ct, IProgress<double>? progress = null)
    {
        Session? session = null;
        try
        {
            session = Session.Create(ct, out var error);
            if (session is null) return error!;
            progress?.Report(0.1);
            return session.Measure(perTest, ct, progress);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return Failed(session?.Name ?? "GPU", ex.Message); }
        finally { session?.Dispose(); }
    }

    /// <summary>Stress contínuo da GPU: meio segundo de cálculo + meio de banda por amostra.</summary>
    public static IStressSession StartStress(CancellationToken ct)
    {
        var session = Session.Create(ct, out var error)
                      ?? throw new InvalidOperationException(error?.Error ?? "Direct3D 11");
        return new Stress(session);
    }

    private sealed class Stress : IStressSession
    {
        private readonly Session _session;
        public Stress(Session session) => _session = session;
        public BenchmarkResult Sample(CancellationToken ct) => _session.Measure(TimeSpan.FromMilliseconds(500), ct, null);
        public void Dispose() => _session.Dispose();
    }

    private static BenchmarkResult Failed(string device, string error) =>
        new() { Kind = BenchmarkKind.Gpu, Device = device, Error = error };

    /// <summary>Device, shaders e buffers criados uma vez e reaproveitados a cada medida.</summary>
    private sealed class Session : IDisposable
    {
        private readonly IDXGIAdapter1? _adapter;
        private readonly ID3D11Device _device;
        private readonly ID3D11DeviceContext _context;
        private readonly ID3D11Query _done;
        private readonly ID3D11ComputeShader _flopsShader, _copyShader;
        private readonly ID3D11Buffer _flopsOut, _flopsParams, _copySrc, _copyDst, _copyParams;
        private readonly ID3D11UnorderedAccessView _flopsView, _dstView;
        private readonly ID3D11ShaderResourceView _srcView;
        private readonly uint _copyBytes;
        private FlopsParams _flops = new() { Iterations = 32, A = 0.9999f, B = 0.0001f };
        private bool _calibrated;

        public string Name { get; }

        private Session(IDXGIAdapter1? adapter, ID3D11Device device, ID3D11DeviceContext context)
        {
            _adapter = adapter;
            _device = device;
            _context = context;
            Name = adapter?.Description1.Description.Trim() ?? "GPU";
            _done = device.CreateQuery(QueryType.Event, QueryFlags.None);

            using (var code = Compile(FlopsShader)) _flopsShader = device.CreateComputeShader(code);
            _flopsOut = device.CreateBuffer(new BufferDescription(Threads * 4, BindFlags.UnorderedAccess, ResourceUsage.Default,
                CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 4));
            _flopsView = device.CreateUnorderedAccessView(_flopsOut, null);
            _flopsParams = device.CreateBuffer(new BufferDescription(16, BindFlags.ConstantBuffer, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0));

            // Dois buffers de 256 MB (128 MB em placas com pouca memória dedicada ou integradas).
            ulong vram = adapter?.Description1.DedicatedVideoMemory ?? 0;
            _copyBytes = vram >= 2UL << 30 ? 256u << 20 : 128u << 20;
            using (var code = Compile(CopyShader)) _copyShader = device.CreateComputeShader(code);
            // A origem é lida por SRV (somente leitura, com cache de textura), como os jogos leem dados.
            var desc = new BufferDescription(_copyBytes, BindFlags.UnorderedAccess | BindFlags.ShaderResource, ResourceUsage.Default,
                CpuAccessFlags.None, ResourceOptionFlags.BufferAllowRawViews, 0);
            _copySrc = device.CreateBuffer(desc);
            _copyDst = device.CreateBuffer(desc);
            _srcView = device.CreateShaderResourceView(_copySrc,
                new ShaderResourceViewDescription(_copySrc, Format.R32_Typeless, 0, _copyBytes / 4, BufferExtendedShaderResourceViewFlags.Raw));
            _dstView = device.CreateUnorderedAccessView(_copyDst,
                new UnorderedAccessViewDescription(_copyDst, Format.R32_Typeless, 0, _copyBytes / 4, BufferUnorderedAccessViewFlags.Raw));
            _copyParams = device.CreateBuffer(new BufferDescription(16, BindFlags.ConstantBuffer, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0));
            var cp = new CopyParams { Count = _copyBytes / 16, Stride = Threads };
            _context.UpdateSubresource(in cp, _copyParams, 0, 0, 0, null);
        }

        public static Session? Create(CancellationToken ct, out BenchmarkResult? error)
        {
            error = null;
            var adapter = PickAdapter();
            string name = adapter?.Description1.Description.Trim() ?? "GPU";
            var hr = D3D11.D3D11CreateDevice(adapter, adapter is null ? DriverType.Hardware : DriverType.Unknown,
                DeviceCreationFlags.None, new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 }, out ID3D11Device? device, out ID3D11DeviceContext? context);
            if (hr.Failure || device is null || context is null)
            {
                adapter?.Dispose();
                error = Failed(name, "Direct3D 11");
                return null;
            }
            ct.ThrowIfCancellationRequested();
            return new Session(adapter, device, context);
        }

        public BenchmarkResult Measure(TimeSpan perTest, CancellationToken ct, IProgress<double>? progress)
        {
            double gflops = MeasureFlops(perTest, ct);
            progress?.Report(0.5);
            double bandwidth = MeasureBandwidth(perTest, ct);
            progress?.Report(1);
            return new BenchmarkResult
            {
                Kind = BenchmarkKind.Gpu,
                Score = BenchmarkScoring.Score(new[] { (gflops, RefGflops, true), (bandwidth, RefBandwidthGBs, true) }),
                Device = Name,
                Metrics = new()
                {
                    new("Bench_GpuCompute", gflops, "GFLOPS", 0),
                    new("Bench_GpuBandwidth", bandwidth, "GB/s"),
                },
            };
        }

        private double MeasureFlops(TimeSpan duration, CancellationToken ct)
        {
            _context.CSSetShader(_flopsShader);
            _context.CSSetUnorderedAccessView(0, _flopsView, 0);
            _context.CSSetConstantBuffer(0, _flopsParams);

            double TimeDispatches(int count)
            {
                _context.UpdateSubresource(in _flops, _flopsParams, 0, 0, 0, null);
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < count; i++) _context.Dispatch(Groups, 1, 1);
                Wait(ct);
                return sw.Elapsed.TotalSeconds;
            }

            if (!_calibrated)
            {
                TimeDispatches(1); // aquecimento: compilação no driver e clocks subindo
                // Ajusta as iterações para cada despacho levar ~25 ms nesta GPU.
                double t = TimeDispatches(1);
                _flops.Iterations = (uint)Math.Clamp(_flops.Iterations * 0.025 / Math.Max(t, 1e-5), 16, 200_000);
                TimeDispatches(2);
                _calibrated = true;
            }

            double flops = 0, seconds = 0;
            var total = Stopwatch.StartNew();
            do
            {
                ct.ThrowIfCancellationRequested();
                seconds += TimeDispatches(4);
                flops += 4.0 * Threads * _flops.Iterations * FlopsPerIteration;
            }
            while (total.Elapsed < duration);
            return flops / seconds / 1e9;
        }

        private double MeasureBandwidth(TimeSpan duration, CancellationToken ct)
        {
            _context.CSSetShader(_copyShader);
            _context.CSSetShaderResource(0, _srcView);
            _context.CSSetUnorderedAccessView(0, _dstView, 0);
            _context.CSSetConstantBuffer(0, _copyParams);

            double Pass(int count)
            {
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < count; i++) _context.Dispatch(Groups, 1, 1);
                Wait(ct);
                return sw.Elapsed.TotalSeconds;
            }

            Pass(1); // aquecimento
            double moved = 0, seconds = 0;
            var total = Stopwatch.StartNew();
            do
            {
                ct.ThrowIfCancellationRequested();
                seconds += Pass(4);
                moved += 4.0 * _copyBytes * 2; // cada byte é lido e escrito
            }
            while (total.Elapsed < duration);
            _context.ClearState();
            return moved / seconds / 1e9;
        }

        private void Wait(CancellationToken ct)
        {
            _context.End(_done);
            _context.Flush();
            var sw = Stopwatch.StartNew();
            while (!_context.IsDataAvailable(_done))
            {
                if (sw.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("GPU");
                ct.ThrowIfCancellationRequested();
                Thread.Yield();
            }
        }

        public void Dispose()
        {
            _context.ClearState();
            _context.Flush();
            foreach (var d in new IDisposable[] { _srcView, _dstView, _flopsView, _copySrc, _copyDst, _copyParams, _flopsOut, _flopsParams, _flopsShader, _copyShader, _done, _context, _device })
                d.Dispose();
            _adapter?.Dispose();
        }
    }

    /// <summary>A GPU que o Windows considera de alto desempenho, ignorando o renderizador por software.</summary>
    private static IDXGIAdapter1? PickAdapter()
    {
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        using (var factory6 = factory.QueryInterfaceOrNull<IDXGIFactory6>())
        {
            if (factory6 is not null)
            {
                for (uint i = 0; factory6.EnumAdapterByGpuPreference(i, GpuPreference.HighPerformance, out IDXGIAdapter1? a).Success && a is not null; i++)
                {
                    if ((a.Description1.Flags & AdapterFlags.Software) == 0) return a;
                    a.Dispose();
                }
            }
        }
        for (uint i = 0; factory.EnumAdapters1(i, out var a).Success; i++)
        {
            if ((a.Description1.Flags & AdapterFlags.Software) == 0) return a;
            a.Dispose();
        }
        return null;
    }

    private static Blob Compile(string source)
    {
        // Sem macros nem include: o Vortice aceita nulo nos dois (a anotação dele não marca isso).
        var hr = Compiler.Compile(source, null!, null!, "main", "pulse1x_bench.hlsl", "cs_5_0",
            ShaderFlags.OptimizationLevel3, EffectFlags.None, out Blob blob, out Blob? errors);
        if (hr.Failure || blob is null)
            throw new InvalidOperationException(errors?.AsString() ?? "HLSL");
        errors?.Dispose();
        return blob;
    }
}
