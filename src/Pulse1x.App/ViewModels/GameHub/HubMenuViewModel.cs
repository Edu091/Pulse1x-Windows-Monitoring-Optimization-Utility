using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pulse1x.App.Localization;

namespace Pulse1x.App.ViewModels.GameHub;

/// <summary>Ações do menu lateral do GameHub.</summary>
public enum HubMenuAction
{
    GameProfile,
    AddGames,
    Statistics,
    Settings,
    ScanGames,
    FetchCovers,
    ExitHub,
    Sleep,
    Restart,
    Shutdown,
}

/// <summary>Uma entrada do menu lateral.</summary>
public partial class HubMenuItem : ObservableObject
{
    public HubMenuAction Action { get; }
    public string Icon { get; }
    public string Label { get; }
    /// <summary>Ações de energia ganham destaque visual diferente — são irreversíveis.</summary>
    public bool IsDestructive { get; }

    public HubMenuItem(HubMenuAction action, string icon, string labelKey, bool destructive = false)
    {
        Action = action;
        Icon = icon;
        Label = Loc.S(labelKey);
        IsDestructive = destructive;
    }
}

/// <summary>
/// Menu lateral do GameHub, aberto pelo botão View/Select — o mesmo gesto do Big Picture.
///
/// Reúne o que se quer alcançar sem sair do sofá: o perfil do jogo em destaque, adicionar jogos,
/// estatísticas, ajustes, sair do hub e as ações de energia da máquina.
/// </summary>
public partial class HubMenuViewModel : ObservableObject
{
    public ObservableCollection<HubMenuItem> Items { get; } = new();

    [ObservableProperty] private HubMenuItem? selectedItem;
    [ObservableProperty] private string subtitle = "";

    /// <summary>Ação escolhida; quem hospeda o menu decide o que fazer.</summary>
    public event Action<HubMenuAction>? ActionChosen;
    public event Action? CloseRequested;

    public HubMenuViewModel(string? selectedGameName)
    {
        subtitle = selectedGameName ?? "";

        Items.Add(new HubMenuItem(HubMenuAction.GameProfile, "", "GH_MenuGameProfile"));
        Items.Add(new HubMenuItem(HubMenuAction.AddGames, "", "GH_AddTitle"));
        Items.Add(new HubMenuItem(HubMenuAction.Statistics, "", "GH_MenuStatistics"));
        Items.Add(new HubMenuItem(HubMenuAction.ScanGames, "", "GH_Scan"));
        Items.Add(new HubMenuItem(HubMenuAction.FetchCovers, "", "GH_FetchCovers"));
        Items.Add(new HubMenuItem(HubMenuAction.Settings, "", "GH_MenuSettings"));
        Items.Add(new HubMenuItem(HubMenuAction.ExitHub, "", "GH_ExitHub"));
        Items.Add(new HubMenuItem(HubMenuAction.Sleep, "", "GH_MenuSleep", destructive: true));
        Items.Add(new HubMenuItem(HubMenuAction.Restart, "", "GH_MenuRestart", destructive: true));
        Items.Add(new HubMenuItem(HubMenuAction.Shutdown, "", "GH_MenuShutdown", destructive: true));
    }

    [RelayCommand]
    private void Choose(HubMenuItem? item)
    {
        if (item is null) return;

        // Desligar, reiniciar e suspender são irreversíveis do ponto de vista de quem está jogando:
        // sempre confirmam antes.
        if (item.IsDestructive && !Confirm(item)) return;

        ActionChosen?.Invoke(item.Action);
        CloseRequested?.Invoke();
    }

    private static bool Confirm(HubMenuItem item)
    {
        var result = System.Windows.MessageBox.Show(
            Loc.F("GH_MenuConfirm", item.Label),
            Loc.S("GH_Title"),
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);
        return result == System.Windows.MessageBoxResult.Yes;
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke();

    /// <summary>
    /// Executa as ações de energia da máquina. Usa o shutdown.exe do Windows, com os mesmos
    /// parâmetros do menu Iniciar — nada de API exótica.
    /// </summary>
    public static void RunPowerAction(HubMenuAction action)
    {
        try
        {
            switch (action)
            {
                case HubMenuAction.Shutdown:
                    Start("shutdown", "/s /t 0");
                    break;
                case HubMenuAction.Restart:
                    Start("shutdown", "/r /t 0");
                    break;
                case HubMenuAction.Sleep:
                    // O /h do rundll32 suspende quando a hibernação está desligada — é o caminho
                    // que o próprio Windows usa no menu de energia.
                    Start("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0");
                    break;
            }
        }
        catch { /* sem permissão ou política de grupo: a ação apenas não acontece */ }
    }

    private static void Start(string exe, string args) =>
        Process.Start(new ProcessStartInfo { FileName = exe, Arguments = args, UseShellExecute = false, CreateNoWindow = true });
}
