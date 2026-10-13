//
//  PlanImage.cs
//  DeltaWiFi
//
//  Grundriss-Bilder einlesen (JPG/PNG, HEIC mit installierter HEIF-Erweiterung),
//  verkleinern und als Data-URL ablegen.
//

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeltaWiFi.Model;

namespace DeltaWiFi.Logic;

public static class PlanImage
{
    /// Längste Kante eines gespeicherten Grundrisses in Pixeln.
    public const int MaxDimension = 1600;

    /// Bilddatei → Grundriss: Ausrichtung anwenden, auf `maxDimension` verkleinern, als JPEG speichern.
    public static Plan? MakePlan(byte[] data, int maxDimension = MaxDimension, int quality = 80)
    {
        try
        {
            using var input = new MemoryStream(data);
            var frame = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            var image = Oriented(frame);

            var scale = Math.Min(1.0, (double)maxDimension / Math.Max(image.PixelWidth, image.PixelHeight));
            var width = Math.Max(1, (int)Flex.Rounded(image.PixelWidth * scale));
            var height = Math.Max(1, (int)Flex.Rounded(image.PixelHeight * scale));

            // Auf Weiß zeichnen: JPEG kennt keine Transparenz, sonst würde ein PNG-Hintergrund schwarz.
            var visual = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
            using (var context = visual.RenderOpen())
            {
                var rect = new Rect(0, 0, width, height);
                context.DrawRectangle(Brushes.White, null, rect);
                context.DrawImage(image, rect);
            }
            var flattened = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            flattened.Render(visual);

            var encoder = new JpegBitmapEncoder { QualityLevel = quality };
            encoder.Frames.Add(BitmapFrame.Create(flattened));
            using var output = new MemoryStream();
            encoder.Save(output);
            return new Plan("data:image/jpeg;base64," + Convert.ToBase64String(output.ToArray()), width, height);
        }
        catch (Exception error) when (error is NotSupportedException or IOException or ArgumentException
                                          or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    /// Dreht bzw. spiegelt das Bild nach seiner EXIF-Ausrichtung (Fotos vom Handy liegen oft gedreht in der Datei).
    private static BitmapSource Oriented(BitmapFrame frame)
    {
        var orientation = 1;
        try
        {
            if (frame.Metadata is BitmapMetadata metadata)
            {
                var query = metadata.Format == "jpg" ? "/app1/ifd/{ushort=274}" : "/ifd/{ushort=274}";
                if (metadata.ContainsQuery(query) && metadata.GetQuery(query) is ushort value) orientation = value;
            }
        }
        catch (Exception error) when (error is NotSupportedException or InvalidOperationException or ArgumentException)
        {
            // Ohne lesbare Metadaten bleibt das Bild, wie es ist.
        }

        Transform? transform = orientation switch
        {
            2 => new ScaleTransform(-1, 1),
            3 => new RotateTransform(180),
            4 => new ScaleTransform(1, -1),
            5 => new TransformGroup { Children = { new RotateTransform(90), new ScaleTransform(-1, 1) } },
            6 => new RotateTransform(90),
            7 => new TransformGroup { Children = { new RotateTransform(270), new ScaleTransform(-1, 1) } },
            8 => new RotateTransform(270),
            _ => null,
        };
        return transform == null ? frame : new TransformedBitmap(frame, transform);
    }

    /// Bilddaten aus der Data-URL eines Grundrisses.
    public static byte[]? ImageData(Plan plan)
    {
        var marker = plan.Src.IndexOf("base64,", StringComparison.Ordinal);
        if (marker < 0) return null;
        try
        {
            return Convert.FromBase64String(plan.Src[(marker + 7)..]);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// Dekodiertes Bild eines Grundrisses; `null`, wenn die Daten kein Bild sind.
    public static BitmapSource? Bitmap(Plan plan)
    {
        if (ImageData(plan) is not { } data) return null;
        try
        {
            using var input = new MemoryStream(data);
            var frame = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            frame.Freeze();
            return frame;
        }
        catch (Exception error) when (error is NotSupportedException or IOException or ArgumentException
                                          or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }
}
