using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Pulse1x.App.Localization;
using Pulse1x.App.Models.WinCustom;
using Pulse1x.App.ViewModels.WinCustom;

namespace Pulse1x.App.Views.WinCustom;

public partial class WinCustomPage : Page
{
    private readonly WinCustomViewModel _viewModel;

    public WinCustomPage(WinCustomViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;

        // Escolher arquivos é responsabilidade da janela, não da ViewModel.
        viewModel.PickImageRequested += () =>
        {
            var dialog = new OpenFileDialog
            {
                Title = Loc.S("WinCustom_PickImage"),
                Filter = Loc.S("WinCustom_ImageFilter"),
            };
            return dialog.ShowDialog(Window.GetWindow(this)) == true ? dialog.FileName : null;
        };

        // Confirmação com reversão automática: a personalização é aplicada, o usuário vê o
        // resultado e tem 5 segundos para mantê-la. Sem resposta, o Pulse desfaz sozinho.
        viewModel.ConfirmChangeRequested += themeName =>
            ConfirmChangeWindow.Ask(Window.GetWindow(this), themeName);

        // Desligar a ocultação automática reinicia o Explorer, então confirmamos antes.
        viewModel.ConfirmDisableAutoHideRequested += () =>
            MessageBox.Show(
                Window.GetWindow(this),
                Loc.S("WinCustom_AutoHideConfirm"),
                Loc.S("WinCustom_DisableAutoHide"),
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

        // Mostra as regiões do componente selecionado e mantém o preview em dia.
        ShowComponent(viewModel.SelectedComponent);
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WinCustomViewModel.SelectedComponent))
                ShowComponent(viewModel.SelectedComponent);
            else if (e.PropertyName == nameof(WinCustomViewModel.SelectedTheme))
                ShowComponent(viewModel.SelectedComponent);
        };
    }

    // =====================================================================================
    //  Componentes e regiões
    // =====================================================================================

    /// <summary>Liga a lista central às regiões do componente escolhido.</summary>
    private void ShowComponent(ComponentTab? tab)
    {
        RegionEditors.ItemsSource = tab?.Editors;
        AttachPreview(tab);
    }

    private void ComponentRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: ComponentTab tab })
            _viewModel.SelectedComponent = tab;
    }

    /// <summary>
    /// A escolha do tipo de aparência é tratada aqui porque o RadioButton precisa comparar o
    /// valor do item com a propriedade do editor — algo que um ConverterParameter estático não
    /// consegue expressar em XAML dentro de uma lista.
    /// </summary>
    private void KindRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: AppearanceKind kind } radio) return;
        if (radio.DataContext is not AppearanceOption) return;

        // O editor é o DataContext do Expander que contém a lista de opções.
        var editor = FindEditor(radio);
        if (editor is not null && editor.Kind != kind) editor.Kind = kind;
    }

    /// <summary>
    /// Marca o botão correspondente ao tipo em vigor. Feito no Loaded porque cada região tem sua
    /// própria lista de opções, e o agrupamento dos RadioButton dentro de um ItemsControl não é
    /// expressável só com bindings.
    /// </summary>
    private void KindRadio_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: AppearanceKind kind } radio) return;

        var editor = FindEditor(radio);
        if (editor is null) return;

        // Um nome de grupo por região mantém as listas independentes entre si.
        radio.GroupName = "Kind_" + editor.TargetId;
        radio.IsChecked = editor.Kind == kind;
    }

    private void GradientDirectionBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ComboBox box) return;
        var editor = FindEditor(box);
        if (editor is null) return;

        box.ItemsSource = Enum.GetValues<GradientDirection>()
            .Select(d => new EnumChoice<GradientDirection>(d, Loc.S("WinCustom_Gradient" + d)))
            .ToList();
        box.DisplayMemberPath = nameof(EnumChoice<GradientDirection>.Label);
        box.SelectedItem = ((IEnumerable<EnumChoice<GradientDirection>>)box.ItemsSource)
            .FirstOrDefault(c => c.Value == editor.GradientDirection);
    }

    private void ImageFitBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ComboBox box) return;
        var editor = FindEditor(box);
        if (editor is null) return;

        box.ItemsSource = Enum.GetValues<ImageFit>()
            .Select(f => new EnumChoice<ImageFit>(f, Loc.S("WinCustom_Fit_" + f)))
            .ToList();
        box.DisplayMemberPath = nameof(EnumChoice<ImageFit>.Label);
        box.SelectedItem = ((IEnumerable<EnumChoice<ImageFit>>)box.ItemsSource)
            .FirstOrDefault(c => c.Value == editor.ImageFit);
    }

    private void GradientDirection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: EnumChoice<GradientDirection> choice } box)
        {
            var editor = FindEditor(box);
            if (editor is not null) editor.GradientDirection = choice.Value;
        }
    }

    private void ImageFit_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: EnumChoice<ImageFit> choice } box)
        {
            var editor = FindEditor(box);
            if (editor is not null) editor.ImageFit = choice.Value;
        }
    }

    /// <summary>Sobe a árvore lógica até achar o editor daquela região.</summary>
    private static AppearanceEditorViewModel? FindEditor(FrameworkElement element)
    {
        DependencyObject? node = element;
        while (node is not null)
        {
            if (node is FrameworkElement { DataContext: AppearanceEditorViewModel editor })
                return editor;
            node = System.Windows.Media.VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node);
        }
        return null;
    }

    // =====================================================================================
    //  Preview
    // =====================================================================================

    private AppearanceEditorViewModel? _previewSource;

    /// <summary>
    /// O painel de preview acompanha a PRIMEIRA região do componente — a que representa o
    /// componente como um todo. Assim o usuário vê o efeito principal enquanto ajusta.
    /// </summary>
    private void AttachPreview(ComponentTab? tab)
    {
        if (_previewSource is not null)
            _previewSource.PropertyChanged -= OnPreviewSourceChanged;

        _previewSource = tab?.Editors.FirstOrDefault();

        if (_previewSource is not null)
            _previewSource.PropertyChanged += OnPreviewSourceChanged;

        RefreshPreview();
    }

    private void OnPreviewSourceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        => RefreshPreview();

    private void RefreshPreview()
    {
        if (_previewSource is null)
        {
            PreviewLayer.Background = null;
            PreviewDarkenLayer.Opacity = 0;
            PreviewLabel.Text = "";
            return;
        }

        PreviewLayer.Background = _previewSource.PreviewBrush;
        PreviewLayer.CornerRadius = _previewSource.PreviewCorner;
        PreviewDarkenLayer.CornerRadius = _previewSource.PreviewCorner;
        PreviewDarkenLayer.Opacity = _previewSource.PreviewDarken;

        // O desfoque do preview é aproximado: no Windows o blur lê o que está atrás da janela,
        // aqui ele desfoca o papel de parede simulado.
        double blur = _previewSource.PreviewBlur;
        PreviewLayer.Effect = blur > 0.5
            ? new System.Windows.Media.Effects.BlurEffect { Radius = blur, KernelType = System.Windows.Media.Effects.KernelType.Gaussian }
            : null;

        PreviewLabel.Text = _previewSource.Title;
    }

    // =====================================================================================
    //  Biblioteca de temas
    // =====================================================================================

    private void RenameTheme_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedTheme is not { IsBuiltIn: false } theme) return;

        string? name = PromptDialog.Show(
            Window.GetWindow(this),
            Loc.S("WinCustom_RenameTitle"),
            Loc.S("WinCustom_RenamePrompt"),
            theme.Name);

        if (!string.IsNullOrWhiteSpace(name)) _viewModel.RenameSelected(name);
    }

    private void DeleteTheme_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedTheme is not { IsBuiltIn: false } theme) return;

        var choice = MessageBox.Show(
            Window.GetWindow(this),
            Loc.F("WinCustom_DeleteConfirm", theme.Name),
            Loc.S("WinCustom_Delete"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (choice == MessageBoxResult.Yes) _viewModel.DeleteThemeCommand.Execute(null);
    }

    private void ImportTheme_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = Loc.S("WinCustom_Import"),
            Filter = Loc.S("WinCustom_ThemeFilter"),
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            _viewModel.ImportFrom(dialog.FileName);
    }

    private void ExportTheme_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedTheme is null) return;

        var dialog = new SaveFileDialog
        {
            Title = Loc.S("WinCustom_Export"),
            Filter = Loc.S("WinCustom_ThemeFilter"),
            FileName = _viewModel.SelectedTheme.Name + ".pulsetheme",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            _viewModel.ExportSelected(dialog.FileName);
    }

    // =====================================================================================
    //  Restauração total
    // =====================================================================================

    private void RestoreAll_Click(object sender, RoutedEventArgs e)
    {
        var choice = MessageBox.Show(
            Window.GetWindow(this),
            Loc.S("WinCustom_RestoreAllConfirm"),
            Loc.S("WinCustom_RestoreAll"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (choice == MessageBoxResult.Yes) _viewModel.RestoreEverything();
    }
}
