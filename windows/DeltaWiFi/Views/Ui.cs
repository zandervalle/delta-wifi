//
//  Ui.cs
//  DeltaWiFi
//
//  Kleine Bausteine der Oberfläche: Texte, Zeilen „Bezeichnung – Wert“, Abschnitte, Statuspunkte.
//  Die Farben kommen ohne feste Hell-/Dunkelwerte aus, damit beide Modi lesbar bleiben.
//

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DeltaWiFi.Logic;

namespace DeltaWiFi.Views;

public static class Ui
{
    /// Deckkraft für Nebentexte.
    public const double SecondaryOpacity = 0.65;

    /// Zurückhaltende Fläche für Karten und Abschnitte, hell wie dunkel passend.
    public static readonly Brush CardBrush = Frozen(Color.FromArgb(0x18, 0x80, 0x80, 0x80));
    public static readonly Brush DividerBrush = Frozen(Color.FromArgb(0x50, 0x80, 0x80, 0x80));
    public static readonly Brush SelectionBrush = Frozen(Color.FromArgb(0x40, SystemColors.AccentColor.R, SystemColors.AccentColor.G, SystemColors.AccentColor.B));

    public static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public static Color ToColor(HeatColor color) =>
        System.Windows.Media.Color.FromRgb((byte)Math.Round(color.Red * 255), (byte)Math.Round(color.Green * 255), (byte)Math.Round(color.Blue * 255));

    public static SolidColorBrush Brush(HeatColor color) => Frozen(ToColor(color));

    public static TextBlock Text(string text, bool secondary = false, bool bold = false, double? size = null)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        if (secondary) block.Opacity = SecondaryOpacity;
        if (bold) block.FontWeight = FontWeights.SemiBold;
        if (size is { } points) block.FontSize = points;
        return block;
    }

    /// Kleiner Statuspunkt in einer der Signalfarben.
    public static Ellipse Dot(HeatColor color) => new()
    {
        Width = 9, Height = 9, Fill = Brush(color), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 6, 0),
    };

    /// Zeile mit Bezeichnung links und Wert rechts; `dot` setzt einen Statuspunkt hinter den Wert.
    public static FrameworkElement Row(string label, string value, HeatColor? dot = null)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(Text(label, secondary: true));

        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var text = Text(value);
        text.TextAlignment = TextAlignment.Right;
        text.Margin = new Thickness(12, 0, 0, 0);
        right.Children.Add(text);
        if (dot is { } color)
        {
            var mark = Dot(color);
            mark.Margin = new Thickness(6, 1, 0, 0);
            right.Children.Add(mark);
        }
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    /// Hinweistext mit vorangestelltem Symbol.
    public static FrameworkElement Note(string text, string symbol = "ⓘ")
    {
        var block = Text(symbol.Length == 0 ? text : $"{symbol}  {text}", secondary: true);
        block.Margin = new Thickness(0, 3, 0, 3);
        return block;
    }

    public static StackPanel Stack(params UIElement[] children)
    {
        var panel = new StackPanel();
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }

    /// Abschnitt mit Überschrift auf einer zurückhaltenden Karte. Liefert die Karte; der Inhalt landet in `content`.
    public static Border Section(string title, out StackPanel content, out TextBlock header)
    {
        header = Text(title, bold: true);
        header.Margin = new Thickness(0, 0, 0, 6);
        content = new StackPanel();
        return new Border
        {
            Background = CardBrush, CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 10), Child = Stack(header, content),
        };
    }

    public static Border Section(string title, out StackPanel content) => Section(title, out content, out _);

    public static Border Divider(bool vertical = false) => vertical
        ? new Border { Width = 1, Background = DividerBrush }
        : new Border { Height = 1, Background = DividerBrush };

    /// Füllt eine Auswahlliste nur dann neu, wenn sich die Einträge geändert haben, und setzt die Auswahl.
    public static void SetItems(ComboBox box, IReadOnlyList<(string Id, string Name)> items, string selected)
    {
        var current = box.Items.Cast<ComboBoxItem>().Select(item => ((string)item.Tag, (string)item.Content)).ToList();
        if (!current.SequenceEqual(items))
        {
            box.Items.Clear();
            foreach (var (id, name) in items) box.Items.Add(new ComboBoxItem { Tag = id, Content = name });
        }
        box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == selected);
    }

    public static string? SelectedId(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;
}
