using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Pulse1x.App.Services;

namespace Pulse1x.App.Views;

public partial class DonatePage : Page
{
    // Chave Pix aleatória (EVP): não expõe CPF/e-mail/telefone, é o tipo recomendado para
    // exibição pública — não dá pra usar pra nada além de receber um depósito nesta conta.
    private const string PixKey = "299b9f7b-b640-4a77-b33b-af04bb895319";

    public DonatePage()
    {
        InitializeComponent();
        PixKeyText.Text = PixKey;
    }

    private void CopyPixKey_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(PixKey);

        // Mostra "Chave copiada!" por 1,8s e desaparece — sem travar a UI (DispatcherTimer).
        Animations.FadeTo(CopiedFeedback, 1.0);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.8) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Animations.FadeTo(CopiedFeedback, 0.0);
        };
        timer.Start();
    }
}
