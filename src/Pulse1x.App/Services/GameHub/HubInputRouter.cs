namespace Pulse1x.App.Services.GameHub;

/// <summary>Onde a entrada do controle está agindo neste momento.</summary>
public enum HubZone
{
    /// <summary>A grade de capas.</summary>
    Grid,
    /// <summary>A faixa superior: abas, busca e botões do cromo.</summary>
    Chrome,
    /// <summary>Os botões de ação do jogo em destaque (Jogar, Perfil, Favorito...).</summary>
    Hero,
    /// <summary>A faixa de cartões largos acima da biblioteca (recentes, favoritos, mais jogados).</summary>
    Highlight,
    /// <summary>O menu lateral aberto pelo botão View.</summary>
    Menu,
    /// <summary>O teclado virtual da busca.</summary>
    Keyboard,
}

/// <summary>
/// Decide quem recebe a entrada do controle no GameHub.
///
/// Antes disso, três lugares diferentes escutavam o mesmo evento — a ViewModel do hub, a janela
/// principal e a página — e todos reagiam ao mesmo toque. O resultado era a navegação "sumindo"
/// (dois handlers movendo o foco em direções diferentes) e ações disparando fora de contexto.
///
/// Agora existe um único dono da entrada, e ele sabe em que zona o usuário está. Cada zona tem uma
/// regra clara de para onde se vai ao sair dela, o que é o que faz a navegação parecer previsível:
///
///     Cromo  ── baixo ──▶  Destaque ── baixo ──▶  Grade
///       ▲                     ▲                     │
///       └──────── cima ───────┴──────── cima ───────┘
///
/// O menu e o teclado são modais: enquanto estão abertos, tudo vai para eles.
/// </summary>
public class HubInputRouter
{
    private HubZone _zone = HubZone.Grid;

    /// <summary>Zona ativa. Trocar de zona avisa a interface para mover o foco visual.</summary>
    public HubZone Zone
    {
        get => _zone;
        private set
        {
            if (_zone == value) return;
            _zone = value;
            ZoneChanged?.Invoke(value);
        }
    }

    /// <summary>Disparado quando a zona muda, para a página posicionar o foco.</summary>
    public event Action<HubZone>? ZoneChanged;

    /// <summary>Entra numa zona modal (menu ou teclado) e lembra de onde viemos.</summary>
    private HubZone _zoneBeforeModal = HubZone.Grid;

    public void EnterModal(HubZone modal)
    {
        if (_zone is not (HubZone.Menu or HubZone.Keyboard)) _zoneBeforeModal = _zone;
        Zone = modal;
    }

    /// <summary>Fecha a zona modal e devolve o controle para onde estava.</summary>
    public void ExitModal() => Zone = _zoneBeforeModal;

    public bool IsModal => _zone is HubZone.Menu or HubZone.Keyboard;

    /// <summary>Força uma zona (usado quando o usuário clica com o mouse em alguma área).</summary>
    public void SetZone(HubZone zone) => Zone = zone;

    /// <summary>
    /// Resolve para onde ir ao navegar verticalmente saindo de uma zona. Devolve null quando o
    /// movimento deve ser tratado dentro da própria zona.
    /// </summary>
    public HubZone? ResolveVerticalExit(GamepadDirection direction, bool atGridTopRow, bool hasSelection,
        bool hasHighlightRow = false)
    {
        return (_zone, direction) switch
        {
            // Da grade, subindo na primeira linha: passa pela faixa de destaques quando ela existe,
            // senão vai direto para as ações do jogo.
            (HubZone.Grid, GamepadDirection.Up) when atGridTopRow && hasHighlightRow => HubZone.Highlight,
            (HubZone.Grid, GamepadDirection.Up) when atGridTopRow && hasSelection => HubZone.Hero,
            (HubZone.Grid, GamepadDirection.Up) when atGridTopRow => HubZone.Chrome,

            // A faixa fica entre as ações do jogo e a grade.
            (HubZone.Highlight, GamepadDirection.Up) when hasSelection => HubZone.Hero,
            (HubZone.Highlight, GamepadDirection.Up) => HubZone.Chrome,
            (HubZone.Highlight, GamepadDirection.Down) => HubZone.Grid,

            // Do destaque: cima leva ao cromo, baixo desce para a faixa (ou direto à grade).
            (HubZone.Hero, GamepadDirection.Up) => HubZone.Chrome,
            (HubZone.Hero, GamepadDirection.Down) when hasHighlightRow => HubZone.Highlight,
            (HubZone.Hero, GamepadDirection.Down) => HubZone.Grid,

            // Do cromo, descendo: volta ao destaque (ou direto à grade, se não há seleção).
            (HubZone.Chrome, GamepadDirection.Down) when hasSelection => HubZone.Hero,
            (HubZone.Chrome, GamepadDirection.Down) => HubZone.Grid,

            _ => null,
        };
    }
}
