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
    // Máquina de referência: GeForce RTX 3050 6GB Laptop GPU.
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

    [StructLayout(LayoutKind.Sequential)]
    private struct FlopsParams { public uint Iterations; public float A; public float B; public uint Pad; }

    [StructLayout(LayoutKind.Sequential)]
    private struct CopyParams { public uint Count; public uint Stride; public uint Pad0; public uint Pad1; }

    public static BenchmarkResult Run(TimeSpan perTest, CancellationToken ct, IProgress<double>? progress = null)
    {
        IDXGIAdapter1? adapter = null;
        ID3D11Device? device = null;
        ID3D11DeviceContext? context = null;
        try
        {
            adapter = PickAdapter();
            string name = adapter?.Description1.Description.Trim() ?? "GPU";
            var result = D3D11.D3D11CreateDevice(adapter, adapter is null ? DriverType.Hardware : DriverType.Unknown,
                DeviceCreationFlags.None, new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 }, out device, out context);
            if (result.Failure || device is null || context is null)
                return Failed(name, "Direct3D 11");

            double gflops = MeasureFlops(device, context, perTest, ct);
            progress?.Report(0.5);
            ulong vram = adapter?.Description1.DedicatedVideoMemory ?? 0;
            double bandwidth = MeasureBandwidth(device, context, perTest, vram, ct);
            progress?.Report(1);

            int score = BenchmarkScoring.Score(new[] { (gflops, RefGflops, true), (bandwidth, RefBandwidthGBs, true) });
            return new BenchmarkResult
            {
                Kind = BenchmarkKind.Gpu,
                Score = score,
                Device = name,
                Metrics = new()
                {
                    new("Bench_GpuCompute", gflops, "GFLOPS", 0),
                    new("Bench_GpuBandwidth", bandwidth, "GB/s"),
                },
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return Failed(adapter?.Description1.Description ?? "GPU", ex.Message);
        }
        finally
        {
            context?.ClearState();
            context?.Dispose();
            device?.Dispose();
            adapter?.Dispose();
        }
    }

    private static BenchmarkResult Failed(string device, string error) =>
        new() { Kind = BenchmarkKind.Gpu, Device = device, Error = error };

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

    private static double MeasureFlops(ID3D11Device device, ID3D11DeviceContext context, TimeSpan duration, CancellationToken ct)
    {
        const uint groups = 4096;
        const uint threads = groups * 256;
        using var bytecode = Compile(FlopsShader);
        using var shader = device.CreateComputeShader(bytecode);
        using var output = device.CreateBuffer(new BufferDescription(threads * 4, BindFlags.UnorderedAccess, ResourceUsage.Default,
            CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 4));
        using var uav = device.CreateUnorderedAccessView(output, null);
        using var cb = device.CreateBuffer(new BufferDescription(16, BindFlags.ConstantBuffer, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0));
        using var done = device.CreateQuery(QueryType.Event, QueryFlags.None);

        context.CSSetShader(shader);
        context.CSSetUnorderedAccessView(0, uav, 0);
        context.CSSetConstantBuffer(0, cb);

        var p = new FlopsParams { Iterations = 32, A = 0.9999f, B = 0.0001f };
        double TimeDispatches(int count)
        {
            context.UpdateSubresource(in p, cb, 0, 0, 0, null);
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < count; i++) context.Dispatch(groups, 1, 1);
            Wait(context, done, ct);
            return sw.Elapsed.TotalSeconds;
        }

        TimeDispatches(1); // aquecimento: compilação no driver e clocks subindo
        // Ajusta as iterações para cada despacho levar ~25 ms nesta GPU.
        double t = TimeDispatches(1);
        p.Iterations = (uint)Math.Clamp(p.Iterations * 0.025 / Math.Max(t, 1e-5), 16, 200_000);
        TimeDispatches(2);

        double flops = 0, seconds = 0;
        var total = Stopwatch.StartNew();
        while (total.Elapsed < duration)
        {
            ct.ThrowIfCancellationRequested();
            seconds += TimeDispatches(4);
            flops += 4.0 * threads * p.Iterations * FlopsPerIteration;
        }
        return flops / seconds / 1e9;
    }

    private static double MeasureBandwidth(ID3D11Device device, ID3D11DeviceContext context, TimeSpan duration, ulong dedicatedVram, CancellationToken ct)
    {
        // Dois buffers de 256 MB (128 MB em placas com pouca memória dedicada ou integradas).
        uint bytes = dedicatedVram >= 2UL << 30 ? 256u << 20 : 128u << 20;
        const uint groups = 4096;
        using var bytecode = Compile(CopyShader);
        using var shader = device.CreateComputeShader(bytecode);
        // A origem é lida por SRV (somente leitura, com cache de textura), como os jogos leem dados.
        var desc = new BufferDescription(bytes, BindFlags.UnorderedAccess | BindFlags.ShaderResource, ResourceUsage.Default,
            CpuAccessFlags.None, ResourceOptionFlags.BufferAllowRawViews, 0);
        using var src = device.CreateBuffer(desc);
        using var dst = device.CreateBuffer(desc);
        using var srcView = device.CreateShaderResourceView(src,
            new ShaderResourceViewDescription(src, Format.R32_Typeless, 0, bytes / 4, BufferExtendedShaderResourceViewFlags.Raw));
        using var dstView = device.CreateUnorderedAccessView(dst,
            new UnorderedAccessViewDescription(dst, Format.R32_Typeless, 0, bytes / 4, BufferUnorderedAccessViewFlags.Raw));
        using var cb = device.CreateBuffer(new BufferDescription(16, BindFlags.ConstantBuffer, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0));
        using var done = device.CreateQuery(QueryType.Event, QueryFlags.None);

        var p = new CopyParams { Count = bytes / 16, Stride = groups * 256 };
        context.UpdateSubresource(in p, cb, 0, 0, 0, null);
        context.CSSetShader(shader);
        context.CSSetShaderResource(0, srcView);
        context.CSSetUnorderedAccessView(0, dstView, 0);
        context.CSSetConstantBuffer(0, cb);

        double Pass(int count)
        {
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < count; i++) context.Dispatch(groups, 1, 1);
            Wait(context, done, ct);
            return sw.Elapsed.TotalSeconds;
        }

        Pass(2); // aquecimento
        double moved = 0, seconds = 0;
        var total = Stopwatch.StartNew();
        while (total.Elapsed < duration)
        {
            ct.ThrowIfCancellationRequested();
            seconds += Pass(4);
            moved += 4.0 * bytes * 2; // cada byte é lido e escrito
        }
        context.ClearState();
        return moved / seconds / 1e9;
    }

    private static void Wait(ID3D11DeviceContext context, ID3D11Query query, CancellationToken ct)
    {
        context.End(query);
        context.Flush();
        var sw = Stopwatch.StartNew();
        while (!context.IsDataAvailable(query))
        {
            if (sw.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("GPU");
            ct.ThrowIfCancellationRequested();
            Thread.Yield();
        }
    }

    private static Vortice.Direct3D.Blob Compile(string source)
    {
        // Sem macros nem include: o Vortice aceita nulo nos dois (a anotação dele não marca isso).
        var hr = Compiler.Compile(source, null!, null!, "main", "pulse1x_bench.hlsl", "cs_5_0",
            ShaderFlags.OptimizationLevel3, EffectFlags.None, out Vortice.Direct3D.Blob blob, out Vortice.Direct3D.Blob? errors);
        if (hr.Failure || blob is null)
            throw new InvalidOperationException(errors?.AsString() ?? "HLSL");
        errors?.Dispose();
        return blob;
    }
}
