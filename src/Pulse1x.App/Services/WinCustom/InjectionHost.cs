using System.IO;
using Pulse1x.App.Models.WinCustom;

namespace Pulse1x.App.Services.WinCustom;

/// <summary>
/// Ponte para o motor de injeção (BETA) — a parte que alcança Menu Iniciar, Configurações e as
/// regiões internas do Explorer.
///
/// ---------------------------------------------------------------------------------------
///  ESTADO ATUAL: a DLL nativa NÃO acompanha esta build.
/// ---------------------------------------------------------------------------------------
/// Estilizar essas superfícies significa desenhar DENTRO de outro processo: elas são elementos
/// XAML de explorer.exe, StartMenuExperienceHost.exe e SystemSettings.exe, sem HWND próprio, e
/// por isso nenhuma API externa as alcança (é exatamente por isso que o Windhawk injeta uma DLL).
/// Esse componente é um projeto C++ à parte, e o toolchain MSVC não está presente nesta máquina,
/// então ele não pôde ser compilado aqui.
///
/// Esta classe é o CONTRATO com o qual o motor conversa. Ela procura a DLL no disco e, quando ela
/// não existe, responde <see cref="IsAvailable"/> = false — e todo o resto do sistema se comporta
/// como se a opção fosse incompatível com a máquina: a interface marca esses alvos como
/// indisponíveis e o motor nunca tenta aplicá-los. É o mesmo caminho já previsto para
/// incompatibilidade de build, então nada quebra e nada é prometido em falso.
///
/// Quando a DLL for compilada e colocada ao lado do executável, <see cref="IsAvailable"/> passa a
/// true e os alvos de injeção se habilitam sozinhos, sem mudar nada aqui.
///
/// SEGURANÇA E REVERSIBILIDADE (válidas para quando o motor existir): a injeção é sempre em
/// memória — nenhum arquivo do Windows é modificado, nenhum recurso é substituído em disco.
/// Encerrar o host (<see cref="Shutdown"/>) descarrega a DLL e os processos alvo voltam ao
/// visual padrão; reiniciar o Explorer também. Não há estado persistente para "sobrar".
/// </summary>
public static class InjectionHost
{
    /// <summary>Nome da DLL nativa, procurada ao lado do executável do Pulse.</summary>
    private const string NativeLibraryName = "Pulse1x.WinCustom.Native.dll";

    private static bool? _available;

    /// <summary>
    /// Se o motor de injeção pode ser usado nesta instalação. É false enquanto a DLL nativa não
    /// estiver presente — o que faz todo o subsistema tratar os alvos de injeção como
    /// indisponíveis, em vez de fingir que funcionam.
    /// </summary>
    public static bool IsAvailable
    {
        get
        {
            if (_available is bool cached) return cached;

            try
            {
                string dir = Path.GetDirectoryName(Environment.ProcessPath ?? "") ?? "";
                _available = !string.IsNullOrEmpty(dir)
                             && File.Exists(Path.Combine(dir, NativeLibraryName));
            }
            catch
            {
                _available = false;
            }

            return _available.Value;
        }
    }

    /// <summary>
    /// Motivo, em uma frase, de o motor estar indisponível — mostrado na seção para o usuário
    /// entender por que os alvos beta estão apagados.
    /// </summary>
    public static string UnavailableExplanationKey => "WinCustom_InjectionMissing";

    /// <summary>
    /// Aplicaria a aparência a um alvo interno. Sem a DLL, devolve "indisponível" — nunca tenta,
    /// nunca falha silenciosamente.
    /// </summary>
    public static ApplyOutcome Apply(string target, ComponentAppearance appearance, CustomizationWatchdog watchdog)
    {
        if (!IsAvailable)
            return ApplyOutcome.Skipped(target, UnavailableReason.NeedsInjection);

        // O motor nativo entra aqui quando existir. A proteção contra falhas já envolve a
        // chamada, porque injeção é justamente a parte que pode derrubar o processo alvo.
        if (!watchdog.BeginAttempt(target))
            return ApplyOutcome.Failed(target, "quarantined");

        try
        {
            bool ok = NativeBridge.ApplyRegion(target, appearance);
            if (!ok)
            {
                watchdog.RecordFailure(target, "native apply failed");
                return ApplyOutcome.Failed(target, "native apply failed");
            }

            watchdog.CommitSuccess(target);
            return ApplyOutcome.Ok(target);
        }
        catch (Exception ex)
        {
            watchdog.RecordFailure(target, ex.Message);
            return ApplyOutcome.Failed(target, ex.Message);
        }
    }

    /// <summary>Devolve um processo alvo ("Explorer", "Start", "Settings") ao visual padrão.</summary>
    public static void RestoreTarget(string target)
    {
        if (!IsAvailable) return;
        try { NativeBridge.RestoreTarget(target); } catch { /* restauração é sempre melhor esforço */ }
    }

    /// <summary>
    /// Descarrega a DLL de todos os processos alvo. Chamado na restauração completa e ao sair do
    /// Pulse, para que nada do nosso código continue dentro do Shell.
    /// </summary>
    public static void Shutdown()
    {
        if (!IsAvailable) return;
        try { NativeBridge.Shutdown(); } catch { /* idem */ }
    }
}

/// <summary>
/// Assinaturas da DLL nativa. Ficam isoladas para que a ausência do arquivo seja detectada por
/// <see cref="InjectionHost.IsAvailable"/> ANTES de qualquer P/Invoke ser resolvido — carregar
/// uma DLL inexistente lançaria DllNotFoundException no primeiro uso.
/// </summary>
internal static class NativeBridge
{
    // A implementação nativa não acompanha esta build (ver InjectionHost). Os métodos existem
    // para fixar o contrato; nunca são chamados enquanto IsAvailable for false.

    public static bool ApplyRegion(string target, ComponentAppearance appearance) =>
        throw new NotSupportedException("O motor de injeção nativo não está presente nesta instalação.");

    public static void RestoreTarget(string target) =>
        throw new NotSupportedException("O motor de injeção nativo não está presente nesta instalação.");

    public static void Shutdown() =>
        throw new NotSupportedException("O motor de injeção nativo não está presente nesta instalação.");
}
