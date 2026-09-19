using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using Pulse1x.App.Models.WinCustom;

namespace Pulse1x.App.Services.WinCustom;

/// <summary>
/// Camada nativa que aplica efeitos de composição a uma JANELA (HWND) de outro processo, sem
/// injetar nada nele.
///
/// É o motor real por trás da personalização da barra de tarefas e da janela do Explorer. Usa
/// SetWindowCompositionAttribute (user32), a mesma API que o TranslucentTB emprega: o
/// gerenciador de janelas do Windows (DWM) desenha o fundo do HWND com o efeito pedido. Como
/// quem desenha é o DWM, e não nós, não há código nosso rodando dentro do explorer.exe — esta
/// parte é segura e totalmente reversível: basta reaplicar ACCENT_DISABLED.
///
/// Limite conhecido e deliberado: a API só aceita um efeito de fundo + uma cor de tint. Não há
/// como pedir "gradiente", "imagem" ou "raio de borda" por aqui — esses dependem de desenhar uma
/// superfície própria, o que só o motor de injeção faz. O <see cref="CapabilityMatrix"/> já
/// impede que eles cheguem até aqui.
/// </summary>
public static class WindowComposition
{
    // =====================================================================================
    //  Interop
    // =====================================================================================

    private enum AccentState
    {
        ACCENT_DISABLED = 0,
        ACCENT_ENABLE_GRADIENT = 1,
        ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
        ACCENT_ENABLE_BLURBEHIND = 3,
        ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
        ACCENT_ENABLE_HOSTBACKDROP = 5,
    }

    [Flags]
    private enum AccentFlags
    {
        None = 0,
        /// <summary>Estende o efeito por todas as bordas — sem isto, sobra uma moldura opaca.</summary>
        DrawAllBorders = 0x0000001 | 0x0000002 | 0x0000004 | 0x0000008,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState AccentState;
        public AccentFlags AccentFlags;
        /// <summary>Cor no formato AABBGGRR (note a ordem invertida em relação a ARGB).</summary>
        public uint GradientColor;
        public int AnimationId;
    }

    private enum WindowCompositionAttribute
    {
        WCA_ACCENT_POLICY = 19,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public WindowCompositionAttribute Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowName);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr param);

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr param);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder buffer, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    // =====================================================================================
    //  Descoberta de janelas
    // =====================================================================================

    /// <summary>
    /// HWND da barra de tarefas principal. Classe estável desde o Windows 7 e mantida no
    /// Windows 11 (o Shell reconstrói a janela ao reiniciar o Explorer, por isso nunca guardamos
    /// o handle — procuramos de novo a cada aplicação).
    /// </summary>
    public static IntPtr FindTaskbar() => FindWindow("Shell_TrayWnd", null);

    /// <summary>
    /// Barras de tarefas dos monitores secundários. Cada uma é uma janela própria, então a
    /// personalização precisa percorrer todas para o resultado ficar consistente em multi-monitor.
    /// </summary>
    public static List<IntPtr> FindSecondaryTaskbars()
    {
        var found = new List<IntPtr>();
        IntPtr current = IntPtr.Zero;
        while ((current = FindWindowEx(IntPtr.Zero, current, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
            found.Add(current);
        return found;
    }

    /// <summary>Todas as barras de tarefas (principal + secundárias) existentes agora.</summary>
    public static List<IntPtr> FindAllTaskbars()
    {
        var all = new List<IntPtr>();
        var primary = FindTaskbar();
        if (primary != IntPtr.Zero) all.Add(primary);
        all.AddRange(FindSecondaryTaskbars());
        return all;
    }

    /// <summary>
    /// Uma barra de tarefas e o monitor em que ela está. O nome do dispositivo
    /// (<c>\\.\DISPLAY1</c>) é estável entre execuções, então serve de identificador do monitor
    /// escolhido pelo usuário — ao contrário do HWND, que muda quando o Explorer reinicia.
    /// </summary>
    public readonly record struct TaskbarOnMonitor(
        IntPtr Hwnd, string DeviceName, bool IsPrimary, int Width, int Height);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const uint MONITORINFOF_PRIMARY = 1;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);

    /// <summary>
    /// Barras de tarefas existentes, cada uma já associada ao seu monitor. É o que permite ao
    /// usuário escolher em qual tela a personalização vale — num notebook com monitor externo,
    /// mexer nas duas raramente é o desejado.
    /// </summary>
    public static List<TaskbarOnMonitor> FindTaskbarsWithMonitors()
    {
        var result = new List<TaskbarOnMonitor>();

        foreach (var hwnd in FindAllTaskbars())
        {
            var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero) continue;

            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (!GetMonitorInfo(monitor, ref info)) continue;

            result.Add(new TaskbarOnMonitor(
                hwnd,
                info.szDevice ?? "",
                (info.dwFlags & MONITORINFOF_PRIMARY) != 0,
                info.rcMonitor.Right - info.rcMonitor.Left,
                info.rcMonitor.Bottom - info.rcMonitor.Top));
        }

        // Monitor principal primeiro: é o que o usuário pensa como "a" barra de tarefas.
        return result.OrderByDescending(t => t.IsPrimary).ToList();
    }

    /// <summary>
    /// Barra de tarefas visível no momento? Com a ocultação automática ligada, o Windows põe a
    /// janela fora da área do monitor, e qualquer efeito aplicado a ela fica invisível — o que
    /// parece, de fora, que a personalização "não funcionou". A seção usa isto para avisar.
    /// </summary>
    public static bool IsTaskbarHidden(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return false;

        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero) return false;

        var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        if (!GetMonitorInfo(monitor, ref info)) return false;

        if (!GetWindowRect(hwnd, out var rect)) return false;

        // Considera oculta quando a maior parte da janela está fora do monitor.
        int visibleHeight = Math.Min(rect.Bottom, info.rcMonitor.Bottom) - Math.Max(rect.Top, info.rcMonitor.Top);
        int height = rect.Bottom - rect.Top;
        return height > 0 && visibleHeight < height / 2;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    /// <summary>
    /// Janelas abertas do Explorador de Arquivos. A classe "CabinetWClass" identifica as janelas
    /// de pastas; "ExplorerWClass" cobre variantes antigas. Só janelas visíveis entram, para não
    /// mexer em janelas-fantasma que o Shell mantém em segundo plano.
    /// </summary>
    public static List<IntPtr> FindExplorerWindows()
    {
        var found = new List<IntPtr>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            var sb = new StringBuilder(256);
            GetClassName(hwnd, sb, sb.Capacity);
            string cls = sb.ToString();
            if (cls is "CabinetWClass" or "ExplorerWClass")
                found.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return found;
    }

    // =====================================================================================
    //  Aplicação
    // =====================================================================================

    /// <summary>
    /// Último efeito aplicado a cada janela. Existe para não reenviar um accent idêntico ao que a
    /// janela já tem: cada chamada faz o DWM recompor a superfície, e em caminhos de vídeo frágeis
    /// (adaptadores DisplayLink/USB, drivers antigos, sessões remotas) essa recomposição repetida
    /// aparece como piscada na tela. Como a reaplicação é disparada por um vigia periódico e pelo
    /// reinício do Explorer, sem esta guarda o mesmo efeito seria reescrito várias vezes por
    /// sessão sem necessidade.
    /// </summary>
    private static readonly Dictionary<IntPtr, (AccentState State, uint Color)> LastApplied = new();

    private static readonly object Gate = new();

    /// <summary>
    /// Aplica a aparência a um HWND. Devolve false se o handle não for mais válido (a janela
    /// fechou, o Explorer reiniciou) — quem chama trata isso como "tentar de novo depois", nunca
    /// como erro fatal.
    /// </summary>
    public static bool Apply(IntPtr hwnd, ComponentAppearance appearance)
    {
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return false;

        var (state, color) = Translate(appearance);

        lock (Gate)
        {
            // Já está exatamente assim: não reescrever (ver LastApplied).
            if (LastApplied.TryGetValue(hwnd, out var current) &&
                current.State == state && current.Color == color)
                return true;
        }

        bool ok = SetAccent(hwnd, state, color);

        lock (Gate)
        {
            if (ok) LastApplied[hwnd] = (state, color);
            else LastApplied.Remove(hwnd);

            // O Shell recria as janelas ao reiniciar, então handles antigos se acumulariam.
            if (LastApplied.Count > 64) PruneDeadWindows();
        }

        return ok;
    }

    /// <summary>Descarta handles de janelas que já não existem.</summary>
    private static void PruneDeadWindows()
    {
        foreach (var dead in LastApplied.Keys.Where(h => !IsWindow(h)).ToList())
            LastApplied.Remove(dead);
    }

    /// <summary>
    /// Remove qualquer efeito aplicado por nós e devolve o HWND ao desenho padrão do Windows.
    /// É a operação de reversão — e é justamente por ela ser uma única chamada, sem resíduo em
    /// disco nem arquivo de sistema alterado, que esta parte da personalização é 100% reversível.
    /// </summary>
    public static bool Reset(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return false;

        // A reversão NUNCA é pulada pela guarda de idempotência: ela é o caminho de segurança e
        // precisa valer mesmo que o nosso registro esteja dessincronizado do estado real da
        // janela (por exemplo, depois de outro programa mexer no mesmo HWND).
        bool ok = SetAccent(hwnd, AccentState.ACCENT_DISABLED, 0);

        lock (Gate) LastApplied.Remove(hwnd);
        return ok;
    }

    /// <summary>
    /// Esquece o que foi aplicado, sem tocar nas janelas. Usado quando o Explorer reinicia: os
    /// HWND antigos morreram e os novos precisam receber o efeito de verdade.
    /// </summary>
    public static void ForgetAll()
    {
        lock (Gate) LastApplied.Clear();
    }

    private static bool SetAccent(IntPtr hwnd, AccentState state, uint color)
    {
        var policy = new AccentPolicy
        {
            AccentState = state,
            // Sem as bordas, o acrylic/blur deixa uma faixa opaca na borda da barra de tarefas.
            AccentFlags = state == AccentState.ACCENT_DISABLED ? AccentFlags.None : AccentFlags.DrawAllBorders,
            GradientColor = color,
            AnimationId = 0,
        };

        int size = Marshal.SizeOf<AccentPolicy>();
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, buffer, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                Data = buffer,
                SizeOfData = size,
            };
            return SetWindowCompositionAttribute(hwnd, ref data) != 0;
        }
        catch
        {
            // A API é não documentada: se uma build futura mudar a assinatura, preferimos
            // "não aplicou" a derrubar o Pulse.
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// Converte a aparência escolhida pelo usuário no par (estado do accent, cor) que a API
    /// entende.
    ///
    /// A cor carrega o ALFA, e é ele que governa a intensidade: num blur/acrylic, o alfa é o
    /// quanto do tint cobre o desfoque; num fundo sólido, é a opacidade do próprio fundo. Por
    /// isso opacidade, transparência e intensidade do tint convergem todas para este cálculo.
    /// </summary>
    private static (AccentState, uint) Translate(ComponentAppearance appearance)
    {
        var tint = ParseColor(appearance.TintColor);
        tint = ApplyLuminosity(tint, appearance.Luminosity);
        tint = ApplySaturation(tint, appearance.Saturation);

        double intensity = Math.Clamp(appearance.EffectIntensity, 0, 1);

        switch (appearance.Kind)
        {
            case AppearanceKind.Transparent:
                // Totalmente transparente: gradiente transparente com alfa zero.
                return (AccentState.ACCENT_ENABLE_TRANSPARENTGRADIENT, 0x00000000);

            case AppearanceKind.Translucent:
            {
                // Translúcido = cor com alfa parcial, sem desfoque.
                byte alpha = AlphaFrom(appearance.Opacity * (1 - appearance.Transparency));
                return (AccentState.ACCENT_ENABLE_TRANSPARENTGRADIENT, Bgra(tint, alpha));
            }

            case AppearanceKind.Blur:
            {
                byte alpha = AlphaFrom(appearance.TintIntensity * intensity);
                return (AccentState.ACCENT_ENABLE_BLURBEHIND, Bgra(tint, alpha));
            }

            case AppearanceKind.Acrylic:
            case AppearanceKind.Glass:
            {
                // Glass e Acrylic compartilham o mesmo backdrop nativo; o Glass usa um tint mais
                // leve e mais claro, que é o que dá a ele a aparência de vidro em vez de material.
                double tintAmount = appearance.Kind == AppearanceKind.Glass
                    ? appearance.TintIntensity * 0.55
                    : appearance.TintIntensity;
                byte alpha = AlphaFrom(tintAmount * intensity);
                return (AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND, Bgra(tint, alpha));
            }

            case AppearanceKind.SolidColor:
            {
                byte alpha = AlphaFrom(appearance.Opacity);
                return (AccentState.ACCENT_ENABLE_GRADIENT, Bgra(tint, alpha));
            }

            default:
                return (AccentState.ACCENT_DISABLED, 0);
        }
    }

    // =====================================================================================
    //  Utilidades de cor
    // =====================================================================================

    /// <summary>A API espera AABBGGRR — trocar R e B aqui é o erro clássico com esta struct.</summary>
    private static uint Bgra(Color c, byte alpha) =>
        (uint)((alpha << 24) | (c.B << 16) | (c.G << 8) | c.R);

    private static byte AlphaFrom(double value) =>
        (byte)Math.Clamp(value * 255.0, 0, 255);

    private static Color ParseColor(string? hex)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(hex)) return Color.FromRgb(0x1A, 0x1A, 0x24);
            return (Color)ColorConverter.ConvertFromString(hex)!;
        }
        catch { return Color.FromRgb(0x1A, 0x1A, 0x24); }
    }

    /// <summary>Clareia (positivo) ou escurece (negativo) a cor, -1 a 1.</summary>
    private static Color ApplyLuminosity(Color c, double amount)
    {
        if (Math.Abs(amount) < 0.001) return c;
        if (amount > 0)
            return Color.FromRgb(
                (byte)(c.R + (255 - c.R) * amount),
                (byte)(c.G + (255 - c.G) * amount),
                (byte)(c.B + (255 - c.B) * amount));

        double k = 1 + amount;
        return Color.FromRgb((byte)(c.R * k), (byte)(c.G * k), (byte)(c.B * k));
    }

    /// <summary>Satura (>1) ou dessatura (&lt;1) em torno da luminância perceptual.</summary>
    private static Color ApplySaturation(Color c, double saturation)
    {
        if (Math.Abs(saturation - 1.0) < 0.001) return c;
        double gray = 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;
        return Color.FromRgb(
            (byte)Math.Clamp(gray + (c.R - gray) * saturation, 0, 255),
            (byte)Math.Clamp(gray + (c.G - gray) * saturation, 0, 255),
            (byte)Math.Clamp(gray + (c.B - gray) * saturation, 0, 255));
    }
}
