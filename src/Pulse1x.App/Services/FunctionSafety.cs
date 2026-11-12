using Pulse1x.App.ViewModels;

namespace Pulse1x.App.Services;

/// <summary>
/// Nível de segurança de cada função do app que muda algo no Windows (selo Seguro / Moderado /
/// Avançado mostrado pelo controle <see cref="Controls.FunctionInfo"/>). A chave é a mesma usada
/// nos textos "&lt;chave&gt;_Pros" e "&lt;chave&gt;_Cons". Cada área do app declara a sua lista em
/// um arquivo parcial (FunctionSafety.&lt;Área&gt;.cs); o que não estiver listado é "Seguro".
///
/// Critério: Seguro = sem efeito colateral perceptível. Moderado = troca algo (bateria, recurso do
/// Windows, conexão por alguns segundos) por desempenho, ou pede atenção. Avançado = mexe na base
/// do sistema, pode exigir reinício ou ter efeito difícil de perceber/desfazer.
/// </summary>
public static partial class FunctionSafety
{
    private static readonly Lazy<Dictionary<string, ToolSafety>> Map = new(() =>
    {
        var map = new Dictionary<string, ToolSafety>(StringComparer.Ordinal);
        foreach (var list in new[] { Advanced, Special, PostFormat, Sound, RecordingArea })
            foreach (var (key, level) in list)
                map[key] = level;
        return map;
    });

    public static ToolSafety Of(string key) =>
        Map.Value.TryGetValue(key, out var level) ? level : LatencyViewModel.SafetyOf(key);

    /// <summary>Todas as chaves declaradas (para os testes conferirem os textos).</summary>
    public static IEnumerable<string> AllKeys => Map.Value.Keys;
}
