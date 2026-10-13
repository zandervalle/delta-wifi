//
//  MainWindow.xaml.cs
//  DeltaWiFi
//
//  Hauptfenster: Punktliste, Karte mit Heatmap bzw. Auswertung, Messpanel.
//  Nach jeder Änderung bringt `Refresh` die ganze Anzeige auf den Stand des Projekts.
//

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeltaWiFi.Logic;
using DeltaWiFi.Model;
using DeltaWiFi.Services;
using Microsoft.Win32;

namespace DeltaWiFi.Views;

public partial class MainWindow : Window
{
    private const string AppName = "DELTA Wi-Fi Analyzer";
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".heic", ".heif", ".bmp", ".tif", ".tiff"];

    private readonly ProjectDocument document = new();
    public Settings Settings { get; } = Settings.Load();
    public MeasurementRunner Runner { get; } = new();
    public WalkRecorder Recorder { get; } = new();

    // Gewählte Etage und Phasen als IDs. Verschwindet eine davon aus dem Projekt, greifen die
    // berechneten Eigenschaften unten auf einen vorhandenen Eintrag zurück.
    private string floorSelection = "eg";
    private string phaseSelection = "VORHER";
    private bool isComparison;
    private string? compareFromSelection;
    private string? compareToSelection;
    private bool showAnalysis;
    private Metric metric = Metric.Rssi;

    private readonly MeasurePanel measurePanel;
    private readonly WalkPanel walkPanel;
    private readonly AnalysisView analysis;

    /// Solange `Refresh` die Bedienelemente setzt, lösen deren Ereignisse nichts aus.
    private bool refreshing;
    private bool wasMeasuring;
    private bool wasWalking;
    private List<string> sidebarContent = [];
    /// Dekodierte Grundrisse je Etage, damit das Base64 nicht bei jedem Neuzeichnen gelesen wird.
    private readonly Dictionary<string, (Plan Plan, BitmapSource? Image)> planImages = [];
    private HeatmapInput? heatmapInput;
    private int heatmapVersion;

    public Project Project => document.Project;
    public string Floor => Project.ResolvedFloorID(floorSelection);
    /// Angezeigte Phase; in sie wird auch gemessen und gelaufen.
    public string Phase => Project.ResolvedPhaseID(phaseSelection);
    /// Vergleich: von der ersten zur letzten Phase, solange nichts anderes gewählt ist.
    private string CompareFrom => Project.ResolvedPhaseID(compareFromSelection);
    private string CompareTo => Project.ResolvedPhaseID(compareToSelection, Project.Phases.LastOrDefault()?.Id);
    /// Name des ausgewählten Messpunkts.
    public string? Selection { get; private set; }

    private List<MeasurePoint> FloorPoints => Project.Points.Where(point => point.Floor == Floor).ToList();
    /// Ausgewählter Punkt dieser Etage ohne Position: Der nächste Klick auf den Plan setzt ihn.
    private MeasurePoint? PendingPoint => FloorPoints.FirstOrDefault(point => point.Name == Selection && !point.IsPlaced);
    private bool IsBusy => Runner.IsRunning || Recorder.IsActive;

    public MainWindow(string? path)
    {
        InitializeComponent();
        measurePanel = new MeasurePanel(this);
        walkPanel = new WalkPanel(this);
        analysis = new AnalysisView(this);
        AnalysisHost.Content = analysis;
        foreach (var item in MetricInfo.All) MetricPicker.Items.Add(item.Title());

        PlanCanvas.PositionSet += SetPosition;
        PlanCanvas.WalkTapped += AddWalkTap;
        PlanCanvas.SelectionChanged += Select;
        Runner.Changed += OnRunnerChanged;
        Recorder.Changed += OnRecorderChanged;

        refreshing = true;
        PointsTab.IsChecked = true;
        refreshing = false;

        if (path != null) Load(path);
        Refresh();
        // Ein neues Projekt fragt zuerst nach Bereichen und Messpunkten.
        Loaded += (_, _) => Dispatcher.BeginInvoke(() => { if (Project.Floors.Count == 0) ShowSetup(); });
    }

    // MARK: - Zustand ändern

    /// Ändert das Projekt (widerrufbar) und aktualisiert die Anzeige.
    public void Edit(Action<Project> change)
    {
        document.Edit(change);
        Refresh();
    }

    /// Wählt einen Messpunkt aus; liegt er auf einer anderen Etage, wechselt die Anzeige dorthin.
    public void Select(string? name)
    {
        Selection = name;
        if (Project.Point(name) is { } point && point.Floor != Floor) floorSelection = point.Floor;
        Refresh();
    }

    /// Phase für Anzeige und Messung; eine Auswahl führt aus dem Vergleich zurück zur einzelnen Phase.
    public void SetPhase(string id)
    {
        phaseSelection = id;
        isComparison = false;
        Refresh();
    }

    private void SetPosition(string name, double x, double y)
    {
        var wasPending = PendingPoint?.Name == name;
        Edit(project => project.SetPosition(name, x, y));
        // Nach dem Setzen gleich den nächsten offenen Punkt der Etage anbieten.
        if (wasPending && FloorPoints.FirstOrDefault(point => !point.IsPlaced) is { } next) Select(next.Name);
    }

    private void AddWalkTap(double x, double y)
    {
        Recorder.AddTap(x, y);
        Edit(Recorder.Commit);
    }

    private void OnRunnerChanged()
    {
        if (Runner.IsRunning == wasMeasuring)
        {
            measurePanel.Update();
            return;
        }
        wasMeasuring = Runner.IsRunning;
        Refresh();
    }

    private void OnRecorderChanged()
    {
        if (Recorder.IsActive == wasWalking)
        {
            walkPanel.Update();
            return;
        }
        wasWalking = Recorder.IsActive;
        // Während eines Laufs zeigt die Karte die Phase, in der gelaufen wird.
        if (Recorder.IsActive)
        {
            isComparison = false;
            showAnalysis = false;
        }
        Refresh();
    }

    // MARK: - Anzeige

    public void Refresh()
    {
        refreshing = true;
        try
        {
            var project = Project;
            // Die Auswahl gilt nur, solange es den Punkt gibt und er auf der angezeigten Etage liegt.
            if (Selection != null && project.Point(Selection)?.Floor != Floor) Selection = null;

            Title = $"{(document.IsDirty ? "● " : "")}{document.DisplayName} – {AppName}";
            var walking = Recorder.IsActive;
            var plan = project.Plans.GetValueOrDefault(Floor);

            // Werkzeugleiste und Menü
            Ui.SetItems(FloorPicker, project.Floors.Select(floor => (floor.Id, floor.Name)).ToList(), Floor);
            FloorPicker.IsEnabled = !walking;
            MapButton.IsChecked = !showAnalysis;
            AnalysisButton.IsChecked = showAnalysis;
            MapButton.IsEnabled = AnalysisButton.IsEnabled = !walking;
            MeasureToggle.IsChecked = SidePanel.Visibility == Visibility.Visible;
            StructureItem.IsEnabled = SetupItem.IsEnabled = !IsBusy;
            LoadPlanItem.Header = plan == null ? "_Grundriss laden …" : "_Grundriss ersetzen …";
            LoadPlanItem.IsEnabled = project.Floors.Count > 0;
            RemovePlanItem.IsEnabled = plan != null;

            // Ansicht, Phasen und Messgröße. Die Auswertung zeigt immer alle Phasen; dort wird nur das Vergleichspaar gewählt.
            var phases = project.Phases.Select(phase => (phase.Id, phase.Name)).ToList();
            var pair = isComparison || showAnalysis;
            ViewButtons.Visibility = Visible(!showAnalysis);
            ComparisonLabel.Visibility = Visible(showAnalysis);
            PhaseViewButton.IsChecked = !isComparison;
            ComparisonViewButton.IsChecked = isComparison;
            PhasePicker.Visibility = Visible(!pair);
            CompareFromPicker.Visibility = CompareArrow.Visibility = CompareToPicker.Visibility = Visible(pair);
            Ui.SetItems(PhasePicker, phases, Phase);
            Ui.SetItems(CompareFromPicker, phases, CompareFrom);
            Ui.SetItems(CompareToPicker, phases, CompareTo);
            ViewButtons.IsEnabled = PhasePicker.IsEnabled = CompareFromPicker.IsEnabled = CompareToPicker.IsEnabled = !walking;
            MetricPicker.SelectedIndex = (int)metric;

            UpdateSidebar();
            var isEmpty = project.Floors.Count == 0;
            EmptyOverlay.Visibility = Visible(isEmpty);
            Sidebar.Visibility = Splitter.Visibility = Center.Visibility = isEmpty ? Visibility.Hidden : Visibility.Visible;
            if (isEmpty) SidePanel.Visibility = Visibility.Collapsed;
            MeasureToggle.IsEnabled = !isEmpty;

            var input = project.HeatmapInput(metric, Floor, isComparison ? new HeatmapView(CompareFrom, CompareTo) : new HeatmapView(Phase));
            LegendHost.Content = !showAnalysis && MissingDataNote(input) is { } note
                ? Ui.Note(note)
                : HeatmapLegend.Create(project.Scale(metric), isComparison && !showAnalysis);

            AnalysisHost.Visibility = Visible(showAnalysis);
            PlanCanvas.Visibility = Footer.Visibility = FooterDivider.Visibility = Visible(!showAnalysis);
            if (showAnalysis)
            {
                analysis.Update(metric, CompareFrom, CompareTo);
            }
            else
            {
                UpdatePlan(plan);
                UpdateHeatmap(input);
            }
            UpdatePanel();
            CommandManager.InvalidateRequerySuggested();
        }
        finally
        {
            refreshing = false;
        }
    }

    private static Visibility Visible(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private void UpdatePlan(Plan? plan)
    {
        var floor = Floor;
        var points = FloorPoints;
        if (plan == null) planImages.Remove(floor);
        else if (!planImages.TryGetValue(floor, out var cached) || !ReferenceEquals(cached.Plan, plan))
        {
            planImages[floor] = (plan, PlanImage.Bitmap(plan));
        }

        PlanCanvas.FloorName = Project.FloorName(floor);
        PlanCanvas.Image = plan == null ? null : planImages[floor].Image;
        PlanCanvas.AspectRatio = Heatmap.AspectRatio(plan);
        PlanCanvas.Points = points;
        PlanCanvas.Markers = Markers(points);
        PlanCanvas.WalkPaths = WalkPaths();
        PlanCanvas.IsWalking = Recorder.IsActive;
        PlanCanvas.PendingPointName = PendingPoint?.Name;
        PlanCanvas.Selection = Selection;
        PlanCanvas.InvalidateVisual();

        HintText.Text = Hint(points);
        PlacedText.Text = $"{points.Count(point => point.IsPlaced)} von {points.Count} Punkten gesetzt";
    }

    private string Hint(List<MeasurePoint> points)
    {
        if (Recorder.IsActive) return "Lauf aktiv: die eigene Position auf dem Plan anklicken.";
        if (PendingPoint is { } pending) return $"Auf den Plan klicken, um „{pending.Name}“ zu setzen.";
        if (points.Any(point => !point.IsPlaced))
        {
            return "Punkt ohne Position in der Liste auswählen und auf den Plan klicken. Ziehen verschiebt einen Punkt.";
        }
        return points.Count == 0 ? "Auf dieser Etage gibt es noch keine Messpunkte." : "Ziehen verschiebt einen Punkt.";
    }

    /// Farbe und Wert der gewählten Messgröße je Punkt dieser Etage.
    private Dictionary<string, PlanMarker> Markers(List<MeasurePoint> points)
    {
        var result = new Dictionary<string, PlanMarker>();
        var scale = Project.Scale(metric);
        foreach (var point in points)
        {
            if (isComparison)
            {
                var before = Project.Value(metric, point.Name, CompareFrom);
                var after = Project.Value(metric, point.Name, CompareTo);
                if (before == null && after == null) continue;
                var color = before is { } from && after is { } to
                    ? Ui.ToColor(HeatColor.Difference(scale.Change(from, to)))
                    : Color.FromRgb(0x73, 0x73, 0x73);
                string Text(double? value) => value is { } number ? metric.Format(number, withUnit: false) : "–";
                result[point.Name] = new PlanMarker(color, $"{Text(before)} → {Text(after)} {metric.Unit()}");
            }
            else if (Project.Value(metric, point.Name, Phase) is { } value)
            {
                result[point.Name] = new PlanMarker(Ui.ToColor(HeatColor.Scale(scale.Score(value))), scale.Describe(value));
            }
        }
        return result;
    }

    /// Wege der Läufe dieser Etage in der angezeigten Phase (im Vergleich beide Phasen).
    private List<PlanWalkPath> WalkPaths()
    {
        string[] shownPhases = isComparison ? [CompareFrom, CompareTo] : [Phase];
        var paths = new List<PlanWalkPath>();
        foreach (var walk in Project.Walks.Where(walk => walk.Floor == Floor))
        {
            var isActive = walk.Id == Recorder.Walk?.Id;
            if (!isActive && (!Settings.ShowWalkPaths || !shownPhases.Contains(walk.Phase))) continue;
            paths.Add(new PlanWalkPath(walk.Taps.Select(tap => new Point(tap.X, tap.Y)).ToList(), isActive));
        }
        return paths;
    }

    /// Erklärt, warum keine Heatmap zu sehen ist.
    private string? MissingDataNote(HeatmapInput input)
    {
        if (input.CompareTo is not { } to)
        {
            return input.Source.Values.Count == 0 ? $"Keine verorteten Werte für {Project.PhaseName(Phase)} auf dieser Etage." : null;
        }
        var missing = new SortedSet<string>();
        if (input.Source.Values.Count == 0) missing.Add(Project.PhaseName(CompareFrom));
        if (to.Values.Count == 0) missing.Add(Project.PhaseName(CompareTo));
        return missing.Count == 0 ? null : $"Für den Vergleich fehlen verortete Werte: {string.Join(" und ", missing)}.";
    }

    /// Berechnet das Heatmap-Bild abseits des UI-Threads, aber nur, wenn sich die Eingabe geändert hat.
    private async void UpdateHeatmap(HeatmapInput input)
    {
        if (input.ContentEquals(heatmapInput)) return;
        heatmapInput = input;
        var version = ++heatmapVersion;
        var size = Heatmap.GridSize(input.Aspect);
        var pixels = await Task.Run(() => Heatmap.Rgba(input, size.Width, size.Height));
        if (version != heatmapVersion) return;

        if (pixels == null)
        {
            PlanCanvas.Heatmap = null;
        }
        else
        {
            // WPF erwartet die Farben als BGRA.
            for (var index = 0; index < pixels.Length; index += 4) (pixels[index], pixels[index + 2]) = (pixels[index + 2], pixels[index]);
            var bitmap = BitmapSource.Create(size.Width, size.Height, 96, 96, PixelFormats.Pbgra32, null, pixels, size.Width * 4);
            bitmap.Freeze();
            PlanCanvas.Heatmap = bitmap;
        }
        PlanCanvas.InvalidateVisual();
    }

    // MARK: - Punktliste

    private void UpdateSidebar()
    {
        var content = new List<string>();
        var items = new List<ListBoxItem>();
        foreach (var floor in Project.Floors)
        {
            var points = Project.Points.Where(point => point.Floor == floor.Id).ToList();
            if (points.Count == 0) continue;
            content.Add($"# {floor.Name}");
            var header = Ui.Text(floor.Name, bold: true);
            header.Margin = new Thickness(0, 6, 0, 0);
            items.Add(new ListBoxItem { Content = header, IsHitTestVisible = false, Focusable = false, Opacity = Ui.SecondaryOpacity });
            foreach (var point in points)
            {
                var summary = MeasurementSummary(point);
                content.Add($"{point.Name}|{point.IsPlaced}|{summary}|{Project.HasMeasurements(point.Name)}");
                items.Add(PointItem(point, summary));
            }
        }
        // Nur bei geändertem Inhalt neu aufbauen, sonst springt die Liste beim Blättern zurück.
        if (!content.SequenceEqual(sidebarContent))
        {
            sidebarContent = content;
            PointList.Items.Clear();
            foreach (var item in items) PointList.Items.Add(item);
        }
        PointList.SelectedItem = PointList.Items.Cast<ListBoxItem>().FirstOrDefault(item => item.Tag is string name && name == Selection);
        Sidebar.IsEnabled = !Recorder.IsActive;
    }

    private ListBoxItem PointItem(MeasurePoint point, string? summary)
    {
        var mark = Ui.Text(point.IsPlaced ? "●" : "○", secondary: true);
        mark.Width = 18;
        mark.ToolTip = point.IsPlaced ? "Position gesetzt" : "Position noch nicht gesetzt";
        var texts = Ui.Stack(Ui.Text(point.Name));
        if (summary != null) texts.Children.Add(Ui.Text(summary, secondary: true, size: 11));
        var row = new DockPanel();
        row.Children.Add(mark);
        row.Children.Add(texts);

        var menu = new ContextMenu();
        var floors = new MenuItem { Header = "Etage" };
        foreach (var floor in Project.Floors)
        {
            var entry = new MenuItem { Header = floor.Name, IsChecked = floor.Id == point.Floor };
            entry.Click += (_, _) => Edit(project => project.SetFloor(floor.Id, point.Name));
            floors.Items.Add(entry);
        }
        var clear = new MenuItem { Header = "Position entfernen", IsEnabled = point.IsPlaced };
        clear.Click += (_, _) => Edit(project => project.ClearPosition(point.Name));
        // Punkte mit Messungen bleiben, damit keine Messdaten verwaisen.
        var remove = new MenuItem { Header = "Punkt löschen", IsEnabled = !Project.HasMeasurements(point.Name) };
        remove.Click += (_, _) => Edit(project => project.RemovePoint(point.Name));
        menu.Items.Add(floors);
        menu.Items.Add(clear);
        menu.Items.Add(new Separator());
        menu.Items.Add(remove);

        var item = new ListBoxItem { Content = row, Tag = point.Name, ContextMenu = menu, Padding = new Thickness(8, 4, 8, 4) };
        System.Windows.Automation.AutomationProperties.SetName(item, point.Name);
        return item;
    }

    private string? MeasurementSummary(MeasurePoint point)
    {
        var parts = new List<string>();
        foreach (var phase in Project.Phases)
        {
            if (Project.Measurement(point.Name, phase.Id) is not { } measurement) continue;
            parts.Add($"{phase.Name} {(measurement.RssiDBm is { } rssi ? $"{Flex.FormatNumber(rssi)} dBm" : "gemessen")}");
        }
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private void OnAddPoint(object sender, RoutedEventArgs e)
    {
        var message = $"Der Punkt wird auf der Etage „{Project.FloorName(Floor)}“ angelegt und gilt für alle Phasen.";
        if (PromptWindow.Ask(this, "Neuer Messpunkt", message, "Hinzufügen")?.NormalizedPointName() is not { Length: > 0 } name) return;
        if (Project.Point(name) == null) Edit(project => project.AddPoint(name, Floor));
        // Gibt es den Namen schon, wird der vorhandene Punkt ausgewählt.
        Select(name);
    }

    // MARK: - Messpanel

    private void UpdatePanel()
    {
        PointsTab.IsEnabled = WalkTab.IsEnabled = !IsBusy;
        if (SidePanel.Visibility != Visibility.Visible) return;
        FrameworkElement panel = WalkTab.IsChecked == true ? walkPanel : measurePanel;
        if (!ReferenceEquals(PanelHost.Content, panel)) PanelHost.Content = panel;
        if (WalkTab.IsChecked == true) walkPanel.Update(); else measurePanel.Update();
    }

    private void OnToggleMeasure(object sender, RoutedEventArgs e)
    {
        if (refreshing) return;
        SidePanel.Visibility = Visible(MeasureToggle.IsChecked == true);
        Refresh();
    }

    private void OnPanelTabPicked(object sender, RoutedEventArgs e)
    {
        if (!refreshing) Refresh();
    }

    // MARK: - Bedienelemente

    private void OnFloorPicked(object sender, SelectionChangedEventArgs e)
    {
        if (refreshing || Ui.SelectedId(FloorPicker) is not { } id) return;
        floorSelection = id;
        Refresh();
    }

    private void OnDisplayPicked(object sender, RoutedEventArgs e)
    {
        if (refreshing) return;
        showAnalysis = AnalysisButton.IsChecked == true;
        Refresh();
    }

    private void OnViewPicked(object sender, RoutedEventArgs e)
    {
        if (refreshing) return;
        isComparison = ComparisonViewButton.IsChecked == true;
        Refresh();
    }

    private void OnPhasePicked(object sender, SelectionChangedEventArgs e)
    {
        if (refreshing || Ui.SelectedId(PhasePicker) is not { } id) return;
        phaseSelection = id;
        Refresh();
    }

    private void OnComparePicked(object sender, SelectionChangedEventArgs e)
    {
        if (refreshing) return;
        compareFromSelection = Ui.SelectedId(CompareFromPicker) ?? compareFromSelection;
        compareToSelection = Ui.SelectedId(CompareToPicker) ?? compareToSelection;
        Refresh();
    }

    private void OnMetricPicked(object sender, SelectionChangedEventArgs e)
    {
        if (refreshing || MetricPicker.SelectedIndex < 0) return;
        metric = (Metric)MetricPicker.SelectedIndex;
        Refresh();
    }

    private void OnPointPicked(object sender, SelectionChangedEventArgs e)
    {
        if (!refreshing) Select((PointList.SelectedItem as ListBoxItem)?.Tag as string);
    }

    // MARK: - Projekt

    private void OnSetup(object sender, RoutedEventArgs e) => ShowSetup();

    private void ShowSetup()
    {
        if (new ProjectSetupWindow(this, Project).ShowAndGetResult() is { } updated) ReplaceProject(updated);
    }

    private void OnEditStructure(object sender, RoutedEventArgs e)
    {
        if (new StructureEditorWindow(this, Project).ShowAndGetResult() is { } updated) ReplaceProject(updated);
    }

    private void ReplaceProject(Project updated)
    {
        document.Replace(updated);
        Refresh();
    }

    // MARK: - Grundriss

    private void OnLoadPlan(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = $"Grundriss für {Project.FloorName(Floor)}",
            Filter = "Bilder (JPG, PNG, HEIC)|" + string.Join(";", ImageExtensions.Select(extension => "*" + extension)) + "|Alle Dateien (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) == true) LoadPlan(dialog.FileName);
    }

    private void OnRemovePlan(object sender, RoutedEventArgs e) => Edit(project => project.Plans.Remove(Floor));

    private void LoadPlan(string path)
    {
        Plan? plan = null;
        try
        {
            plan = PlanImage.MakePlan(File.ReadAllBytes(path));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
        if (plan == null)
        {
            Notice("Grundriss", $"„{Path.GetFileName(path)}“ ließ sich nicht als Bild lesen. Unterstützt werden JPG und PNG, " +
                                "HEIC nur mit der „HEIF-Bilderweiterung“ aus dem Microsoft Store.");
            return;
        }
        var floor = Floor;
        Edit(project => project.Plans[floor] = plan);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } files) return;
        var path = files[0];
        if (Path.GetExtension(path).Equals(ProjectDocument.Extension, StringComparison.OrdinalIgnoreCase))
        {
            if (!IsBusy && ConfirmDiscard()) OpenFile(path);
        }
        else if (!showAnalysis && Project.Floors.Count > 0)
        {
            LoadPlan(path);
        }
    }

    // MARK: - Datei

    private void Notice(string title, string message) => MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    private bool Load(string path)
    {
        try
        {
            document.Load(path);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException
                                          or System.Text.Json.JsonException)
        {
            MessageBox.Show($"„{Path.GetFileName(path)}“ ließ sich nicht als Projekt öffnen.\n\n{error.Message}", AppName,
                            MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private void OpenFile(string path)
    {
        if (!Load(path)) return;
        ResetView();
        Refresh();
        if (Project.Floors.Count == 0) ShowSetup();
    }

    private void ResetView()
    {
        Selection = null;
        isComparison = false;
        compareFromSelection = compareToSelection = null;
        planImages.Clear();
    }

    /// Fragt bei ungespeicherten Änderungen nach. `false`, wenn der Nutzer abbricht.
    private bool ConfirmDiscard()
    {
        if (!document.IsDirty) return true;
        var answer = MessageBox.Show(this, $"Änderungen an „{document.DisplayName}“ speichern?", AppName,
                                     MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return answer switch
        {
            MessageBoxResult.Yes => Save(saveAs: false),
            MessageBoxResult.No => true,
            _ => false,
        };
    }

    private bool Save(bool saveAs)
    {
        var path = document.FilePath;
        if (saveAs || path == null)
        {
            var dialog = new SaveFileDialog
            {
                Filter = ProjectDocument.FileFilter, DefaultExt = ProjectDocument.Extension, AddExtension = true,
                FileName = document.DisplayName + ProjectDocument.Extension,
            };
            if (path != null) dialog.InitialDirectory = Path.GetDirectoryName(path);
            if (dialog.ShowDialog(this) != true) return false;
            path = dialog.FileName;
        }
        try
        {
            document.Save(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Das Projekt ließ sich nicht speichern.\n\n{error.Message}", AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        Refresh();
        return true;
    }

    private void OnNew(object sender, ExecutedRoutedEventArgs e)
    {
        if (IsBusy || !ConfirmDiscard()) return;
        document.Reset();
        ResetView();
        Refresh();
        ShowSetup();
    }

    private void OnOpen(object sender, ExecutedRoutedEventArgs e)
    {
        if (IsBusy || !ConfirmDiscard()) return;
        var dialog = new OpenFileDialog { Filter = ProjectDocument.FileFilter };
        if (dialog.ShowDialog(this) == true) OpenFile(dialog.FileName);
    }

    private void OnSave(object sender, ExecutedRoutedEventArgs e) => Save(saveAs: false);

    private void OnSaveAs(object sender, ExecutedRoutedEventArgs e) => Save(saveAs: true);

    private void OnExit(object sender, RoutedEventArgs e) => Close();

    private void CanUndo(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = document.CanUndo && !IsBusy;

    private void CanRedo(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = document.CanRedo && !IsBusy;

    private void OnUndo(object sender, ExecutedRoutedEventArgs e)
    {
        document.Undo();
        Refresh();
    }

    private void OnRedo(object sender, ExecutedRoutedEventArgs e)
    {
        document.Redo();
        Refresh();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!ConfirmDiscard())
        {
            e.Cancel = true;
            return;
        }
        Runner.Cancel();
        Recorder.StopSampling();
        Settings.Save();
        base.OnClosing(e);
    }
}
