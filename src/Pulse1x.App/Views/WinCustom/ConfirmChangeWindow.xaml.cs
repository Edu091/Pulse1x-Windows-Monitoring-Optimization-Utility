using System.Windows;
using System.Windows.Threading;
using Pulse1x.App.Localization;

namespace Pulse1x.App.Views.WinCustom;

/// <summary>
/// Confirmação com reversão automática, no mesmo espírito do diálogo de resolução de tela do
/// Windows: a personalização já está aplicada e visível, e o usuário tem alguns segundos para
/// dizer se quer mantê-la. Sem confirmação, o Pulse desfaz sozinho.
///
/// É a proteção mais importante da seção. A <see cref="CustomizationWatchdog"/> reage a falhas
/// que derrubam processos, mas não a um tema que simplesmente ficou ilegível — barra de tarefas
/// invisível, texto sem contraste, cor que some no papel de parede. Nesses casos o usuário
/// costuma conseguir ver o problema mas nem sempre consegue voltar atrás com facilidade; aqui,
/// não fazer nada é o caminho seguro.
/// </summary>
public partial class ConfirmChangeWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly DispatcherTimer _timer;
    private int _secondsLeft;

    /// <summary>True se o usuário confirmou dentro do prazo.</summary>
    public bool Confirmed { get; private set; }

    private ConfirmChangeWindow(string themeName, int seconds)
    {
        InitializeComponent();

        _secondsLeft = seconds;
        TitleText.Text = Loc.S("WinCustom_ConfirmTitle");
        MessageText.Text = Loc.F("WinCustom_ConfirmMessage", themeName);
        KeepButton.Content = Loc.S("WinCustom_ConfirmKeep");
        RevertButton.Content = Loc.S("WinCustom_ConfirmRevert");
        UpdateCountdown();

        KeepButton.Click += (_, _) => Finish(confirmed: true);
        RevertButton.Click += (_, _) => Finish(confirmed: false);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) =>
        {
            _secondsLeft--;
            if (_secondsLeft <= 0)
            {
                // Esgotou o prazo sem resposta: reverter é sempre a escolha segura.
                Finish(confirmed: false);
                return;
            }
            UpdateCountdown();
        };
        _timer.Start();

        // Fechar a janela no "X" conta como não confirmar.
        Closed += (_, _) => _timer.Stop();
    }

    private void UpdateCountdown()
    {
        CountdownText.Text = Loc.F("WinCustom_ConfirmCountdown", _secondsLeft);
        CountdownBar.Value = _secondsLeft;
    }

    private void Finish(bool confirmed)
    {
        _timer.Stop();
        Confirmed = confirmed;
        DialogResult = confirmed;
        Close();
    }

    /// <summary>
    /// Mostra a confirmação e devolve true se o usuário quiser manter a personalização.
    /// A janela fica sempre visível por cima, porque um tema ruim pode ter deixado a barra de
    /// tarefas (e portanto a forma de voltar ao Pulse) difícil de enxergar.
    /// </summary>
    public static bool Ask(Window? owner, string themeName, int seconds = 5)
    {
        var dialog = new ConfirmChangeWindow(themeName, seconds)
        {
            Owner = owner,
            WindowStartupLocation = owner is null
                ? WindowStartupLocation.CenterScreen
                : WindowStartupLocation.CenterOwner,
            Topmost = true,
        };

        dialog.ShowDialog();
        return dialog.Confirmed;
    }
}
