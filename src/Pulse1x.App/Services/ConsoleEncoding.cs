using System.Text;

namespace Pulse1x.App.Services;

/// <summary>
/// Decodificação correta da saída das ferramentas de console do Windows (powercfg, netsh, ipconfig).
///
/// Essas ferramentas escrevem na página de código do console — CP850 num Windows em português, não
/// UTF-8. Lendo como UTF-8, qualquer palavra acentuada chega corrompida: um plano chamado
/// "Desempenho Máximo" vira "Desempenho M?ximo" na tela, e comparações de texto com acento
/// simplesmente nunca casam. Este helper descobre a página certa uma vez e a entrega pronta para
/// <c>ProcessStartInfo.StandardOutputEncoding</c>.
/// </summary>
public static class ConsoleEncoding
{
    private static Encoding? _oem;
    private static bool _resolved;

    /// <summary>Página de código do console desta máquina (null quando não for possível determinar,
    /// caso em que o .NET usa o padrão dele).</summary>
    public static Encoding? Oem
    {
        get
        {
            if (_resolved) return _oem;
            _resolved = true;

            try
            {
                // As páginas legadas não vêm no .NET moderno; este provedor as disponibiliza.
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                _oem = Encoding.GetEncoding((int)GetOEMCP());
            }
            catch
            {
                _oem = null;
            }

            return _oem;
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint GetOEMCP();
}
