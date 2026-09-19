using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Pulse1x.App.Services.GameHub;

/// <summary>Direções de navegação (D-Pad ou analógico esquerdo).</summary>
public enum GamepadDirection { Up, Down, Left, Right }

/// <summary>
/// Ações da interface, independentes do controle físico usado.
///
/// O mapeamento segue o que se espera de um console, e cada ação tem um botão DIGITAL próprio —
/// gatilhos analógicos não disparam ação nenhuma, porque eles oscilam em repouso e acabavam
/// abrindo a busca sozinhos.
/// </summary>
public enum GamepadAction
{
    /// <summary>Confirmar / jogar (A no Xbox, X no PlayStation).</summary>
    Accept,
    /// <summary>Voltar (B no Xbox, O no PlayStation).</summary>
    Back,
    /// <summary>Favoritar (X no Xbox, quadrado no PlayStation).</summary>
    Favorite,
    /// <summary>Abrir a busca com o teclado virtual (Y no Xbox, triângulo no PlayStation).</summary>
    Search,
    /// <summary>Aba anterior (LB).</summary>
    PreviousTab,
    /// <summary>Próxima aba (RB).</summary>
    NextTab,
    /// <summary>Menu lateral (View/Select).</summary>
    Menu,
    /// <summary>Perfil/detalhes do item em destaque (Menu/Start).</summary>
    Details,
    /// <summary>Abre as ações do jogo selecionado (L2).</summary>
    GameActions,
}

/// <summary>
/// Fonte de entrada de um controle. Existe para que o GameHub não fique preso ao XInput: um
/// provedor de DualSense/DualShock por HID, ou de controles genéricos por DirectInput, entra como
/// mais uma implementação sem que a interface precise mudar.
/// </summary>
public interface IGamepadProvider
{
    string Name { get; }
    bool IsConnected { get; }

    /// <summary>Lê o estado atual do controle; null quando não há nenhum conectado.</summary>
    GamepadSnapshot? Poll();
}

/// <summary>Estado bruto de um controle num instante, já normalizado.</summary>
public record GamepadSnapshot(
    bool Up, bool Down, bool Left, bool Right,
    bool Accept, bool Back, bool Favorite, bool Details,
    bool LeftBumper, bool RightBumper, bool LeftTrigger, bool RightTrigger,
    bool View, bool Start,
    // Analógico separado do D-pad para que uma diagonal gere uma única direção de menu.
    // Valores normalizados entre -1 e 1; provedores antigos podem omiti-los.
    float LeftStickX = 0, float LeftStickY = 0);

/// <summary>
/// Navegação por controle no GameHub: converte a leitura contínua do controle em eventos discretos
/// de navegação, com repetição ao segurar (como qualquer interface de console).
///
/// O serviço só fica ativo enquanto o GameHub está visível — não faz sentido gastar CPU lendo o
/// controle numa tela que não usa isso — e é suspenso pelo Modo Gaming durante a partida, para não
/// disputar a entrada com o jogo.
/// </summary>
public class GamepadService : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly List<IGamepadProvider> _providers = new();

    private GamepadSnapshot? _previous;
    private readonly Dictionary<GamepadDirection, DateTime> _heldSince = new();
    private readonly Dictionary<GamepadDirection, DateTime> _lastRepeat = new();

    /// <summary>Espera antes de a primeira repetição começar (ao segurar a direção).</summary>
    private static readonly TimeSpan RepeatDelay = TimeSpan.FromMilliseconds(330);
    /// <summary>Intervalo entre repetições — rápido o bastante para varrer uma biblioteca grande.</summary>
    private static readonly TimeSpan RepeatInterval = TimeSpan.FromMilliseconds(85);

    public event Action<GamepadDirection>? Navigate;
    public event Action<GamepadAction>? Action;
    public event Action<bool>? ConnectionChanged;

    private bool _wasConnected;

    public bool IsConnected => _providers.Any(p => p.IsConnected);

    /// <summary>Nome do provedor ativo (para exibir "Controle conectado" na interface).</summary>
    public string? ActiveProviderName => _providers.FirstOrDefault(p => p.IsConnected)?.Name;

    public GamepadService()
    {
        _providers.Add(new XInputProvider());

        // ~60 Hz: responde na hora sem pesar. O timer só roda com o GameHub aberto.
        _timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => Tick();
    }

    /// <summary>Liga/desliga a leitura do controle (chamado pela visibilidade da página do GameHub).</summary>
    public void SetActive(bool active)
    {
        if (active && !_timer.IsEnabled) _timer.Start();
        else if (!active && _timer.IsEnabled)
        {
            _timer.Stop();
            _previous = null;
            _heldSince.Clear();
            _lastRepeat.Clear();
        }
    }

    private void Tick()
    {
        var snapshot = _providers.Select(p => p.Poll()).FirstOrDefault(s => s is not null);

        bool connected = snapshot is not null;
        if (connected != _wasConnected)
        {
            _wasConnected = connected;
            ConnectionChanged?.Invoke(connected);
        }

        if (snapshot is null)
        {
            _previous = null;
            return;
        }

        HandleDirections(snapshot);

        // Botões não repetem: valem só na transição de solto para pressionado.
        HandleButton(GamepadAction.Accept, snapshot.Accept, _previous?.Accept);
        HandleButton(GamepadAction.Back, snapshot.Back, _previous?.Back);
        HandleButton(GamepadAction.Favorite, snapshot.Favorite, _previous?.Favorite);
        HandleButton(GamepadAction.Search, snapshot.Details, _previous?.Details);      // Y
        HandleButton(GamepadAction.PreviousTab, snapshot.LeftBumper, _previous?.LeftBumper);
        HandleButton(GamepadAction.NextTab, snapshot.RightBumper, _previous?.RightBumper);
        HandleButton(GamepadAction.Menu, snapshot.View, _previous?.View);               // View/Select
        HandleButton(GamepadAction.Details, snapshot.Start, _previous?.Start);          // Menu/Start
        HandleButton(GamepadAction.GameActions, snapshot.LeftTrigger, _previous?.LeftTrigger); // L2

        _previous = snapshot;
    }

    /// <summary>
    /// Uma interface de console recebe um único passo por vez. O D-pad já é digital; para o
    /// analógico escolhemos o eixo mais inclinado, evitando que uma diagonal navegue duas vezes
    /// no mesmo quadro (a origem dos pulos e das aparentes inversões do seletor).
    /// </summary>
    private void HandleDirections(GamepadSnapshot snapshot)
    {
        GamepadDirection? direction = ResolveDirection(snapshot);

        foreach (var candidate in Enum.GetValues<GamepadDirection>())
            HandleDirection(candidate, candidate == direction);
    }

    private static GamepadDirection? ResolveDirection(GamepadSnapshot snapshot)
    {
        float horizontal = Math.Abs(snapshot.LeftStickX);
        float vertical = Math.Abs(snapshot.LeftStickY);

        if (horizontal > 0 || vertical > 0)
        {
            if (vertical >= horizontal)
                return snapshot.LeftStickY > 0 ? GamepadDirection.Up : GamepadDirection.Down;

            return snapshot.LeftStickX > 0 ? GamepadDirection.Right : GamepadDirection.Left;
        }

        // Provedores que só expõem botões também ficam protegidos contra duas direções opostas.
        if (snapshot.Up) return GamepadDirection.Up;
        if (snapshot.Down) return GamepadDirection.Down;
        if (snapshot.Left) return GamepadDirection.Left;
        if (snapshot.Right) return GamepadDirection.Right;
        return null;
    }

    /// <summary>Uma direção dispara ao ser pressionada e volta a disparar se continuar segurada.</summary>
    private void HandleDirection(GamepadDirection direction, bool pressed)
    {
        var now = DateTime.UtcNow;

        if (!pressed)
        {
            _heldSince.Remove(direction);
            _lastRepeat.Remove(direction);
            return;
        }

        if (!_heldSince.TryGetValue(direction, out var since))
        {
            _heldSince[direction] = now;
            _lastRepeat[direction] = now;
            Navigate?.Invoke(direction);
            return;
        }

        if (now - since < RepeatDelay) return;
        if (_lastRepeat.TryGetValue(direction, out var last) && now - last < RepeatInterval) return;

        _lastRepeat[direction] = now;
        Navigate?.Invoke(direction);
    }

    private void HandleButton(GamepadAction action, bool pressed, bool? wasPressed)
    {
        if (pressed && wasPressed != true) Action?.Invoke(action);
    }

    public void Dispose() => _timer.Stop();
}

// =====================================================================================
//  XInput
// =====================================================================================

/// <summary>
/// Controles compatíveis com XInput (Xbox e a grande maioria dos controles de PC, inclusive
/// DualSense/DualShock via Steam Input ou DS4Windows).
///
/// A DLL do XInput muda de nome conforme a versão do Windows, então tentamos as conhecidas em
/// ordem; se nenhuma carregar, o provedor apenas se declara desconectado e o GameHub segue
/// funcionando com teclado e mouse.
/// </summary>
public class XInputProvider : IGamepadProvider
{
    public string Name => "XInput";

    private const int MaxControllers = 4;
    private const int ErrorSuccess = 0;

    /// <summary>Zona morta do analógico esquerdo (valor recomendado pela documentação do XInput).</summary>
    private const short LeftThumbDeadzone = 7849;

    /// <summary>
    /// Limiares com histerese para os eixos do analógico.
    ///
    /// Um analógico não volta ao centro em linha reta: ao soltar, a mola o faz ultrapassar o zero
    /// e oscilar brevemente para o lado oposto. Com um limiar único, esse repique é lido como uma
    /// navegação na direção contrária — era exatamente a "inversão" do seletor. Com dois limiares,
    /// o eixo só ATIVA passando de 60% e só SOLTA abaixo de 35%, faixa em que o repique morre.
    /// </summary>
    private const float AxisEngage = 0.60f;
    private const float AxisRelease = 0.35f;

    /// <summary>Estado atual de cada eixo: -1 (negativo), 0 (centro) ou +1 (positivo).</summary>
    private int _stickXHeld;
    private int _stickYHeld;

    /// <summary>
    /// Converte a leitura bruta de um eixo em -1/0/+1 com histerese, devolvendo já normalizado
    /// para quem só precisa do sinal e da intensidade.
    /// </summary>
    private static float ReadAxis(short raw, ref int held)
    {
        float value = raw / (float)short.MaxValue;
        float magnitude = Math.Abs(value);

        if (held != 0)
        {
            // Já ativo: só solta quando volta perto do centro — ou quando cruza para o outro lado
            // com força de verdade (movimento intencional, não repique).
            if (magnitude < AxisRelease || Math.Sign(value) != held && magnitude < AxisEngage)
                held = 0;
        }
        else if (magnitude >= AxisEngage)
        {
            held = Math.Sign(value);
        }

        return held == 0 ? 0 : held * magnitude;
    }

    /// <summary>
    /// Gatilhos analógicos com histerese. O limiar de 30 (de 255) que a documentação do XInput
    /// sugere é pensado para ação em jogo; num menu ele dispara sozinho, porque gatilho analógico
    /// repousa em valores ligeiramente acima de zero e oscila. Com dois limiares, o gatilho só
    /// "liga" passando de 160 e só "desliga" abaixo de 80 — sem tremeliques.
    /// </summary>
    private const byte TriggerOn = 160;
    private const byte TriggerOff = 80;

    private bool _leftTriggerHeld;
    private bool _rightTriggerHeld;

    private int _connectedIndex = -1;
    private DateTime _lastScan = DateTime.MinValue;

    public bool IsConnected => _connectedIndex >= 0;

    public GamepadSnapshot? Poll()
    {
        // Procurar por controles em todas as portas a cada quadro seria desperdício; quando não há
        // nenhum conectado, varremos uma vez por segundo.
        if (_connectedIndex < 0)
        {
            if (DateTime.UtcNow - _lastScan < TimeSpan.FromSeconds(1)) return null;
            _lastScan = DateTime.UtcNow;

            for (int i = 0; i < MaxControllers; i++)
            {
                if (TryGetState(i, out _)) { _connectedIndex = i; break; }
            }
            if (_connectedIndex < 0) return null;
        }

        if (!TryGetState(_connectedIndex, out var state))
        {
            _connectedIndex = -1;
            return null;
        }

        var pad = state.Gamepad;
        ushort buttons = pad.wButtons;

        bool Button(ushort mask) => (buttons & mask) != 0;

        // O analógico esquerdo é enviado separado do D-pad: o serviço decide um só eixo para
        // cada passo. Isso impede uma diagonal de disparar duas navegações de uma vez.
        float stickX = ReadAxis(pad.sThumbLX, ref _stickXHeld);
        float stickY = ReadAxis(pad.sThumbLY, ref _stickYHeld);

        _leftTriggerHeld = _leftTriggerHeld
            ? pad.bLeftTrigger > TriggerOff
            : pad.bLeftTrigger > TriggerOn;
        _rightTriggerHeld = _rightTriggerHeld
            ? pad.bRightTrigger > TriggerOff
            : pad.bRightTrigger > TriggerOn;

        return new GamepadSnapshot(
            Up: Button(DPadUp),
            Down: Button(DPadDown),
            Left: Button(DPadLeft),
            Right: Button(DPadRight),
            Accept: Button(ButtonA),
            Back: Button(ButtonB),
            Favorite: Button(ButtonX),
            Details: Button(ButtonY),
            LeftBumper: Button(LeftShoulder),
            RightBumper: Button(RightShoulder),
            LeftTrigger: _leftTriggerHeld,
            RightTrigger: _rightTriggerHeld,
            // View/Select (BACK no XInput) abre o menu; Menu/Start é o botão de contexto.
            View: Button(ButtonBack),
            Start: Button(ButtonStart),
            LeftStickX: stickX,
            LeftStickY: stickY);
    }

    private static bool TryGetState(int index, out XInputState state)
    {
        state = default;
        try { return XInputGetState(index, out state) == ErrorSuccess; }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
        catch { return false; }
    }

    private const ushort DPadUp = 0x0001;
    private const ushort DPadDown = 0x0002;
    private const ushort DPadLeft = 0x0004;
    private const ushort DPadRight = 0x0008;
    private const ushort ButtonStart = 0x0010;
    private const ushort ButtonBack = 0x0020;   // View/Select nos controles Xbox
    private const ushort LeftShoulder = 0x0100;
    private const ushort RightShoulder = 0x0200;
    private const ushort ButtonA = 0x1000;
    private const ushort ButtonB = 0x2000;
    private const ushort ButtonX = 0x4000;
    private const ushort ButtonY = 0x8000;

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint dwPacketNumber;
        public XInputGamepad Gamepad;
    }

    // xinput1_4 existe no Windows 8 em diante; a resolução da DLL é feita pelo carregador do .NET,
    // e a falha é tratada em TryGetState.
    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern int XInputGetState(int userIndex, out XInputState state);
}
