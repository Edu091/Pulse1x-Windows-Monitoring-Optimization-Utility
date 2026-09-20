using System.Windows;
using System.Windows.Controls;
using Pulse1x.App.ViewModels.GameHub;
using Pulse1x.App.Services.GameHub;
using Pulse1x.App.Services;
using Wpf.Ui.Controls;

namespace Pulse1x.App.Views.GameHub;

/// <summary>Seção de estatísticas de uso: horas, ritmo, picos e FPS por jogo.</summary>
public partial class StatisticsWindow : FluentWindow
{
    public StatisticsWindow(StatisticsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested += Close;
        Loaded += (_, _) =>
        {
            Animations.OpenWindow(RootGrid);
            Dispatcher.BeginInvoke(() =>
            {
                FocusSelectedGameOrMaster();
            }, System.Windows.Threading.DispatcherPriority.Input);
        };
    }

    /// <summary>Moves the visible selector through every interactive statistics control.</summary>
    public bool MoveGamepadFocus(GamepadDirection direction)
    {
        if (GamesList.IsKeyboardFocusWithin)
            return MoveInGameList(direction);

        var collectionToggles = CollectionToggles();
        int collectionIndex = collectionToggles.FindIndex(c => c.IsKeyboardFocusWithin);
        if (collectionIndex >= 0)
        {
            return direction switch
            {
                GamepadDirection.Left when collectionIndex > 0 => collectionToggles[collectionIndex - 1].Focus(),
                GamepadDirection.Right when collectionIndex < collectionToggles.Count - 1 => collectionToggles[collectionIndex + 1].Focus(),
                GamepadDirection.Up => FocusSelectedGameOrMaster(),
                GamepadDirection.Down => ClearHistoryButton.Focus(),
                _ => true,
            };
        }

        if (MasterToggle.IsKeyboardFocusWithin)
        {
            return direction switch
            {
                GamepadDirection.Right => PlaytimeToggle.Focus(),
                GamepadDirection.Down or GamepadDirection.Left => FocusSelectedGameOrCollection(),
                _ => true,
            };
        }

        if (ClearHistoryButton.IsKeyboardFocusWithin || CloseButton.IsKeyboardFocusWithin)
        {
            return direction switch
            {
                GamepadDirection.Left => ClearHistoryButton.Focus(),
                GamepadDirection.Right => CloseButton.Focus(),
                GamepadDirection.Up => MemoryToggle.Focus(),
                _ => true,
            };
        }

        return FocusSelectedGameOrMaster();
    }

    private bool MoveInGameList(GamepadDirection direction)
    {
        int index = Math.Max(0, GamesList.SelectedIndex);
        return direction switch
        {
            GamepadDirection.Up when index > 0 => FocusGame(index - 1),
            GamepadDirection.Down when index < GamesList.Items.Count - 1 => FocusGame(index + 1),
            GamepadDirection.Up or GamepadDirection.Left => MasterToggle.Focus(),
            GamepadDirection.Down or GamepadDirection.Right => PlaytimeToggle.Focus(),
            _ => true,
        };
    }

    private bool FocusSelectedGameOrCollection() =>
        GamesList.Items.Count > 0 ? FocusGame(Math.Max(0, GamesList.SelectedIndex)) : PlaytimeToggle.Focus();

    private bool FocusSelectedGameOrMaster() =>
        GamesList.Items.Count > 0 ? FocusGame(Math.Max(0, GamesList.SelectedIndex)) : MasterToggle.Focus();

    private bool FocusGame(int index)
    {
        if (index < 0 || index >= GamesList.Items.Count) return false;

        GamesList.SelectedIndex = index;
        GamesList.ScrollIntoView(GamesList.SelectedItem);
        if (GamesList.ItemContainerGenerator.ContainerFromIndex(index) is ListBoxItem item)
            return item.Focus();

        Dispatcher.BeginInvoke(() =>
        {
            if (GamesList.ItemContainerGenerator.ContainerFromIndex(index) is ListBoxItem generated)
                generated.Focus();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
        return true;
    }

    private List<Control> CollectionToggles() =>
        [PlaytimeToggle, FpsToggle, TemperatureToggle, HardwareUsageToggle, MemoryToggle];
}
