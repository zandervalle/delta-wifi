//
//  Dialogs.cs
//  DeltaWiFi
//
//  Gemeinsames der Dialoge: Grundgerüst mit Knopfleiste, die Eingabe eines Namens
//  und die Felder für die Rate des Internetanschlusses.
//

using System.Windows;
using System.Windows.Controls;
using DeltaWiFi.Logic;
using DeltaWiFi.Model;

namespace DeltaWiFi.Views;

/// Dialogfenster mit rollbarem Inhalt und einer Fußzeile für Hinweis und Knöpfe.
public abstract class DialogWindow : Window
{
    protected readonly StackPanel Body = new() { Margin = new Thickness(16) };
    protected readonly TextBlock FooterText = Ui.Text("", secondary: true);
    protected readonly StackPanel Buttons = new() { Orientation = Orientation.Horizontal };

    protected DialogWindow(Window owner, string title, double width, double height)
    {
        Owner = owner;
        Title = title;
        Width = width;
        Height = height;
        MinWidth = 380;
        MinHeight = 240;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var footer = new DockPanel { Margin = new Thickness(16, 10, 16, 12) };
        DockPanel.SetDock(Buttons, Dock.Right);
        footer.Children.Add(Buttons);
        footer.Children.Add(FooterText);

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        var divider = Ui.Divider();
        DockPanel.SetDock(divider, Dock.Bottom);
        root.Children.Add(divider);
        root.Children.Add(new ScrollViewer { Content = Body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
    }

    protected Button AddButton(string title, bool isDefault = false, bool isCancel = false)
    {
        var button = new Button
        {
            Content = title, Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(8, 0, 0, 0), MinWidth = 90,
            IsDefault = isDefault, IsCancel = isCancel,
        };
        Buttons.Children.Add(button);
        return button;
    }
}

/// Fragt einen kurzen Text ab, z. B. den Namen eines neuen Messpunkts.
public sealed class PromptWindow : DialogWindow
{
    private readonly TextBox input = new() { Margin = new Thickness(0, 10, 0, 0) };

    private PromptWindow(Window owner, string title, string message, string confirm) : base(owner, title, 440, 220)
    {
        ResizeMode = ResizeMode.NoResize;
        Body.Children.Add(Ui.Text(message, secondary: true));
        Body.Children.Add(input);
        AddButton("Abbrechen", isCancel: true);
        var ok = AddButton(confirm, isDefault: true);
        ok.IsEnabled = false;
        ok.Click += (_, _) => DialogResult = true;
        input.TextChanged += (_, _) => ok.IsEnabled = input.Text.Trim().Length > 0;
        Loaded += (_, _) => input.Focus();
    }

    /// Der eingegebene Text; `null`, wenn abgebrochen wurde.
    public static string? Ask(Window owner, string title, string message, string confirm)
    {
        var window = new PromptWindow(owner, title, message, confirm);
        return window.ShowDialog() == true ? window.input.Text : null;
    }
}

/// Optionale Eingabe der Anschlussrate; daran wird der Internet-Speedtest gemessen.
public sealed class LineRateEditor
{
    private readonly TextBox down = new() { Width = 110 };
    private readonly TextBox up = new() { Width = 110 };

    public Border Section { get; }

    public LineRateEditor(Project project)
    {
        Section = Ui.Section("Internetanschluss (optional)", out var content);
        content.Children.Add(Field("Download (Mbit/s)", down, project.LineDownMbit));
        content.Children.Add(Field("Upload (Mbit/s)", up, project.LineUpMbit));
        content.Children.Add(Ui.Note("Synchronisierte Rate in Mbit/s, zu finden in der Oberfläche des Routers. Ist der Download eingetragen, " +
                                     "gilt ein Speedtest ab 80 % davon als gut und ab 50 % als okay, und die Werte erscheinen zusätzlich als " +
                                     "Anteil am Anschluss. Ohne Eingabe gelten feste Grenzen (120 und 60 Mbit/s).", ""));
    }

    private static FrameworkElement Field(string label, TextBox box, double? value)
    {
        box.Text = value is { } number ? MetricInfo.Number(number).Replace(".", "") : "";
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(box, Dock.Right);
        row.Children.Add(box);
        row.Children.Add(Ui.Text(label));
        return row;
    }

    public void ApplyTo(Project project)
    {
        // Null, negative und unlesbare Eingaben gelten als „nicht eingetragen“.
        project.LineDownMbit = Flex.ParseNumber(down.Text) is > 0 and var downRate ? downRate : null;
        project.LineUpMbit = Flex.ParseNumber(up.Text) is > 0 and var upRate ? upRate : null;
    }
}
