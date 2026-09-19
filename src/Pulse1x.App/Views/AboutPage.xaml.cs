using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Pulse1x.App.Localization;
using Pulse1x.App.Services;

namespace Pulse1x.App.Views;

public partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();

        // A versão vem do assembly: antes era uma literal nas traduções e ficou parada em
        // "1.0.0" por todas as publicações seguintes.
        VersionText.Text = string.Format(Loc.Instance["About_Version"], AppInfoService.Version);

        if (AppInfoService.Changelog is { Length: > 0 } changelog)
        {
            ChangelogToggle.Content = Loc.Instance["About_Changelog"];
            RenderChangelog(changelog);
        }
        else
        {
            // Sem histórico embutido, o botão não teria o que mostrar.
            ChangelogToggle.Visibility = Visibility.Collapsed;
        }
    }

    private void ToggleChangelog(object sender, RoutedEventArgs e)
    {
        bool showing = ChangelogPanel.Visibility == Visibility.Visible;
        ChangelogPanel.Visibility = showing ? Visibility.Collapsed : Visibility.Visible;
        ChangelogToggle.Content = Loc.Instance[showing ? "About_Changelog" : "About_ChangelogHide"];
    }

    /// <summary>
    /// Converte o CHANGELOG.md em blocos de texto.
    ///
    /// É um tradutor deliberadamente pequeno: o arquivo é escrito por nós e usa só títulos de
    /// versão ("## 1.4.7 — data"), subtítulos em negrito, itens de lista e parágrafos. Trazer uma
    /// biblioteca de Markdown para isso adicionaria dependência sem ganho.
    /// </summary>
    private void RenderChangelog(string markdown)
    {
        foreach (string rawLine in markdown.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r').Trim();

            // O título do arquivo ("# Histórico de versões") já é o rótulo do botão.
            if (line.Length == 0 || line.StartsWith("# ")) continue;

            if (line.StartsWith("## "))
            {
                ChangelogContent.Children.Add(new TextBlock
                {
                    Text = line[3..],
                    FontSize = 15,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, ChangelogContent.Children.Count == 0 ? 0 : 18, 0, 6),
                    TextWrapping = TextWrapping.Wrap,
                });
                continue;
            }

            if (line.StartsWith("- "))
            {
                ChangelogContent.Children.Add(BulletItem(line[2..]));
                continue;
            }

            // Parágrafo solto (as introduções de cada seção), inclusive os subtítulos em negrito.
            ChangelogContent.Children.Add(new TextBlock
            {
                Inlines = { InlineFor(line) },
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.85,
                Margin = new Thickness(0, 6, 0, 2),
            });
        }
    }

    /// <summary>Um item de lista com marcador alinhado, para o texto não quebrar embaixo do ponto.</summary>
    private static Grid BulletItem(string text)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var bullet = new TextBlock
        {
            Text = "•",
            Margin = new Thickness(0, 0, 8, 0),
            Opacity = 0.6,
        };

        var body = new TextBlock
        {
            Inlines = { InlineFor(text) },
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.85,
        };

        Grid.SetColumn(bullet, 0);
        Grid.SetColumn(body, 1);
        grid.Children.Add(bullet);
        grid.Children.Add(body);
        return grid;
    }

    /// <summary>
    /// Resolve a ênfase em negrito (**assim**) e o código entre crases, que o histórico usa para
    /// nomes de comandos. O resto do texto vai como está.
    /// </summary>
    private static Span InlineFor(string text)
    {
        var span = new Span();
        int index = 0;

        while (index < text.Length)
        {
            int bold = text.IndexOf("**", index, System.StringComparison.Ordinal);
            int code = text.IndexOf('`', index);

            // O marcador que vier primeiro decide o próximo trecho.
            int next = bold >= 0 && (code < 0 || bold < code) ? bold : code;
            if (next < 0)
            {
                span.Inlines.Add(new Run(text[index..]));
                break;
            }

            if (next > index) span.Inlines.Add(new Run(text[index..next]));

            if (next == bold)
            {
                int close = text.IndexOf("**", next + 2, System.StringComparison.Ordinal);
                if (close < 0) { span.Inlines.Add(new Run(text[next..])); break; }

                span.Inlines.Add(new Bold(new Run(text[(next + 2)..close])));
                index = close + 2;
            }
            else
            {
                int close = text.IndexOf('`', next + 1);
                if (close < 0) { span.Inlines.Add(new Run(text[next..])); break; }

                span.Inlines.Add(new Run(text[(next + 1)..close])
                {
                    FontFamily = new System.Windows.Media.FontFamily("Consolas, Courier New"),
                });
                index = close + 1;
            }
        }

        return span;
    }
}
