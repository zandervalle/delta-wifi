//
//  AnalysisView.cs
//  DeltaWiFi
//
//  Auswertung: Übersicht je Phase, Tabelle aller Messpunkte und Detailansicht
//  des ausgewählten Punkts mit allen Werten je Phase.
//

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DeltaWiFi.Logic;
using DeltaWiFi.Model;

namespace DeltaWiFi.Views;

public sealed class AnalysisView : Grid
{
    private readonly MainWindow host;
    private readonly StackPanel cards = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(12) };
    private readonly ScrollViewer tableScroll = new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
    };
    private readonly ScrollViewer detailScroll = new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        Padding = new Thickness(12),
    };

    private Project Project => host.Project;

    public AnalysisView(MainWindow host)
    {
        this.host = host;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 120 });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(250) });

        var cardScroll = new ScrollViewer
        {
            Content = cards, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        Place(cardScroll, 0);
        Place(Ui.Divider(), 1);
        Place(tableScroll, 2);
        Place(Ui.Divider(), 3);
        Place(detailScroll, 4);
    }

    private void Place(UIElement element, int row)
    {
        SetRow(element, row);
        Children.Add(element);
    }

    public void Update(Metric metric, string compareFrom, string compareTo)
    {
        var scale = Project.Scale(metric);

        cards.Children.Clear();
        foreach (var phase in Project.Phases) cards.Children.Add(PhaseCard(phase, metric, scale));
        cards.Children.Add(ChangeCard(metric, compareFrom, compareTo));

        tableScroll.Content = Table(metric, scale, compareFrom, compareTo);
        detailScroll.Content = Project.Point(host.Selection) is { } point
            ? PointDetail(point)
            : Centered("Punkt in der Tabelle auswählen, um alle Werte je Phase und die Notizen zu sehen.");
    }

    private static TextBlock Centered(string text)
    {
        var block = Ui.Text(text, secondary: true);
        block.HorizontalAlignment = HorizontalAlignment.Center;
        block.Margin = new Thickness(0, 90, 0, 0);
        return block;
    }

    // MARK: - Übersicht

    private Border PhaseCard(Phase phase, Metric metric, MetricScale scale)
    {
        var summary = Project.PhaseSummary(metric, phase.Id);
        var content = new List<UIElement>();
        if (summary.Median is { } median)
        {
            content.Add(Line(Ui.Text($"Median {scale.Describe(median)}", bold: true, size: 16), Dot(HeatColor.Scale(scale.Score(median)))));
            content.Add(Line(Count(summary.Good, "gut", HeatColor.Good), Count(summary.Okay, "okay", HeatColor.Okay),
                             Count(summary.Critical, "kritisch", HeatColor.Critical)));
            content.Add(Ui.Text($"{summary.Count} von {Project.Points.Count} Punkten gemessen", secondary: true));
            // Liegt schon der Median am Anschlussmaximum, unterscheidet der Speedtest gute Punkte nicht mehr.
            if (scale.Reference is { } line && median >= 0.9 * line)
            {
                var hint = Ui.Text("Nahe am Maximum des Anschlusses. Der interne Durchsatz sagt hier mehr über das WLAN.", secondary: true);
                hint.MaxWidth = 230;
                content.Add(hint);
            }
        }
        else
        {
            content.Add(Ui.Text("Keine Werte", secondary: true, size: 16));
        }
        return Card(phase.Name, content);
    }

    private Border ChangeCard(Metric metric, string from, string to)
    {
        var summary = Project.ChangeSummary(metric, from, to);
        var content = new List<UIElement>();
        if (summary.Compared > 0)
        {
            content.Add(Line(Count(summary.Better, "besser", HeatColor.Good, 16), Count(summary.Worse, "schlechter", HeatColor.Critical, 16)));
            content.Add(Ui.Text($"{summary.Unchanged} unverändert"));
            content.Add(Ui.Text($"{summary.Compared} Punkte in beiden Phasen gemessen", secondary: true));
        }
        else
        {
            content.Add(Ui.Text("Kein Punkt in beiden Phasen gemessen", secondary: true));
        }
        return Card($"{Project.PhaseName(from)} → {Project.PhaseName(to)}", content);
    }

    private static Border Card(string title, IEnumerable<UIElement> content)
    {
        var panel = Ui.Stack(Ui.Text(title, bold: true, size: 14));
        foreach (var element in content)
        {
            if (element is FrameworkElement framework) framework.Margin = new Thickness(0, 4, 0, 0);
            panel.Children.Add(element);
        }
        return new Border
        {
            Background = Ui.CardBrush, CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 12, 0), MinWidth = 190, Child = panel,
        };
    }

    private static StackPanel Line(params UIElement[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }

    private static FrameworkElement Dot(HeatColor color)
    {
        var dot = Ui.Dot(color);
        dot.Margin = new Thickness(6, 1, 0, 0);
        return dot;
    }

    private static StackPanel Count(int number, string label, HeatColor color, double? size = null)
    {
        var text = Ui.Text($"{number} {label}", bold: size != null, size: size);
        text.Margin = new Thickness(0, 0, 12, 0);
        return Line(Ui.Dot(color), text);
    }

    // MARK: - Tabelle

    private Grid Table(Metric metric, MetricScale scale, string compareFrom, string compareTo)
    {
        var table = new Grid { Margin = new Thickness(12, 6, 12, 6) };
        var titles = new List<string> { "Messpunkt", "Etage" };
        titles.AddRange(Project.Phases.Select(phase => phase.Name));
        titles.Add("Veränderung");
        foreach (var _ in titles) table.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 90 });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        void Cell(UIElement element, int row, int column)
        {
            if (element is FrameworkElement framework) framework.Margin = new Thickness(8, 5, 16, 5);
            SetRow(element, row);
            SetColumn(element, column);
            table.Children.Add(element);
        }

        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var column = 0; column < titles.Count; column++) Cell(Ui.Text(titles[column], secondary: true, bold: true), 0, column);

        var row = 1;
        foreach (var point in Project.Points)
        {
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            // Die Zeile als Ganzes ist anklickbar; die Auswahl ist farbig hinterlegt.
            var background = new Border
            {
                Background = point.Name == host.Selection ? Ui.SelectionBrush : Brushes.Transparent, CornerRadius = new CornerRadius(4),
                BorderBrush = Ui.DividerBrush, BorderThickness = new Thickness(0, 1, 0, 0), Cursor = Cursors.Hand,
            };
            background.MouseLeftButtonDown += (_, _) => host.Select(point.Name);
            SetRow(background, row);
            SetColumnSpan(background, titles.Count + 1);
            table.Children.Add(background);

            Cell(Passive(Ui.Text(point.Name)), row, 0);
            Cell(Passive(Ui.Text(Project.FloorName(point.Floor))), row, 1);
            for (var index = 0; index < Project.Phases.Count; index++)
            {
                var value = Project.Value(metric, point.Name, Project.Phases[index].Id);
                Cell(Passive<UIElement>(value is { } number ? Line(Ui.Dot(HeatColor.Scale(scale.Score(number))), Ui.Text(scale.Describe(number))) : Dash()),
                     row, 2 + index);
            }
            Cell(Passive(ChangeCell(metric, scale, Project.Value(metric, point.Name, compareFrom), Project.Value(metric, point.Name, compareTo))),
                 row, titles.Count - 1);
            row++;
        }
        return table;
    }

    /// Lässt Klicks zur Zeile dahinter durch.
    private static T Passive<T>(T element) where T : UIElement
    {
        element.IsHitTestVisible = false;
        return element;
    }

    private static TextBlock Dash() => Ui.Text("–", secondary: true);

    private static UIElement ChangeCell(Metric metric, MetricScale scale, double? before, double? after)
    {
        if (before is not { } from || after is not { } to) return Dash();
        var trend = scale.Trend(from, to);
        var difference = Ui.Text(metric.FormatDifference(to - from));
        difference.Margin = new Thickness(0, 0, 8, 0);
        return Line(Ui.Dot(trend == Trend.Better ? HeatColor.Good : trend == Trend.Worse ? HeatColor.Critical : HeatColor.Unchanged),
                    difference,
                    Ui.Text(trend == Trend.Better ? "besser" : trend == Trend.Worse ? "schlechter" : "unverändert", secondary: true));
    }

    // MARK: - Detail

    /// Eine Zeile der Detailansicht: Text je Messung, bei bewerteten Messgrößen mit Statuspunkt.
    private sealed record Field(string Title, Func<Measurement, string?> Text, Metric? Metric = null)
    {
        public Field(Metric metric) : this(metric.Title(), measurement => metric.Value(measurement) is { } value ? metric.Format(value) : null, metric) { }
    }

    private const string UploadTitle = "Internet Upload";

    private static string? Number(double? value, string unit) => value is { } number ? $"{MetricInfo.Number(number)} {unit}" : null;

    private static readonly Field[] Fields =
    [
        new("Zeit", m => m.Zeit),
        new("Wiederholung", m => m.Wiederholung?.ToString()),
        new(Metric.Rssi),
        new("Rauschen", m => Number(m.NoiseDBm, "dBm")),
        new(Metric.Snr),
        new(Metric.LinkRate),
        new("Band", m => m.BandGHz == null ? null : $"{m.BandGHz.Replace('.', ',')} GHz"),
        new("Kanal", m => m.Kanal == null ? null : $"{m.Kanal}" + (m.BreiteMHz == null ? "" : $" ({m.BreiteMHz} MHz)")),
        new("PHY", m => m.Phy),
        new("Netz", m => m.Ssid),
        new("Knoten (BSSID)", m => m.Bssid),
        new(Metric.GatewayLoss),
        new("Ping zur Box (Mittel)", m => Number(m.PingGwAvgMs, "ms")),
        new(Metric.GatewayMaxPing),
        new("Paketverlust Internet", m => Number(m.PingInetVerlustPct, "%")),
        new("Ping Internet (Mittel)", m => Number(m.PingInetAvgMs, "ms")),
        new("Max. Ping Internet", m => Number(m.PingInetMaxMs, "ms")),
        new(Metric.InternetDown),
        new(UploadTitle, m => Number(m.InetUpMbit, "Mbit/s")),
        new(Metric.LanDown),
        new("Intern Upload (iperf3)", m => Number(m.LanUpMbit, "Mbit/s")),
        new("Notiz", m => m.Notiz),
    ];

    /// Alle Werte eines Punkts, die Phasen nebeneinander.
    private StackPanel PointDetail(MeasurePoint point)
    {
        var measurements = Project.Phases.Select(phase => Project.Measurement(point.Name, phase.Id)).ToList();
        var panel = Ui.Stack(Ui.Text($"{point.Name} · {Project.FloorName(point.Floor)}", bold: true, size: 14));
        if (measurements.All(measurement => measurement == null))
        {
            panel.Children.Add(Ui.Text("An diesem Punkt wurde noch nicht gemessen.", secondary: true));
            return panel;
        }

        var grid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        for (var column = 0; column <= measurements.Count; column++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        void Cell(FrameworkElement element, int row, int column)
        {
            element.Margin = new Thickness(0, 2, 24, 2);
            SetRow(element, row);
            SetColumn(element, column);
            grid.Children.Add(element);
        }

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var index = 0; index < Project.Phases.Count; index++) Cell(Ui.Text(Project.Phases[index].Name, bold: true), 0, index + 1);

        var rowIndex = 1;
        // Zeilen, die in keiner Phase einen Wert haben, entfallen.
        foreach (var field in Fields.Where(field => measurements.Any(measurement => measurement != null && field.Text(measurement) != null)))
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Cell(Ui.Text(field.Title, secondary: true), rowIndex, 0);
            for (var index = 0; index < measurements.Count; index++)
            {
                Cell(DetailCell(field, measurements[index]), rowIndex, index + 1);
            }
            rowIndex++;
        }
        panel.Children.Add(grid);
        return panel;
    }

    private FrameworkElement DetailCell(Field field, Measurement? measurement)
    {
        if (measurement == null || field.Text(measurement) is not { } text) return Dash();
        // Anteil an der Anschlussrate für die Internet-Werte, sofern sie eingetragen ist.
        var share = field.Metric == Metric.InternetDown ? MetricScale.ShareText(measurement.InetDownMbit, Project.LineDownMbit)
            : field.Title == UploadTitle ? MetricScale.ShareText(measurement.InetUpMbit, Project.LineUpMbit) : null;
        var value = Ui.Text(text + (share ?? ""));
        value.MaxWidth = 260;
        if (field.Metric is not { } metric || metric.Value(measurement) is not { } number) return value;
        return Line(Ui.Dot(HeatColor.Scale(Project.Scale(metric).Score(number))), value);
    }
}
