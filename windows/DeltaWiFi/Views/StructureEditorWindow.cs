//
//  StructureEditorWindow.cs
//  DeltaWiFi
//
//  Projekt verwalten: Etagen, Phasen und die Rate des Internetanschlusses.
//  Der Dialog arbeitet auf einer Kopie; „Fertig“ übernimmt alles in einem Schritt.
//

using System.Windows;
using System.Windows.Controls;
using DeltaWiFi.Model;

namespace DeltaWiFi.Views;

public sealed class StructureEditorWindow : DialogWindow
{
    private readonly Project working;
    private readonly StackPanel floorList = new();
    private readonly StackPanel phaseList = new();
    private readonly LineRateEditor lineRate;

    public StructureEditorWindow(Window owner, Project project) : base(owner, "Etagen, Phasen und Anschluss", 500, 720)
    {
        working = project.Clone();

        Body.Children.Add(Ui.Section("Etagen", out var floors));
        floors.Children.Add(floorList);
        floors.Children.Add(AddRowButton("＋  Etage hinzufügen", () => working.AddFloor("Neue Etage")));
        floors.Children.Add(Ui.Note("Jede Etage hat ihren eigenen Grundriss, ihre Messpunkte und Läufe.", ""));

        Body.Children.Add(Ui.Section("Phasen", out var phases));
        phases.Children.Add(phaseList);
        phases.Children.Add(AddRowButton("＋  Phase hinzufügen", () => working.AddPhase("Neue Phase")));
        phases.Children.Add(Ui.Note("Jede Phase ist ein eigener Messdurchgang, etwa vor und nach einem Umbau. Verglichen werden können " +
                                    "zwei beliebige Phasen.", ""));

        lineRate = new LineRateEditor(working);
        Body.Children.Add(lineRate.Section);

        FooterText.Text = "Änderungen lassen sich widerrufen (Strg+Z).";
        AddButton("Abbrechen", isCancel: true);
        AddButton("Fertig", isDefault: true).Click += (_, _) =>
        {
            lineRate.ApplyTo(working);
            DialogResult = true;
        };
        Rebuild();
    }

    /// Zeigt den Dialog; liefert das geänderte Projekt oder `null` bei „Abbrechen“.
    public Project? ShowAndGetResult() => ShowDialog() == true ? working : null;

    private Button AddRowButton(string title, Action add)
    {
        var button = new Button { Content = title, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 4) };
        button.Click += (_, _) => { add(); Rebuild(); };
        return button;
    }

    private void Rebuild()
    {
        floorList.Children.Clear();
        for (var index = 0; index < working.Floors.Count; index++)
        {
            var floor = working.Floors[index];
            floorList.Children.Add(Row(floor.Name, index, working.Floors.Count,
                                       name => working.RenameFloor(floor.Id, name),
                                       offset => working.MoveFloor(floor.Id, offset),
                                       () => RequestRemoval(floor)));
        }
        phaseList.Children.Clear();
        for (var index = 0; index < working.Phases.Count; index++)
        {
            var phase = working.Phases[index];
            phaseList.Children.Add(Row(phase.Name, index, working.Phases.Count,
                                       name => working.RenamePhase(phase.Id, name),
                                       offset => working.MovePhase(phase.Id, offset),
                                       () => RequestRemoval(phase)));
        }
    }

    private FrameworkElement Row(string name, int index, int count, Action<string> rename, Action<int> move, Action remove)
    {
        var box = new TextBox { Text = name };
        box.TextChanged += (_, _) => rename(box.Text);

        Button Small(string symbol, string tip, bool enabled, Action action)
        {
            var button = new Button
            {
                Content = symbol, Width = 32, Margin = new Thickness(4, 0, 0, 0), ToolTip = tip, IsEnabled = enabled, IsTabStop = false,
            };
            button.Click += (_, _) => { action(); Rebuild(); };
            return button;
        }

        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        // Mindestens eine Etage und eine Phase müssen bleiben.
        var buttons = new[]
        {
            Small("✕", "Löschen", count > 1, remove),
            Small("↓", "Nach unten", index < count - 1, () => move(1)),
            Small("↑", "Nach oben", index > 0, () => move(-1)),
        };
        foreach (var button in buttons)
        {
            DockPanel.SetDock(button, Dock.Right);
            row.Children.Add(button);
        }
        row.Children.Add(box);
        return row;
    }

    // MARK: - Löschen

    private void RequestRemoval(Floor floor)
    {
        var usage = working.FloorUsage(floor.Id);
        var parts = new List<string>();
        if (usage.HasPlan) parts.Add("der Grundriss");
        if (usage.Points > 0)
        {
            var measured = usage.MeasuredPoints > 0 ? $" ({usage.MeasuredPoints} davon mit Messungen in allen Phasen)" : "";
            parts.Add(Count(usage.Points, "Messpunkt", "Messpunkte") + measured);
        }
        if (usage.Walks > 0) parts.Add(Count(usage.Walks, "Lauf", "Läufe"));
        // Leere Einträge verschwinden ohne Rückfrage.
        if (usage.IsEmpty || Confirm(floor.Name, parts)) working.RemoveFloor(floor.Id);
    }

    private void RequestRemoval(Phase phase)
    {
        var usage = working.PhaseUsage(phase.Id);
        var parts = new List<string>();
        if (usage.MeasuredPoints > 0) parts.Add($"die Messungen an {Count(usage.MeasuredPoints, "Punkt", "Punkten")}");
        if (usage.Walks > 0) parts.Add(Count(usage.Walks, "Lauf", "Läufe"));
        if (usage.IsEmpty || Confirm(phase.Name, parts)) working.RemovePhase(phase.Id);
    }

    private bool Confirm(string name, List<string> parts) =>
        MessageBox.Show(this, $"Mit entfernt werden: {string.Join(", ", parts)}.", $"„{name}“ löschen?",
                        MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK;

    private static string Count(int number, string singular, string plural) => $"{number} {(number == 1 ? singular : plural)}";
}
