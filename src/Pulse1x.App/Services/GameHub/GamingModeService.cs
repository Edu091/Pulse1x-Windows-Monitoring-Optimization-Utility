namespace Pulse1x.App.Services.GameHub;

/// <summary>
/// Modo Gaming do Pulse1x: enquanto um jogo estiver aberto, o próprio app se cala.
///
/// Não é um "modo" separado com código paralelo — é um único sinal que as partes do Pulse1x que
/// consomem recursos observam para se suspenderem:
///
///   • os painéis em tempo real (Dashboard, Latência, Status dos Servidores) param de atualizar;
///   • a biblioteca do GameHub para de varrer o disco atrás de jogos novos;
///   • a busca e o carregamento de capas são suspensos;
///   • as animações da interface são desligadas (nada é animado numa janela que ninguém está vendo);
///   • os caches de imagem em memória são liberados.
///
/// Ao fechar o jogo, tudo é religado exatamente como estava — inclusive a preferência de animações
/// do usuário, que é guardada antes de ser desligada.
/// </summary>
public class GamingModeService
{
    private bool _active;
    private bool _animationsBefore = true;

    /// <summary>Disparado quando o modo liga (true) ou desliga (false).</summary>
    public event Action<bool>? StateChanged;

    public bool IsActive => _active;

    /// <summary>Nome do jogo que motivou o modo (só para exibição na interface).</summary>
    public string? CurrentGameName { get; private set; }

    public void Enter(string? gameName)
    {
        if (_active) return;
        _active = true;
        CurrentGameName = gameName;

        // Guarda a preferência do usuário antes de desligar, para devolvê-la depois.
        _animationsBefore = AnimationSettings.Enabled;
        AnimationSettings.Enabled = false;

        ReleaseCaches();
        StateChanged?.Invoke(true);
    }

    public void Exit()
    {
        if (!_active) return;
        _active = false;
        CurrentGameName = null;

        AnimationSettings.Enabled = _animationsBefore;
        StateChanged?.Invoke(false);
    }

    /// <summary>
    /// Libera o que o Pulse1x guarda em memória e pode reconstruir sozinho depois — sobretudo os
    /// bitmaps das capas, que são a maior parte da memória do GameHub. A coleta é do tipo que
    /// devolve as páginas ao sistema, para a RAM liberada ficar disponível ao jogo de verdade.
    /// </summary>
    private static void ReleaseCaches()
    {
        try
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }
        catch { }
    }
}
