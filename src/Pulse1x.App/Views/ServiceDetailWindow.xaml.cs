using System.Diagnostics;
using System.Windows;
using Pulse1x.App.Localization;
using Pulse1x.App.Models;
using Pulse1x.App.Services;

namespace Pulse1x.App.Views;

/// <summary>Dados exibidos na janela de detalhes de um serviço.</summary>
public record ServiceDetailContext(
    string CompanyName,
    string CompanyIcon,
    string ServiceName,
    ServiceState State,
    string StatusUrl,
    ServiceIncident? Incident);

/// <summary>Janela simples de detalhes de um serviço: status atual, serviços/regiões afetados,
/// horários de início e última atualização do incidente, e link para a página oficial de status.
/// Os textos são montados aqui para manter o XAML enxuto e localizado (PT/EN).</summary>
public partial class ServiceDetailWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly string _statusUrl;

    public ServiceDetailWindow(ServiceDetailContext ctx)
    {
        InitializeComponent();
        _statusUrl = ctx.StatusUrl;

        IconText.Text = ctx.CompanyIcon;
        CompanyText.Text = ctx.CompanyName;
        ServiceText.Text = ctx.ServiceName;

        StatusText.Text = ServiceStatusVisuals.Emoji(ctx.State) + "  " + Loc.S(ServiceStatusVisuals.LabelKey(ctx.State));
        StatusText.Foreground = ToBrush(ServiceStatusVisuals.Hex(ctx.State));

        if (ctx.Incident is { } inc)
        {
            IncidentPanel.Visibility = Visibility.Visible;
            NoteText.Visibility = Visibility.Collapsed;

            AffectedText.Text = inc.AffectedServices.Count > 0
                ? string.Join(", ", inc.AffectedServices)
                : ctx.ServiceName;

            bool hasRegions = inc.AffectedRegions.Count > 0;
            RegionsLabel.Visibility = RegionsText.Visibility = hasRegions ? Visibility.Visible : Visibility.Collapsed;
            RegionsText.Text = hasRegions ? string.Join(", ", inc.AffectedRegions) : "";

            StartedText.Text = FormatDate(inc.StartedAt);
            UpdatedText.Text = FormatDate(inc.UpdatedAt);
            LatestText.Text = inc.LatestUpdate;
            LatestText.Visibility = string.IsNullOrWhiteSpace(inc.LatestUpdate) ? Visibility.Collapsed : Visibility.Visible;
        }
        else
        {
            // Sem incidente ativo: esconde o bloco de incidente e mostra uma nota conforme o estado.
            IncidentPanel.Visibility = Visibility.Collapsed;
            NoteText.Visibility = Visibility.Visible;
            NoteText.Text = ctx.State == ServiceState.Unknown
                ? Loc.S("Srv_DetailUnknownNote")
                : Loc.S("Srv_DetailNoIncident");
        }
    }

    private static string FormatDate(DateTimeOffset? dt) =>
        dt is { } d ? d.ToLocalTime().ToString("dd/MM/yyyy HH:mm") : "—";

    private static System.Windows.Media.Brush ToBrush(string hex)
    {
        try { return new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)); }
        catch { return System.Windows.Media.Brushes.Gray; }
    }

    // Abre a página oficial de status no navegador padrão (UseShellExecute para respeitar a associação).
    private void OpenPageButton_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(_statusUrl) { UseShellExecute = true }); }
        catch { /* sem navegador/URL inválida — silencioso, não vale derrubar a janela */ }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
