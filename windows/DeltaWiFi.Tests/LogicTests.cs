using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeltaWiFi.Logic;
using DeltaWiFi.Model;

namespace DeltaWiFi.Tests;

public class WalkTests
{
    private static readonly Tap[] Taps = [new(1_000, 0, 0), new(3_000, 1, 0.5), new(4_000, 1, 1)];

    [Fact]
    public void InterpolatesBetweenSurroundingTaps()
    {
        Assert.Equal((0.5, 0.25), WalkInterpolation.Position(2_000, Taps));
        Assert.Equal((1, 0.75), WalkInterpolation.Position(3_500, Taps));
        Assert.Equal((1, 0.5), WalkInterpolation.Position(3_000, Taps));
        Assert.Equal((0, 0), WalkInterpolation.Position(1_000, Taps));
    }

    [Fact]
    public void DropsSamplesOutsideTaps()
    {
        Assert.Null(WalkInterpolation.Position(999, Taps));
        Assert.Null(WalkInterpolation.Position(4_001, Taps));
        Assert.Null(WalkInterpolation.Position(1_000, []));
    }
}

public class PlanImageTests
{
    /// Teiltransparentes PNG in der gewünschten Größe: linke Hälfte blau, rechte durchsichtig.
    private static byte[] Png(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width / 2; column++)
            {
                pixels[(row * width + column) * 4] = 255;
                pixels[(row * width + column) * 4 + 3] = 255;
            }
        }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4)));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    [Fact]
    public void ScalesLargeImagesDownToMaxDimension()
    {
        var plan = PlanImage.MakePlan(Png(3200, 1600));
        Assert.NotNull(plan);
        Assert.Equal((1600, 800), (plan.W, plan.H));
        Assert.StartsWith("data:image/jpeg;base64,", plan.Src);
        Assert.Equal(2, Heatmap.AspectRatio(plan));

        var image = PlanImage.Bitmap(plan);
        Assert.NotNull(image);
        Assert.Equal((1600, 800), (image.PixelWidth, image.PixelHeight));
    }

    [Fact]
    public void KeepsSmallImagesAndRejectsNonImages()
    {
        var plan = PlanImage.MakePlan(Png(100, 50));
        Assert.Equal((100, 50), (plan?.W, plan?.H));
        Assert.Null(PlanImage.MakePlan("kein Bild"u8.ToArray()));
        Assert.Null(PlanImage.Bitmap(new Plan("data:image/jpeg;base64,AAAA", 1, 1)));
        Assert.Equal(1600.0 / 1120.0, Heatmap.AspectRatio(null), 9);
    }

    [Fact]
    public void TransparentAreasBecomeWhite()
    {
        var image = PlanImage.Bitmap(PlanImage.MakePlan(Png(200, 100))!)!;
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var pixel = new byte[4];
        // Pixel aus der rechten, im PNG transparenten Hälfte, und eines aus der blauen.
        converted.CopyPixels(new Int32Rect(150, 50, 1, 1), pixel, 4, 0);
        Assert.True(pixel[0] > 240 && pixel[1] > 240 && pixel[2] > 240);
        converted.CopyPixels(new Int32Rect(50, 50, 1, 1), pixel, 4, 0);
        Assert.True(pixel[0] > 200 && pixel[1] < 60 && pixel[2] < 60);
    }
}

public class MetricTests
{
    [Fact]
    public void ScoreFollowsThresholds()
    {
        Assert.Equal(1, Metric.Rssi.Score(-60));
        Assert.Equal(0.75, Metric.Rssi.Score(-65));
        Assert.Equal(0.5, Metric.Rssi.Score(-70));
        Assert.Equal(0, Metric.Rssi.Score(-80));
        Assert.Equal(0, Metric.Rssi.Score(-95));
        Assert.Equal(1, Metric.Rssi.Score(-40));
        // Niedriger ist besser
        Assert.Equal(1, Metric.GatewayMaxPing.Score(30));
        Assert.Equal(0.5, Metric.GatewayMaxPing.Score(100));
        Assert.Equal(0, Metric.GatewayMaxPing.Score(170));
        Assert.Equal(1, Metric.GatewayLoss.Score(0));
        Assert.Equal(0, Metric.GatewayLoss.Score(2));
    }

    [Fact]
    public void RatingUsesGoodAndOkayLimits()
    {
        Assert.Equal(Rating.Good, Metric.Rssi.Rating(-60));
        Assert.Equal(Rating.Okay, Metric.Rssi.Rating(-70));
        Assert.Equal(Rating.Critical, Metric.Rssi.Rating(-70.5));
        Assert.Equal(Rating.Good, Metric.GatewayLoss.Rating(0));
        Assert.Equal(Rating.Okay, Metric.GatewayLoss.Rating(1));
        Assert.Equal(Rating.Critical, Metric.GatewayLoss.Rating(1.7));
        Assert.Equal(Rating.Good, Metric.LinkRate.Rating(400));
        Assert.Equal(Rating.Critical, Metric.LanDown.Rating(99));
    }

    [Fact]
    public void ChangeSaturatesAtOneAndAHalfSteps()
    {
        Assert.Equal(1, Metric.Rssi.Change(-70, -55));
        Assert.Equal(-1, Metric.Rssi.Change(-55, -70));
        Assert.Equal(0, Metric.Rssi.Change(-70, -70));
        Assert.Equal(0.5, Metric.Rssi.Change(-70, -62.5), 9);
        // Weniger Verlust ist besser.
        Assert.Equal(1, Metric.GatewayLoss.Change(2, 0.5));
        Assert.Equal(-1, Metric.GatewayMaxPing.Change(30, 300));
    }

    [Fact]
    public void ColorsAndTexts()
    {
        Assert.Equal(HeatColor.Critical, HeatColor.Scale(0));
        Assert.Equal(HeatColor.Okay, HeatColor.Scale(0.5));
        Assert.Equal(HeatColor.Good, HeatColor.Scale(1));
        Assert.Equal(HeatColor.Unchanged, HeatColor.Difference(0));
        Assert.Equal(HeatColor.Good, HeatColor.Difference(1));
        Assert.Equal(HeatColor.Critical, HeatColor.Difference(-2));
        Assert.Equal("-79 dBm", Metric.Rssi.Format(-79));
        Assert.Equal("1,7 %", Metric.GatewayLoss.Format(1.7));
        Assert.Equal("≥ -60 dBm", Metric.Rssi.ThresholdText(-60));
        Assert.Equal("≤ 30 ms", Metric.GatewayMaxPing.ThresholdText(30));
        Assert.Equal("0 %", Metric.GatewayLoss.ThresholdText(0));
    }
}

public class HeatmapTests
{
    [Fact]
    public void WeightMatchesFormula()
    {
        foreach (var squared in new[] { 0.0, 0.0001, 0.01, 0.25, 2.0 })
        {
            var expected = 1 / Math.Pow(squared + 0.0004, 1.25);
            Assert.True(Math.Abs(Heatmap.Weight(squared) - expected) / expected < 1e-12);
        }
    }

    [Fact]
    public void InterpolatesByInverseDistance()
    {
        Assert.Null(Heatmap.Interpolate([], 1, 4, 4));

        var single = Heatmap.Interpolate([new FieldValue(0.5, 0.5, -61)], 1, 8, 8)!;
        Assert.All(single.Values, value => Assert.Equal(-61, value, 9));

        var field = Heatmap.Interpolate([new FieldValue(0.05, 0.5, -50), new FieldValue(0.95, 0.5, -90)], 1, 10, 1)!;
        Assert.Equal(-50, field.Values[0], 2);
        Assert.Equal(-90, field.Values[9], 2);
        Assert.Equal(-70, (field.Values[4] + field.Values[5]) / 2, 9);
        Assert.Equal(field.Values.OrderDescending(), field.Values);
    }

    [Fact]
    public void DistancesAreAspectCorrected()
    {
        // Plan doppelt so breit wie hoch: Der rechte Rand ist zwei Planhöhen vom linken entfernt.
        var field = Heatmap.Interpolate([new FieldValue(0, 0.5, 1)], 2, 100, 1)!;
        Assert.Equal(1.99, field.Nearest[99], 9);
        Assert.Equal(0.01, field.Nearest[0], 9);

        Assert.Equal((160, 80), Heatmap.GridSize(2));
        Assert.Equal((80, 160), Heatmap.GridSize(0.5));
    }

    [Fact]
    public void OpacityFadesWithDistance()
    {
        Assert.Equal(0.58, Heatmap.Opacity(0, false));
        Assert.Equal(0.18, Heatmap.Opacity(0.32, false), 9);
        Assert.Equal(0.18, Heatmap.Opacity(0.9, false), 9);
        Assert.Equal(0.18, Heatmap.Opacity(0.18, true), 9);
        Assert.True(Heatmap.Opacity(0.18, false) > 0.4);
    }

    [Fact]
    public void RendersPhaseAndComparison()
    {
        static byte Byte(double value) => (byte)Math.Round(value, MidpointRounding.AwayFromZero);
        var before = new FieldSource { Values = [new FieldValue(0.5, 0.5, -80)] };
        var after = new FieldSource { Values = [new FieldValue(0.5, 0.5, -55)] };
        var scale = Metric.Rssi.Scale();

        // Mittleres Pixel liegt auf dem Messpunkt: volle Deckkraft, kritisch = rot.
        var pixels = Heatmap.Rgba(new HeatmapInput(scale, 1, before), 3, 3)!;
        Assert.Equal(36, pixels.Length);
        Assert.Equal(Byte(0.58 * 255), pixels[4 * 4 + 3]);
        Assert.Equal(Byte(HeatColor.Critical.Red * 0.58 * 255), pixels[4 * 4]);
        Assert.True(pixels[3] < pixels[4 * 4 + 3]);

        pixels = Heatmap.Rgba(new HeatmapInput(scale, 1, before, after), 3, 3)!;
        Assert.Equal(Byte(HeatColor.Good.Green * 0.58 * 255), pixels[4 * 4 + 1]);
        // Umgekehrte Richtung: derselbe Unterschied gilt als Verschlechterung.
        pixels = Heatmap.Rgba(new HeatmapInput(scale, 1, after, before), 3, 3)!;
        Assert.Equal(Byte(HeatColor.Critical.Red * 0.58 * 255), pixels[4 * 4]);

        // Fehlt eine Phase, wird im Vergleich nichts gezeichnet.
        Assert.Null(Heatmap.Rgba(new HeatmapInput(scale, 1, before, new FieldSource()), 3, 3));
        Assert.Null(Heatmap.Rgba(new HeatmapInput(scale, 1, new FieldSource()), 3, 3));
    }

    [Fact]
    public void CollectsFixedPointsAndWalkSamples()
    {
        var project = Fixtures.StandardProject();
        var measurement = new Measurement { RssiDBm = -79, PingGwMaxMs = 310 };
        project.Measurements["VORHER"]["Büro Keller"] = measurement;
        project.Measurements["VORHER"]["Büro OG"] = measurement;
        project.Measurements["VORHER"]["Flur UG (Referenz nahe Router)"] = measurement;
        project.SetPosition("Büro Keller", 0.2, 0.3);
        project.SetPosition("Büro OG", 0.6, 0.6);
        // „Flur UG“ ist im Keller, aber nicht gesetzt.
        project.Walks = [new Walk("w1", "VORHER", "keller", 0), new Walk("w2", "NACHHER", "keller", 0)];
        project.Survey = new()
        {
            ["w1"] = [new SurveySample(0.5, 0.5, -60, null, 300)],
            ["w2"] = [new SurveySample(0.1, 0.1, -40)],
        };

        var rssi = project.FieldSource(Metric.Rssi, "keller", "VORHER");
        Assert.Equal([new FieldValue(0.2, 0.3, -79), new FieldValue(0.5, 0.5, -60)], rssi.Values);
        Assert.True(rssi.HasWalkData);

        // Läufe liefern keinen Ping; Samples ohne Wert zählen nicht.
        var ping = project.FieldSource(Metric.GatewayMaxPing, "keller", "VORHER");
        Assert.Equal([new FieldValue(0.2, 0.3, 310)], ping.Values);
        Assert.False(ping.HasWalkData);
        Assert.True(project.FieldSource(Metric.Snr, "keller", "VORHER").ContentEquals(new FieldSource()));

        var input = project.HeatmapInput(Metric.Rssi, "keller", new HeatmapView("VORHER", "NACHHER"));
        Assert.True(input.Source.ContentEquals(rssi));
        Assert.True(input.CompareTo!.ContentEquals(new FieldSource { Values = [new FieldValue(0.1, 0.1, -40)], HasWalkData = true }));
        var single = project.HeatmapInput(Metric.Rssi, "keller", new HeatmapView("VORHER"));
        Assert.False(single.IsComparison);
        Assert.True(single.Source.ContentEquals(rssi));
        Assert.Equal(1600.0 / 1120.0, input.Aspect, 9);
        Assert.False(input.ContentEquals(single));
        Assert.True(single.ContentEquals(project.HeatmapInput(Metric.Rssi, "keller", new HeatmapView("VORHER"))));
    }
}

public class MeasurementParsingTests
{
    [Fact]
    public void SummarizesPingSeries()
    {
        var series = Enumerable.Repeat<double?>(4, 58).Append(310.25).Append(null).ToList();
        Assert.Equal(new PingResult(1.7, 9.2, 310.3), MeasurementParsing.Ping(series));
        Assert.Equal(new PingResult(100), MeasurementParsing.Ping([null, null, null]));
        Assert.Equal(new PingResult(0, 2.5, 3), MeasurementParsing.Ping([2, 3]));
        Assert.Null(MeasurementParsing.Ping([]));
    }

    [Fact]
    public void ComputesThroughputAndParsesIperf()
    {
        Assert.Equal(241.5, MeasurementParsing.Throughput(181_147_500, TimeSpan.FromSeconds(6)));
        Assert.Null(MeasurementParsing.Throughput(0, TimeSpan.FromSeconds(6)));
        Assert.Null(MeasurementParsing.Throughput(1000, TimeSpan.Zero));

        Assert.Equal(873.5, MeasurementParsing.Iperf(
            """{"start": {}, "end": {"sum_sent": {"bits_per_second": 9.1e8}, "sum_received": {"bits_per_second": 873456789.5}}}"""));
        Assert.Null(MeasurementParsing.Iperf("""{"error": "unable to connect to server"}"""));
        Assert.Null(MeasurementParsing.Iperf("iperf3: error"));
    }

    [Fact]
    public void SummarizesSamplesWithMedian()
    {
        Assert.Null(MeasurementParsing.Median([]));
        Assert.Equal(2, MeasurementParsing.Median([3, 1, 2]));
        Assert.Equal(2.5, MeasurementParsing.Median([4, 1, 2, 3]));
        Assert.Equal("a", MeasurementParsing.MostCommon(["a", "b", "b", "a"]));
        Assert.Null(MeasurementParsing.MostCommon(Array.Empty<string>()));

        static RadioSnapshot Sample(int rssi, int? noise, double rate, string? bssid) =>
            new(rssi, noise, rate, "5", 44, 80, "11ax", bssid, "Zuhause");
        // Ein Ausreißer und ein kurzer Wechsel des Knotens verfälschen das Ergebnis nicht.
        var measurement = MeasurementParsing.Summarize([
            Sample(-61, -94, 720, "aa"), Sample(-62, -94, 720, "aa"), Sample(-88, null, 6, "bb"), Sample(-60, -95, 864, "aa"),
        ]);
        Assert.Equal(-62, measurement.RssiDBm);   // Median −61,5, gerundet
        Assert.Equal(-94, measurement.NoiseDBm);
        Assert.Equal(32, measurement.SnrDB);
        Assert.Equal(720, measurement.TxrateMbit);
        Assert.Equal("aa", measurement.Bssid);
        Assert.Equal(("5", 44, 80, "11ax"), (measurement.BandGHz, measurement.Kanal, measurement.BreiteMHz, measurement.Phy));
        Assert.Equal(new Measurement(), MeasurementParsing.Summarize([]));

        // Windows liefert keinen Rauschpegel: Signal ja, SNR nein.
        var windows = MeasurementParsing.Summarize([new RadioSnapshot(-63, TxRate: 866.7), new RadioSnapshot(-65, TxRate: 780)]);
        Assert.Equal((-64, null, null, 823), (windows.RssiDBm, windows.NoiseDBm, windows.SnrDB, windows.TxrateMbit));
        Assert.Null(windows.Kanal);

        // Unplausible Rauschwerte fallen aus dem Median; überwiegen sie, gibt es keinen SNR.
        var mixed = MeasurementParsing.Summarize([Sample(-35, -17, 866, "aa"), Sample(-35, -92, 866, "aa"), Sample(-35, -90, 866, "aa")]);
        Assert.Equal((-91, 56), (mixed.NoiseDBm, mixed.SnrDB));
        var bogus = MeasurementParsing.Summarize([Sample(-35, -17, 866, "aa"), Sample(-36, -14, 866, "aa")]);
        Assert.Equal((-36, null, null), (bogus.RssiDBm, bogus.NoiseDBm, bogus.SnrDB));
    }

    [Fact]
    public void IgnoresImplausibleNoiseInStoredData()
    {
        Assert.True(NoiseFloor.IsPlausible(-92) && NoiseFloor.IsPlausible(-75));
        Assert.False(NoiseFloor.IsPlausible(-74) || NoiseFloor.IsPlausible(-17) || NoiseFloor.IsPlausible(0));

        var measurement = new Measurement { RssiDBm = -35, NoiseDBm = -17, SnrDB = -18 };
        Assert.Null(Metric.Snr.Value(measurement));
        Assert.Equal(-35, Metric.Rssi.Value(measurement));
        Assert.Equal(50, Metric.Snr.Value(measurement with { NoiseDBm = -85, SnrDB = 50 }));

        Assert.Null(Metric.Snr.Value(new SurveySample(0, 0, -69, -13)));
        Assert.Equal(20, Metric.Snr.Value(new SurveySample(0, 0, -69, 20)));
        Assert.Equal(20, Metric.Snr.Value(new SurveySample(0, 0, null, 20)));
    }

    [Fact]
    public void FormatsTimestampInLocalTime()
    {
        var berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        Assert.Equal("2026-09-26 11:29:50", MeasurementParsing.Timestamp(DateTimeOffset.FromUnixTimeSeconds(1_790_414_990), berlin));
    }
}

public class RadioChannelTests
{
    [Fact]
    public void DerivesBandAndChannelFromFrequency()
    {
        Assert.Equal(("2.4", 1), (RadioChannel.Band(2412), RadioChannel.Channel(2412)));
        Assert.Equal(("2.4", 13), (RadioChannel.Band(2472), RadioChannel.Channel(2472)));
        Assert.Equal(14, RadioChannel.Channel(2484));
        Assert.Equal(("5", 36), (RadioChannel.Band(5180), RadioChannel.Channel(5180)));
        Assert.Equal(("5", 165), (RadioChannel.Band(5825), RadioChannel.Channel(5825)));
        Assert.Equal(("6", 1), (RadioChannel.Band(5955), RadioChannel.Channel(5955)));
        Assert.Equal(("6", 37), (RadioChannel.Band(6135), RadioChannel.Channel(6135)));
        Assert.Null(RadioChannel.Band(900));
        Assert.Null(RadioChannel.Channel(0));

        Assert.Equal("11ac", RadioChannel.Phy(8));
        Assert.Equal("11ax", RadioChannel.Phy(10));
        Assert.Equal("11be", RadioChannel.Phy(11));
        Assert.Null(RadioChannel.Phy(0));
        Assert.Equal("PHY 42", RadioChannel.Phy(42));
    }

    [Fact]
    public void ReadsChannelWidthFromBeacon()
    {
        byte[] ssid = [0, 4, (byte)'T', (byte)'e', (byte)'s', (byte)'t'];
        static byte[] HtOperation(byte secondaryOffset) => [61, 22, 36, secondaryOffset, .. new byte[20]];
        static byte[] VhtOperation(byte width, byte segment0, byte segment1) => [192, 5, width, segment0, segment1, 0, 0];

        Assert.Null(RadioChannel.Width(ssid));
        Assert.Equal(20, RadioChannel.Width([.. ssid, .. HtOperation(0)]));
        Assert.Equal(40, RadioChannel.Width([.. ssid, .. HtOperation(1)]));
        Assert.Equal(40, RadioChannel.Width([.. HtOperation(3), .. VhtOperation(0, 0, 0)]));
        Assert.Equal(80, RadioChannel.Width([.. HtOperation(1), .. VhtOperation(1, 42, 0)]));
        Assert.Equal(160, RadioChannel.Width([.. HtOperation(1), .. VhtOperation(1, 42, 50)]));
        Assert.Equal(160, RadioChannel.Width([.. HtOperation(1), .. VhtOperation(2, 50, 0)]));

        // HE Operation mit „6 GHz Operation Information“ (Bit 17), Breite im zweiten Byte der Angabe.
        static byte[] HeOperation(byte control) => [255, 12, 36, 0, 0, 0x02, 0, 0xFC, 0xFF, 37, control, 39, 0, 0];
        Assert.Equal(80, RadioChannel.Width(HeOperation(2)));
        Assert.Equal(160, RadioChannel.Width(HeOperation(3)));
        // Abgeschnittene Daten führen zu keinem Ergebnis statt zu einem Fehler.
        Assert.Null(RadioChannel.Width([61, 22, 36]));
    }
}

public class SummaryTests
{
    /// Je Punktname: Signal in VORHER und NACHHER.
    private static Project Project(params (string Name, double? Before, double? After)[] values)
    {
        var project = Fixtures.StandardProject();
        foreach (var (name, before, after) in values)
        {
            if (before is { } first) project.Record(new Measurement { RssiDBm = first }, name, "VORHER");
            if (after is { } second) project.Record(new Measurement { RssiDBm = second }, name, "NACHHER");
        }
        return project;
    }

    [Fact]
    public void SummarizesPhase()
    {
        var project = Project(("Büro OG", -58, -50), ("Büro Keller", -79, -65), ("Schlafzimmer", -70, null), ("Küche Esstisch", -64, -64.5));
        Assert.Equal(new PhaseSummary(-67, 1, 2, 1), project.PhaseSummary(Metric.Rssi, "VORHER"));
        Assert.Equal(new PhaseSummary(-64.5, 1, 2, 0), project.PhaseSummary(Metric.Rssi, "NACHHER"));
        // Messgröße ohne Werte und unbekannte Phase
        Assert.Equal(new PhaseSummary(), project.PhaseSummary(Metric.LanDown, "VORHER"));
        Assert.Equal(0, project.PhaseSummary(Metric.Rssi, "gibt-es-nicht").Count);
    }

    [Fact]
    public void CountsBetterAndWorse()
    {
        var project = Project(("Büro OG", -58, -50), ("Büro Keller", -79, -65), ("Schlafzimmer", -70, null),
                              ("Küche Esstisch", -64, -64.5), ("Wohnzimmer Sofa", -60, -66));
        var forward = project.ChangeSummary(Metric.Rssi, "VORHER", "NACHHER");
        Assert.Equal(new ChangeSummary(2, 1, 1), forward);
        Assert.Equal(4, forward.Compared);
        // In Gegenrichtung tauschen besser und schlechter.
        Assert.Equal(new ChangeSummary(1, 2, 1), project.ChangeSummary(Metric.Rssi, "NACHHER", "VORHER"));
        Assert.Equal(new ChangeSummary(0, 0, 5), project.ChangeSummary(Metric.Rssi, "VORHER", "VORHER"));
    }

    [Fact]
    public void TrendHasToleranceAndRespectsDirection()
    {
        Assert.Equal(Trend.Unchanged, Metric.Rssi.Trend(-70, -69.5));
        Assert.Equal(Trend.Better, Metric.Rssi.Trend(-70, -69));
        Assert.Equal(Trend.Worse, Metric.Rssi.Trend(-70, -72));
        // Niedriger ist besser
        Assert.Equal(Trend.Better, Metric.GatewayMaxPing.Trend(100, 60));
        Assert.Equal(Trend.Unchanged, Metric.GatewayMaxPing.Trend(30, 35));
        Assert.Equal(Trend.Worse, Metric.GatewayLoss.Trend(0, 1.7));

        Assert.Equal("+18 dB", Metric.Rssi.FormatDifference(18));
        Assert.Equal("-40,3 ms", Metric.GatewayMaxPing.FormatDifference(-40.25));
        Assert.Equal("0 %", Metric.GatewayLoss.FormatDifference(0));
        Assert.Equal("0 %", Metric.GatewayLoss.FormatDifference(-0.04));
    }
}
