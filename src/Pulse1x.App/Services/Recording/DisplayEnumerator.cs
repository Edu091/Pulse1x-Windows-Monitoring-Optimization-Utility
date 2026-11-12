using Vortice.DXGI;

namespace Pulse1x.App.Services.Recording;

/// <summary>Um monitor que pode ser gravado, com a GPU onde ele está ligado.</summary>
public sealed record CaptureMonitor(
    string DeviceName,      // \\.\DISPLAY1
    int Number,             // 1, 2, 3... (ordem para o usuário)
    int AdapterIndex,       // índice do IDXGIFactory1::EnumAdapters1 (o mesmo do -init_hw_device d3d11va=x:N)
    int OutputIndex,        // índice da saída dentro da GPU (output_idx do ddagrab)
    uint VendorId,          // 0x10DE NVIDIA, 0x1002 AMD, 0x8086 Intel
    string AdapterName,
    int Left, int Top, int Width, int Height, // em pixels físicos, coordenadas da área de trabalho virtual
    bool IsPrimary)
{
    public const uint VendorNvidia = 0x10DE, VendorAmd = 0x1002, VendorIntel = 0x8086;
}

/// <summary>Lista os monitores pela DXGI — a mesma enumeração que o ddagrab do ffmpeg usa.</summary>
public static class DisplayEnumerator
{
    public static List<CaptureMonitor> GetMonitors()
    {
        var list = new List<CaptureMonitor>();
        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            for (uint a = 0; factory.EnumAdapters1(a, out var adapter).Success; a++)
            {
                using (adapter)
                {
                    var desc = adapter.Description1;
                    if ((desc.Flags & AdapterFlags.Software) != 0) continue;
                    for (uint o = 0; adapter.EnumOutputs(o, out var output).Success; o++)
                    {
                        using (output)
                        {
                            var od = output.Description;
                            if (!od.AttachedToDesktop) continue;
                            var r = od.DesktopCoordinates;
                            list.Add(new CaptureMonitor(
                                od.DeviceName, 0, (int)a, (int)o, desc.VendorId, desc.Description,
                                r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top,
                                r.Left == 0 && r.Top == 0));
                        }
                    }
                }
            }
        }
        catch { /* sem DXGI (sessão remota antiga): cai na lista vazia e o gdigrab assume */ }

        // Numeração estável: da esquerda para a direita, como no "Identificar" do Windows.
        return list
            .OrderBy(m => m.Left).ThenBy(m => m.Top)
            .Select((m, i) => m with { Number = i + 1 })
            .ToList();
    }

    /// <summary>O monitor salvo nas configurações, ou o principal.</summary>
    public static CaptureMonitor? Resolve(IReadOnlyList<CaptureMonitor> monitors, string? deviceName) =>
        monitors.FirstOrDefault(m => string.Equals(m.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
        ?? monitors.FirstOrDefault(m => m.IsPrimary)
        ?? monitors.FirstOrDefault();
}
