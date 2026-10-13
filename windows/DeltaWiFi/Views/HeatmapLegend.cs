//
//  HeatmapLegend.cs
//  DeltaWiFi
//
//  Farbskala zur Heatmap mit den Grenzwerten der gewählten Messgröße.
//

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DeltaWiFi.Logic;

namespace DeltaWiFi.Views;

public static class HeatmapLegend
{
    public static FrameworkElement Create(MetricScale scale, bool isComparison)
    {
        var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        for (var step = 0.0; step <= 1.0; step += 0.125)
        {
            var color = isComparison ? HeatColor.Difference(step * 2 - 1) : HeatColor.Scale(step);
            gradient.GradientStops.Add(new GradientStop(Ui.ToColor(color), step));
        }

        var panel = new StackPanel { Orientation = Orientation.Horizontal, ToolTip = Explanation(scale, isComparison) };
        panel.Children.Add(Ui.Text(isComparison ? "schlechter" : "kritisch"));
        panel.Children.Add(new Border
        {
            Width = 120, Height = 10, CornerRadius = new CornerRadius(5), Background = gradient,
            Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
        });
        panel.Children.Add(Ui.Text(isComparison ? "besser" : "gut"));
        var explanation = Ui.Text(Explanation(scale, isComparison), secondary: true);
        explanation.Margin = new Thickness(10, 0, 0, 0);
        panel.Children.Add(explanation);
        return panel;
    }

    private static string Explanation(MetricScale scale, bool isComparison)
    {
        if (isComparison)
        {
            var full = scale.Metric.Format(1.5 * Math.Abs(scale.Good - scale.Okay), withUnit: false);
            return $"volle Farbe ab ± {full} {scale.Metric.DifferenceUnit()}";
        }
        var note = scale.Reference == null ? "" : " (50 % / 80 % des Anschlusses)";
        return $"okay {scale.Metric.ThresholdText(scale.Okay)} · gut {scale.Metric.ThresholdText(scale.Good)}{note}";
    }
}
