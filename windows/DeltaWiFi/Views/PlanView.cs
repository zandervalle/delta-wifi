//
//  PlanView.cs
//  DeltaWiFi
//
//  Grundriss einer Etage mit Heatmap und den festen Messpunkten. Klick setzt den
//  ausgewählten, noch nicht gesetzten Punkt; Ziehen verschiebt einen Punkt.
//

using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DeltaWiFi.Model;

namespace DeltaWiFi.Views;

/// Darstellung eines Messpunkts: Statusfarbe und Wert der gewählten Messgröße.
public sealed record PlanMarker(Color Color, string Text);

/// Weg eines Laufs: die angeklickten Positionen relativ 0…1.
/// Der laufende Lauf wird durchgezogen gezeichnet, abgeschlossene gestrichelt.
public sealed record PlanWalkPath(IReadOnlyList<Point> Taps, bool IsActive);

public sealed class PlanView : FrameworkElement
{
    public string FloorName = "";
    public ImageSource? Image;
    public ImageSource? Heatmap;
    /// Breite / Höhe des Plans.
    public double AspectRatio = 1600.0 / 1120.0;
    /// Punkte dieser Etage.
    public IReadOnlyList<MeasurePoint> Points = [];
    /// Farbe und Wert je Punktname; Punkte ohne Eintrag haben keinen Messwert.
    public IReadOnlyDictionary<string, PlanMarker> Markers = new Dictionary<string, PlanMarker>();
    public IReadOnlyList<PlanWalkPath> WalkPaths = [];
    /// Im Lauf-Modus setzt jeder Klick auf den Plan eine Position des Laufs.
    public bool IsWalking;
    /// Ausgewählter Punkt dieser Etage, der noch keine Position hat.
    public string? PendingPointName;
    public string? Selection;

    public event Action<string, double, double>? PositionSet;
    public event Action<double, double>? WalkTapped;
    public event Action<string?>? SelectionChanged;

    private static readonly Color NoValueColor = Color.FromRgb(0x73, 0x73, 0x73);
    private static readonly Typeface LabelFont = new("Segoe UI");
    private static readonly Typeface LabelFontBold = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    // Punkt unter der Maus beim Drücken; `dragLocation` ist gesetzt, sobald wirklich gezogen wird.
    private string? pressedPoint;
    private Point pressLocation;
    private Point? dragLocation;

    public PlanView()
    {
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        ClipToBounds = true;
    }

    /// Fläche des Plans: eingepasst und zentriert, mit etwas Rand.
    private Rect PlanRect
    {
        get
        {
            var width = Math.Max(ActualWidth - 32, 1);
            var height = Math.Min(Math.Max(ActualHeight - 32, 1), width / AspectRatio);
            width = height * AspectRatio;
            return new Rect((ActualWidth - width) / 2, (ActualHeight - height) / 2, width, height);
        }
    }

    private Point Location(MeasurePoint point, Rect plan)
    {
        if (dragLocation is { } drag && pressedPoint == point.Name)
        {
            return new Point(Math.Clamp(drag.X, plan.Left, plan.Right), Math.Clamp(drag.Y, plan.Top, plan.Bottom));
        }
        return new Point(plan.Left + (point.X ?? 0) * plan.Width, plan.Top + (point.Y ?? 0) * plan.Height);
    }

    // MARK: - Zeichnen

    protected override void OnRender(DrawingContext context)
    {
        // Durchsichtige Fläche, damit Klicks überall ankommen.
        context.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        var plan = PlanRect;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        if (Image != null) context.DrawImage(Image, plan); else DrawPlaceholder(context, plan, dpi);
        if (Heatmap != null) context.DrawImage(Heatmap, plan);
        DrawWalks(context, plan);

        // Der ausgewählte Punkt liegt oben.
        foreach (var point in Points.Where(point => point.IsPlaced).OrderBy(point => point.Name == Selection))
        {
            DrawMarker(context, point, Location(point, plan), dpi);
        }
    }

    private void DrawPlaceholder(DrawingContext context, Rect plan, double dpi)
    {
        // Die geerbte Textfarbe passt zu Hell- und Dunkelmodus.
        var foreground = (GetValue(System.Windows.Documents.TextElement.ForegroundProperty) as Brush ?? Brushes.Gray).Clone();
        foreground.Opacity = Ui.SecondaryOpacity;
        var border = new Pen(foreground, 1) { DashStyle = new DashStyle([6, 4], 0) };
        context.DrawRoundedRectangle(Ui.CardBrush, border, plan, 8, 8);

        var lines = new[]
        {
            Formatted($"Kein Grundriss für {FloorName}", LabelFontBold, 15, foreground, dpi),
            Formatted("Bild hierher ziehen oder über „Projekt › Grundriss laden“ wählen.", LabelFont, 13, foreground, dpi),
            Formatted("Punkte lassen sich trotzdem setzen.", LabelFont, 13, foreground, dpi),
        };
        var y = plan.Top + (plan.Height - lines.Sum(line => line.Height + 4)) / 2;
        foreach (var line in lines)
        {
            line.MaxTextWidth = Math.Max(plan.Width - 24, 1);
            line.TextAlignment = TextAlignment.Center;
            context.DrawText(line, new Point(plan.Left + 12, y));
            y += line.Height + 4;
        }
    }

    /// Wege und Positionen der Läufe: heller Rand unter dunkler Linie, damit sie auf jeder Heatmap lesbar bleiben.
    private void DrawWalks(DrawingContext context, Rect plan)
    {
        foreach (var path in WalkPaths)
        {
            var points = path.Taps.Select(tap => new Point(plan.Left + tap.X * plan.Width, plan.Top + tap.Y * plan.Height)).ToList();
            if (points.Count > 1)
            {
                var line = new StreamGeometry();
                using (var geometry = line.Open())
                {
                    geometry.BeginFigure(points[0], false, false);
                    geometry.PolyLineTo(points.Skip(1).ToList(), true, true);
                }
                context.DrawGeometry(null, WalkPen(Colors.White, 0.9, 4.5, path.IsActive), line);
                context.DrawGeometry(null, WalkPen(Colors.Black, 0.75, 2, path.IsActive), line);
            }
            for (var index = 0; index < points.Count; index++)
            {
                // Die zuletzt gesetzte Position des laufenden Laufs ist größer.
                var radius = path.IsActive && index == points.Count - 1 ? 6 : 3.5;
                context.DrawEllipse(Ui.Frozen(Color.FromArgb(0xCC, 0, 0, 0)), new Pen(Brushes.White, 1.5), points[index], radius, radius);
            }
        }
    }

    private static Pen WalkPen(Color color, double opacity, double width, bool solid)
    {
        var pen = new Pen(new SolidColorBrush(color) { Opacity = opacity }, width)
        {
            StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round,
        };
        // Strichlängen zählen in WPF in Vielfachen der Linienbreite.
        if (!solid) pen.DashStyle = new DashStyle([6 / width, 5 / width], 0);
        return pen;
    }

    private void DrawMarker(DrawingContext context, MeasurePoint point, Point location, double dpi)
    {
        var info = Markers.GetValueOrDefault(point.Name);
        context.DrawEllipse(Ui.Frozen(Color.FromArgb(0x50, 0, 0, 0)), null, new Point(location.X, location.Y + 1), 8.5, 8.5);
        context.DrawEllipse(Ui.Frozen(info?.Color ?? NoValueColor), new Pen(Brushes.White, 2), location, 7, 7);
        if (Selection == point.Name) context.DrawEllipse(null, new Pen(SystemColors.AccentColorBrush, 2.5), location, 11, 11);

        var name = Formatted(point.Name, LabelFont, 11, Brushes.Black, dpi);
        var value = info == null ? null : Formatted(info.Text, LabelFontBold, 11, Brushes.Black, dpi);
        var width = Math.Max(name.Width, value?.Width ?? 0);
        var box = new Rect(location.X - width / 2 - 5, location.Y + 13, width + 10, name.Height + (value?.Height ?? 0) + 2);
        context.DrawRoundedRectangle(Ui.Frozen(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF)), null, box, 5, 5);
        context.DrawText(name, new Point(location.X - name.Width / 2, box.Top + 1));
        if (value != null) context.DrawText(value, new Point(location.X - value.Width / 2, box.Top + 1 + name.Height));
    }

    private static FormattedText Formatted(string text, Typeface font, double size, Brush brush, double dpi) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, font, size, brush, dpi);

    // MARK: - Maus

    private MeasurePoint? PointAt(Point location, Rect plan)
    {
        if (IsWalking) return null;
        return Points.Where(point => point.IsPlaced)
            .OrderByDescending(point => point.Name == Selection)
            .FirstOrDefault(point => (Location(point, plan) - location).Length <= 14);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        pressLocation = e.GetPosition(this);
        pressedPoint = PointAt(pressLocation, PlanRect)?.Name;
        dragLocation = null;
        CaptureMouse();
        if (pressedPoint != null) SelectionChanged?.Invoke(pressedPoint);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (pressedPoint == null || !IsMouseCaptured) return;
        var location = e.GetPosition(this);
        if (dragLocation == null && (location - pressLocation).Length < 3) return;
        dragLocation = location;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!IsMouseCaptured) return;
        var plan = PlanRect;
        var location = e.GetPosition(this);
        var x = (location.X - plan.Left) / plan.Width;
        var y = (location.Y - plan.Top) / plan.Height;
        // Erst merken, dann loslassen: Das Loslassen setzt den Zustand zurück.
        var (pressed, dragged) = (pressedPoint, dragLocation != null);
        ReleaseMouseCapture();

        if (pressed != null)
        {
            if (dragged) PositionSet?.Invoke(pressed, x, y); else InvalidateVisual();
        }
        else if (plan.Contains(location))
        {
            if (IsWalking) WalkTapped?.Invoke(x, y);
            else if (PendingPointName != null) PositionSet?.Invoke(PendingPointName, x, y);
            else SelectionChanged?.Invoke(null);
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        pressedPoint = null;
        dragLocation = null;
        InvalidateVisual();
    }
}
