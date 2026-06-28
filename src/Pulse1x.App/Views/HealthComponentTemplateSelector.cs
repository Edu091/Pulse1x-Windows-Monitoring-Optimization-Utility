using System.Windows;
using System.Windows.Controls;
using Pulse1x.App.Models;

namespace Pulse1x.App.Views;

/// <summary>Escolhe o cartão "estilo CrystalDiskInfo" (com tabela de atributos SMART) para
/// discos, e o cartão genérico de métricas para os demais componentes (CPU, GPU, RAM, etc.).</summary>
public class HealthComponentTemplateSelector : DataTemplateSelector
{
    public DataTemplate? ComponentCard { get; set; }
    public DataTemplate? DiskHealthCard { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is ComponentHealth { HasSmartAttributes: true })
            return DiskHealthCard;
        return ComponentCard;
    }
}
