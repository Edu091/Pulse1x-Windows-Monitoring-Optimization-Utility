using System.Windows;
using H.NotifyIcon;
using Pulse1x.App.Localization;

namespace Pulse1x.App.Services;

public class TrayIconService : IDisposable
{
    private TaskbarIcon? _trayIcon;
    private System.Windows.Controls.MenuItem? _openItem;
    private System.Windows.Controls.MenuItem? _exitItem;
    private readonly Window _window;
    private readonly Action _onExitRequested;

    public TrayIconService(Window window, Action onExitRequested)
    {
        _window = window;
        _onExitRequested = onExitRequested;
    }

    public void Initialize()
    {
        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "Pulse1x",
            // O ícone da bandeja precisa ser um System.Drawing.Icon (GDI), carregado de um .ico
            // de verdade. Passar um PNG via IconSource faz o H.NotifyIcon tentar
            // `new Icon(streamPng)` e quebrar ("must be a picture that can be used as a Icon").
            Icon = LoadTrayIcon(),
            ContextMenu = BuildContextMenu()
        };

        _trayIcon.TrayLeftMouseUp += (_, _) => ShowWindow();
        Loc.Instance.LanguageChanged += RefreshTexts;
    }

    // Carrega o ícone da bandeja (.ico embutido como Resource). Qualquer falha cai num ícone do
    // sistema em vez de derrubar o app — o ícone nunca é motivo para o programa não abrir.
    private static System.Drawing.Icon LoadTrayIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Resources/pulse1x_tray.ico");
            var info = Application.GetResourceStream(uri);
            if (info?.Stream is { } stream)
            {
                using (stream)
                    return new System.Drawing.Icon(stream);
            }
        }
        catch { /* cai no ícone padrão abaixo */ }
        return System.Drawing.SystemIcons.Application;
    }

    private System.Windows.Controls.ContextMenu BuildContextMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();

        _openItem = new System.Windows.Controls.MenuItem { Header = Loc.S("Tray_Open") };
        _openItem.Click += (_, _) => ShowWindow();
        menu.Items.Add(_openItem);

        _exitItem = new System.Windows.Controls.MenuItem { Header = Loc.S("Tray_Exit") };
        _exitItem.Click += (_, _) => _onExitRequested();
        menu.Items.Add(_exitItem);

        return menu;
    }

    private void RefreshTexts()
    {
        if (_openItem != null) _openItem.Header = Loc.S("Tray_Open");
        if (_exitItem != null) _exitItem.Header = Loc.S("Tray_Exit");
    }

    public void MinimizeToTray()
    {
        _window.Hide();
    }

    private void ShowWindow()
    {
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    public void Dispose()
    {
        Loc.Instance.LanguageChanged -= RefreshTexts;
        _trayIcon?.Dispose();
    }
}
