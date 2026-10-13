//
//  ProjectSetupWindow.cs
//  DeltaWiFi
//
//  Einrichtung eines neuen Projekts: fragt nach den Etagen bzw. Bereichen
//  und den Messpunkten darin.
//

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DeltaWiFi.Model;

namespace DeltaWiFi.Views;

public sealed class ProjectSetupWindow : DialogWindow
{
    private sealed class Area
    {
        public string Name = "";
        public List<PointEntry> Points = [new()];
    }

    private sealed class PointEntry
    {
        public string Name = "";
    }

    private readonly Project project;
    private readonly List<Area> areas = [new()];
    private readonly StackPanel areaList = new();
    private readonly LineRateEditor lineRate;
    private readonly Button createButton;
    private Project? result;
    /// Eintrag, dessen Textfeld nach dem nächsten Neuaufbau den Fokus bekommt.
    private object? focusTarget;

    public ProjectSetupWindow(Window owner, Project project) : base(owner, "Projekt einrichten", 560, 680)
    {
        this.project = project;
        Body.Children.Add(Ui.Text("Projekt einrichten", bold: true, size: 18));
        var intro = Ui.Text("Welche Etagen oder Bereiche sollen ausgemessen werden, und an welchen Stellen darin? Jeder Bereich bekommt " +
                            "später seinen eigenen Grundriss. Alles lässt sich nachträglich ändern und ergänzen.", secondary: true);
        intro.Margin = new Thickness(0, 4, 0, 14);
        Body.Children.Add(intro);
        Body.Children.Add(areaList);

        var addArea = new Button { Content = "＋  Weiteren Bereich hinzufügen", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 14) };
        addArea.Click += (_, _) =>
        {
            var area = new Area();
            areas.Add(area);
            focusTarget = area;
            Rebuild();
        };
        Body.Children.Add(addArea);
        lineRate = new LineRateEditor(project);
        Body.Children.Add(lineRate.Section);

        AddButton("Später", isCancel: true);
        createButton = AddButton("Projekt anlegen");
        createButton.Click += (_, _) =>
        {
            // In einem Schritt übernehmen, damit Rückgängig alles zusammen zurücknimmt.
            result = project.Clone();
            result.AddAreas(areas.Select(area => new SetupArea(area.Name, area.Points.Select(point => point.Name).ToList())));
            lineRate.ApplyTo(result);
            DialogResult = true;
        };

        focusTarget = areas[0];
        Rebuild();
    }

    /// Zeigt den Dialog; liefert das eingerichtete Projekt oder `null` bei „Später“.
    public Project? ShowAndGetResult() => ShowDialog() == true ? result : null;

    // MARK: - Eingabe

    private void Rebuild()
    {
        areaList.Children.Clear();
        TextBox? focus = null;
        foreach (var area in areas)
        {
            var section = Ui.Section("Etage / Bereich", out var content);
            var name = new TextBox { Text = area.Name, ToolTip = "Name, z. B. Erdgeschoss" };
            name.TextChanged += (_, _) => { area.Name = name.Text; Validate(); };
            name.KeyDown += (_, e) => { if (e.Key == Key.Enter) Focus(area.Points[0]); };
            if (ReferenceEquals(focusTarget, area)) focus = name;
            var nameCaption = Caption("Name, z. B. Erdgeschoss");
            nameCaption.Margin = new Thickness(0, 0, 0, 3);
            content.Children.Add(nameCaption);
            content.Children.Add(name);
            content.Children.Add(Caption("Messpunkte, z. B. Wohnzimmer Sofa"));

            foreach (var point in area.Points)
            {
                var box = new TextBox { Text = point.Name };
                box.TextChanged += (_, _) => { point.Name = box.Text; Validate(); };
                // Eingabetaste springt zum nächsten Punkt bzw. legt einen weiteren an.
                box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Advance(area, point); };
                if (ReferenceEquals(focusTarget, point)) focus = box;
                var remove = new Button
                {
                    Content = "−", Width = 30, Margin = new Thickness(6, 0, 0, 0), IsEnabled = area.Points.Count > 1,
                    ToolTip = "Messpunkt entfernen", IsTabStop = false,
                };
                remove.Click += (_, _) => { area.Points.Remove(point); Rebuild(); };
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
                DockPanel.SetDock(remove, Dock.Right);
                row.Children.Add(remove);
                row.Children.Add(box);
                content.Children.Add(row);
            }

            var buttons = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 2, 0, 0) };
            var addPoint = new Button { Content = "＋  Messpunkt hinzufügen", IsTabStop = false };
            addPoint.Click += (_, _) => AddPoint(area);
            buttons.Children.Add(addPoint);
            if (areas.Count > 1)
            {
                var removeArea = new Button { Content = "Bereich entfernen", IsTabStop = false };
                removeArea.Click += (_, _) => { areas.Remove(area); Rebuild(); };
                DockPanel.SetDock(removeArea, Dock.Right);
                buttons.Children.Add(removeArea);
            }
            content.Children.Add(buttons);
            areaList.Children.Add(section);
        }
        focusTarget = null;
        Validate();
        if (focus != null) Dispatcher.BeginInvoke(() => { focus.Focus(); focus.CaretIndex = focus.Text.Length; });
    }

    private static TextBlock Caption(string text)
    {
        var caption = Ui.Text(text, secondary: true, size: 11);
        caption.Margin = new Thickness(0, 8, 0, 3);
        return caption;
    }

    private void Focus(object target)
    {
        focusTarget = target;
        Rebuild();
    }

    private void AddPoint(Area area)
    {
        var point = new PointEntry();
        area.Points.Add(point);
        Focus(point);
    }

    private void Advance(Area area, PointEntry point)
    {
        var index = area.Points.IndexOf(point);
        if (index + 1 < area.Points.Count) Focus(area.Points[index + 1]);
        else if (point.Name.NormalizedPointName().Length > 0) AddPoint(area);
    }

    private void Validate()
    {
        var problem = Problem();
        FooterText.Text = problem == null ? "" : $"⚠  {problem}";
        createButton.IsEnabled = problem == null;
    }

    /// Was dem Anlegen noch im Weg steht; `null`, wenn alles passt.
    private string? Problem()
    {
        var named = areas.Select(area => area.Name.Trim()).Where(name => name.Length > 0).ToList();
        if (named.Count == 0) return "Mindestens ein Bereich braucht einen Namen.";
        if (areas.Any(area => area.Name.Trim().Length == 0 && area.Points.Any(point => point.Name.NormalizedPointName().Length > 0)))
        {
            return "Ein Bereich mit Messpunkten hat noch keinen Namen.";
        }
        if (named.Distinct().Count() != named.Count) return "Bereichsnamen müssen eindeutig sein.";

        var seen = project.Points.Select(point => point.Name).ToHashSet();
        foreach (var name in areas.SelectMany(area => area.Points).Select(point => point.Name.NormalizedPointName()).Where(name => name.Length > 0))
        {
            if (!seen.Add(name)) return $"Der Messpunkt „{name}“ kommt mehrfach vor. Namen müssen eindeutig sein.";
        }
        return null;
    }
}
