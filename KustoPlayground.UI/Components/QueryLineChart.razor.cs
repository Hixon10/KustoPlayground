using System.Globalization;
using System.Text;
using KustoPlayground.Core;
using Microsoft.AspNetCore.Components;

namespace KustoPlayground.UI.Components;

public partial class QueryLineChart
{
    [Parameter, EditorRequired] public ExecutionResult Result { get; set; } = null!;

    private static readonly string[] Colors = ["var(--cp-accent)", "var(--cp-link)", "var(--cp-success)", "var(--cp-text)"];
    private static readonly string?[] Dashes = [null, "7 3", "2 3"];
    private readonly List<ChartSeries> series = [];
    private string? notice, description, xLow, xHigh, yLow, yHigh;
    private bool showLegend;

    private readonly record struct Sample(double X, double Y, bool Start);
    private sealed record ChartSeries(string Label, string Color, string? Dash, string Path, List<(string X, string Y)> Points);

    protected override void OnParametersSet()
    {
        series.Clear();
        bool time = Result.Visualization == "timechart";
        var columns = Result.Columns;
        var measures = columns?.Skip(1).Where(c => IsNumeric(c.Type)).ToArray() ?? [];
        if (columns is not [var x, ..] || !(time ? x.Type == "datetime" : IsNumeric(x.Type)) || measures.Length == 0)
        {
            notice = $"To draw a {Result.Visualization}, project {(time ? "a datetime" : "a numeric")} X column first, " +
                "followed by at least one numeric Y column. The query results are shown below.";
            return;
        }

        var groupColumn = columns.Skip(1).FirstOrDefault(c => c.Type == "string");
        var xs = new Axis(useReal: x.Type == "real");
        var ys = new Axis(useReal: measures.Any(m => m.Type == "real"));
        var pending = new List<(string Label, List<Sample> Samples)>();
        var rows = Result.ResultRows!;
        foreach (var group in rows.GroupBy(row => groupColumn is null ? null : ExecutionResult.FormatValue(row[groupColumn.Name]),
                     StringComparer.Ordinal))
        {
            foreach (var measure in measures)
            {
                var samples = new List<Sample>();
                bool start = true;
                foreach (var row in group)
                {
                    var xValue = row[x.Name];
                    var yValue = row[measure.Name];
                    if (xValue is null || yValue is null || !IsFinite(xValue) || !IsFinite(yValue))
                    {
                        start = true;
                        continue;
                    }
                    samples.Add(new Sample(xs.Add(xValue), ys.Add(yValue), start));
                    start = false;
                }
                if (samples.Count > 0)
                    pending.Add((groupColumn is null ? measure.Name : $"{measure.Name} ({groupColumn.Name}={group.Key})", samples));
            }
        }
        if (pending.Count == 0)
        {
            notice = rows.Count == 0 ? "No chart to draw: the query returned no rows." :
                "No chart to draw: no row has a finite value on both axes.";
            return;
        }

        notice = null;
        description = $"{Result.Visualization}: {string.Join(", ", measures.Select(m => m.Name))} by {x.Name}";
        if (groupColumn is not null) description += $", grouped by {groupColumn.Name}";
        showLegend = groupColumn is not null || measures.Length > 1;
        (xLow, xHigh) = xs.Labels;
        (yLow, yHigh) = ys.Labels;
        foreach (var (label, samples) in pending)
            series.Add(ScaleSeries(label, samples, xs, ys, series.Count));
    }

    private static ChartSeries ScaleSeries(string label, List<Sample> samples, Axis xs, Axis ys, int index)
    {
        var points = new List<(string, string)>(samples.Count);
        var path = new StringBuilder();
        foreach (var sample in samples)
        {
            var px = (80 + 544 * xs.Fraction(sample.X)).ToString("0.###", CultureInfo.InvariantCulture);
            var py = (168 - 152 * ys.Fraction(sample.Y)).ToString("0.###", CultureInfo.InvariantCulture);
            path.Append(sample.Start ? 'M' : 'L').Append(px).Append(' ').Append(py);
            points.Add((px, py));
        }
        return new ChartSeries(label, Colors[index % Colors.Length], Dashes[index % Dashes.Length], path.ToString(), points);
    }

    private static bool IsNumeric(string type) => type is "int" or "long" or "real" or "decimal";
    private static bool IsFinite(object value) => value switch
    {
        double d => double.IsFinite(d),
        float f => float.IsFinite(f),
        _ => true
    };

    private sealed class Axis(bool useReal)
    {
        private double min = double.PositiveInfinity, max = double.NegativeInfinity;
        private object? low, high;
        private decimal? origin;

        internal (string, string) Labels => (Label(low!), Label(high!));

        internal double Add(object value)
        {
            var n = useReal ? Convert.ToDouble(value, CultureInfo.InvariantCulture) : Offset(value);
            if (n < min) { min = n; low = value; }
            if (n > max) { max = n; high = value; }
            return n;
        }

        internal double Fraction(double value)
        {
            if (min == max) return 0.5;
            return double.IsFinite(max - min) ? (value - min) / (max - min) :
                (value / 2 - min / 2) / (max / 2 - min / 2);
        }

        private double Offset(object value)
        {
            // Subtract before converting to double so adjacent datetime ticks and long IDs stay distinct.
            decimal n = value switch
            {
                DateTime d => (d.Kind == DateTimeKind.Local ? d.ToUniversalTime() : d).Ticks,
                DateTimeOffset d => d.UtcTicks,
                _ => Convert.ToDecimal(value, CultureInfo.InvariantCulture)
            };
            decimal start = origin ??= n;
            return (n < 0) == (start < 0) ? (double)(n - start) : (double)n - (double)start;
        }

        private static string Label(object value) => value is DateTime or DateTimeOffset
            ? ExecutionResult.FormatValue(value)
            : ((IFormattable)value).ToString("G5", CultureInfo.InvariantCulture);
    }
}
