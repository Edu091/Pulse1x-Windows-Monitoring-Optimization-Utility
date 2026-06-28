using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Pulse1x.App.Services;

namespace Pulse1x.App.Views;

public partial class AboutPage : Page
{
    // PLACEHOLDER: troque pela chave Pix aleatória (EVP) real antes de publicar. Uma chave
    // aleatória não expõe CPF/e-mail/telefone — é o tipo recomendado para exibição pública.
    private const string PixKey = "COLOQUE-AQUI-SUA-CHAVE-PIX-ALEATORIA";

    public AboutPage()
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
