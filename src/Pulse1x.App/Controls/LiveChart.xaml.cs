using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LiveChartsCore.Defaults;

namespace Pulse1x.App.Controls;

public partial class LiveChart : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(LiveChart));

    public static readonly DependencyProperty ValuesProperty =
        DependencyProperty.Register(nameof(Values), typeof(ObservableCollection<DateTimePoint>), typeof(LiveChart),
            new PropertyMetadata(null, OnValuesChanged));

    public static readonly DependencyProperty AccentColorProperty =
        DependencyProperty.Register(nameof(AccentColor), typeof(Color), typeof(LiveChart),
            new PropertyMetadata(Color.FromRgb(0xDC, 0x26, 0x26), OnAccentColorChanged));

    public static readonly DependencyProperty MaxYValueProperty =
        DependencyProperty.Register(nameof(MaxYValue), typeof(double?), typeof(LiveChart), new PropertyMetadata(null));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public ObservableCollection<DateTimePoint> Values
    {
        get => (ObservableCollection<DateTimePoint>)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Color AccentColor
    {
        get => (Color)GetValue(AccentColorProperty);
        set => SetValue(AccentColorProperty, value);
    }

    public double? MaxYValue
    {
        get => (double?)GetValue(MaxYValueProperty);
        set => SetValue(MaxYValueProperty, value);
    }

    public LiveChart()
    {
        InitializeComponent();
        ApplyAccentColor();
    }

    private static void OnValuesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var chart = (LiveChart)d;

        if (e.OldValue is ObservableCollection<DateTimePoint> oldCollection)
            oldCollection.CollectionChanged -= chart.OnCollectionChanged;

        if (e.NewValue is ObservableCollection<DateTimePoint> newCollection)
            newCollection.CollectionChanged += chart.OnCollectionChanged;

        chart.Redraw();
    }

    private static void OnAccentColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((LiveChart)d).ApplyAccentColor();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Redraw();

    private void ChartArea_SizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

    private void ApplyAccentColor()
    {
        var strokeBrush = new SolidColorBrush(AccentColor);
        var fillBrush = new SolidColorBrush(AccentColor) { Opacity = 0.18 };
        LineShape.Stroke = strokeBrush;
        FillArea.Fill = fillBrush;
    }

    // Eixo Y com escala automática suavizada: sobe imediatamente para mostrar picos,
    // mas desce lentamente para não "achatar" oscilações recentes nem ficar instável.
    private double? _displayMaxY;

    private void Redraw()
    {
        var points = Values;
        double width = ChartArea.ActualWidth;
        double height = ChartArea.ActualHeight;

        if (points == null || points.Count < 2 || width <= 0 || height <= 0)
        {
            LineShape.Points = null;
            FillArea.Points = null;
            EmptyHint.Visibility = Visibility.Visible;
            return;
        }

        EmptyHint.Visibility = Visibility.Collapsed;

        double minX = points[0].DateTime.Ticks;
        double maxX = points[^1].DateTime.Ticks;
        if (maxX <= minX) maxX = minX + 1;

        double minY = 0;
        double maxY;

        if (MaxYValue is { } fixedMax)
        {
            maxY = fixedMax;
        }
        else
        {
            var actualMax = points.Max(p => p.Value ?? 0);
            var desiredMax = Math.Max(actualMax * 1.25, 5);

            if (_displayMaxY is not { } currentMax)
            {
                _displayMaxY = desiredMax;
            }
            else if (desiredMax > currentMax)
            {
                _displayMaxY = desiredMax;
            }
            else
            {
                _displayMaxY = currentMax + (desiredMax - currentMax) * 0.08;
            }

            maxY = _displayMaxY.Value;
        }

        var linePoints = new PointCollection();
        foreach (var point in points)
        {
            double x = (point.DateTime.Ticks - minX) / (maxX - minX) * width;
            double y = height - ((point.Value ?? 0) - minY) / (maxY - minY) * height;
            linePoints.Add(new Point(x, y));
        }

        var fillPoints = new PointCollection(linePoints);
        fillPoints.Add(new Point(width, height));
        fillPoints.Add(new Point(0, height));

        LineShape.Points = linePoints;
        FillArea.Points = fillPoints;
    }
}
