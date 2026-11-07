using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Pulse1x.Hub.Tests;

/// <summary>
/// Carrega o Pulse1xVoice.dll embutido no app como o Equalizer APO faz (VSTPluginMain, setSampleRate,
/// setChunk, processReplacing) e confere que um seno de 200 Hz sai em 400 Hz com pitch=12.
/// </summary>
internal static unsafe class VoicePluginProbe
{
    public static void Run()
    {
        using var stream = typeof(Pulse1x.App.Services.Sound.EqualizerApoService).Assembly
            .GetManifestResourceStream("Pulse1x.App.Native.Pulse1xVoice.dll")
            ?? throw new Exception("plugin not embedded");
        string path = Path.Combine(Path.GetTempPath(), $"pulse1x-voice-{Guid.NewGuid():N}.dll");
        using (var file = File.Create(path)) stream.CopyTo(file);
        IntPtr lib = NativeLibrary.Load(path);
        try
        {
            var main = (delegate* unmanaged<IntPtr, IntPtr>)NativeLibrary.GetExport(lib, "VSTPluginMain");
            IntPtr fx = main(IntPtr.Zero);
            TestSuite.Equal(0x56737450, Marshal.ReadInt32(fx, 0));
            var dispatch = (delegate* unmanaged<IntPtr, int, int, IntPtr, IntPtr, float, IntPtr>)Marshal.ReadIntPtr(fx, 8);
            var process = (delegate* unmanaged<IntPtr, IntPtr, IntPtr, int, void>)Marshal.ReadIntPtr(fx, 120);
            dispatch(fx, 10, 0, 0, 0, 48000);
            var chunk = Encoding.ASCII.GetBytes("pitch=12");
            fixed (byte* b = chunk) dispatch(fx, 24, 1, chunk.Length, (IntPtr)b, 0);

            const int total = 96000, block = 480;
            var output = new float[total];
            var inL = new float[block]; var inR = new float[block]; var outL = new float[block]; var outR = new float[block];
            var ins = stackalloc IntPtr[2];
            var outs = stackalloc IntPtr[2];
            fixed (float* a = inL, b = inR, c = outL, d = outR)
            {
                ins[0] = (IntPtr)a; ins[1] = (IntPtr)b; outs[0] = (IntPtr)c; outs[1] = (IntPtr)d;
                for (int pos = 0; pos < total; pos += block)
                {
                    for (int i = 0; i < block; i++) inL[i] = inR[i] = (float)(0.3 * Math.Sin(2 * Math.PI * 200 * (pos + i) / 48000));
                    process(fx, (IntPtr)ins, (IntPtr)outs, block);
                    Array.Copy(outL, 0, output, pos, block);
                }
            }
            dispatch(fx, 1, 0, 0, 0, 0);

            int crossings = 0;
            for (int i = total / 2 + 1; i < total; i++) if (output[i - 1] < 0 && output[i] >= 0) crossings++;
            double freq = crossings * 48000.0 / (total / 2);
            if (Math.Abs(freq - 400) > 15) throw new Exception($"pitch=12 gave {freq:0} Hz");
        }
        finally
        {
            NativeLibrary.Free(lib);
            try { File.Delete(path); } catch { }
        }
    }
}
