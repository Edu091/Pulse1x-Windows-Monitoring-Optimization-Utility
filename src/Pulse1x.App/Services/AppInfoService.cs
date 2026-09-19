using System.IO;
using System.Reflection;

namespace Pulse1x.App.Services;

/// <summary>
/// Identidade da versão em execução e o histórico de alterações.
///
/// A versão vem do próprio assembly, e não de um texto escrito à mão: a seção Sobre exibia
/// "Versão 1.0.0" desde o primeiro lançamento porque era uma literal nas traduções, que ninguém
/// lembrava de atualizar a cada publicação. Lendo do assembly, ela acompanha o
/// &lt;Version&gt; do projeto automaticamente — a mesma origem que o verificador de atualizações
/// já usa para decidir se há algo mais novo publicado.
/// </summary>
public static class AppInfoService
{
    /// <summary>Versão em execução, como "1.4.7" (sem o quarto número, sempre zero aqui).</summary>
    public static string Version
    {
        get
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            if (version is null) return "—";

            // O quarto componente (revisão) não é usado pelo Pulse1x; exibi-lo só faria a versão
            // divergir da que aparece nas publicações do GitHub ("1.4.7", não "1.4.7.0").
            return version.Build > 0 || version.Minor > 0
                ? $"{version.Major}.{version.Minor}.{version.Build}"
                : version.ToString(2);
        }
    }

    private static string? _changelog;

    /// <summary>
    /// O CHANGELOG.md embutido no executável. Lido uma única vez e guardado em memória.
    /// Devolve null quando o recurso não está presente, para a interface poder esconder a seção
    /// em vez de mostrar um espaço vazio.
    /// </summary>
    public static string? Changelog
    {
        get
        {
            if (_changelog is not null) return _changelog;

            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream("Pulse1x.App.CHANGELOG.md");
                if (stream is null) return null;

                using var reader = new StreamReader(stream);
                _changelog = reader.ReadToEnd();
                return _changelog;
            }
            catch
            {
                // Um histórico ilegível não pode impedir a seção Sobre de abrir.
                return null;
            }
        }
    }
}
