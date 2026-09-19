using System.Windows.Controls;

namespace Pulse1x.App.Views;

/// <summary>
/// Espaço reservado da Personalização do Windows.
///
/// A implementação anterior aplicava efeitos de composição (transparência, blur, acrylic) à barra
/// de tarefas e às janelas do Explorer. Funcionava, mas o alcance real era pequeno demais para o
/// que a seção se propunha: Menu Iniciar, Configurações e as áreas internas do Explorer são
/// elementos XAML dentro de outros processos, sem janela própria, e só seriam alcançáveis
/// injetando uma DLL neles — com o risco de derrubar o Shell a cada atualização do Windows.
///
/// O código daquela versão está preservado no histórico do Git (branch
/// feat/windows-personalization), caso a seção seja retomada.
/// </summary>
public partial class WinCustomPage : Page
{
    public WinCustomPage()
    {
        InitializeComponent();
    }
}
