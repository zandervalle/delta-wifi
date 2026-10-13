//
//  ProjectDocument.cs
//  DeltaWiFi
//
//  Das geöffnete Projekt mit Dateipfad, „ungespeichert“-Merker sowie Rückgängig und Wiederholen.
//

using DeltaWiFi.Model;

namespace DeltaWiFi.Views;

public sealed class ProjectDocument
{
    public const string Extension = ".wlanprojekt";
    public const string FileFilter = "WLAN-Projekt (*.wlanprojekt)|*.wlanprojekt|Alle Dateien (*.*)|*.*";
    private const int UndoLimit = 100;

    public Project Project { get; private set; } = new();
    /// Datei des Projekts; `null`, solange es noch nie gespeichert wurde.
    public string? FilePath { get; private set; }
    public bool IsDirty { get; private set; }

    private readonly List<Project> undoStack = [];
    private readonly List<Project> redoStack = [];

    public bool CanUndo => undoStack.Count > 0;
    public bool CanRedo => redoStack.Count > 0;

    public string DisplayName => FilePath == null ? "Unbenannt" : Path.GetFileNameWithoutExtension(FilePath);

    /// Ändert das Projekt; der Stand davor lässt sich mit „Rückgängig“ zurückholen.
    public void Edit(Action<Project> change)
    {
        undoStack.Add(Project.Clone());
        if (undoStack.Count > UndoLimit) undoStack.RemoveAt(0);
        redoStack.Clear();
        change(Project);
        IsDirty = true;
    }

    /// Ersetzt das Projekt als Ganzes, z. B. nach einem Dialog, der auf einer Kopie gearbeitet hat.
    public void Replace(Project project) => Edit(_ => Project = project);

    public void Undo() => Step(undoStack, redoStack);

    public void Redo() => Step(redoStack, undoStack);

    private void Step(List<Project> from, List<Project> to)
    {
        if (from.Count == 0) return;
        to.Add(Project);
        Project = from[^1];
        from.RemoveAt(from.Count - 1);
        IsDirty = true;
    }

    public void Reset()
    {
        Project = new Project();
        FilePath = null;
        Clear();
    }

    public void Load(string path)
    {
        Project = Project.Decode(File.ReadAllBytes(path));
        FilePath = path;
        Clear();
    }

    public void Save(string path)
    {
        // Erst vollständig daneben schreiben, dann ersetzen: Ein Abbruch mittendrin lässt die alte Datei heil.
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, Project.Encoded());
        File.Move(temporary, path, overwrite: true);
        FilePath = path;
        IsDirty = false;
    }

    private void Clear()
    {
        undoStack.Clear();
        redoStack.Clear();
        IsDirty = false;
    }
}
