using System.Globalization;
using System.Text;
using KustoPlayground.Core;
using Microsoft.AspNetCore.Components;

namespace KustoPlayground.UI.Components;

public partial class QueryLineChart
{
    [Parameter, EditorRequired] public ExecutionResult Result { get; set; } = null!;

    private readonly List<(string X, string Y)> points = [];
    private string? notice, description, line, xLow, xHigh, yLow, yHigh;

    protected override void OnParametersSet()
    {
        points.Clear();
        bool time = Result.Visualization == "timechart";
        if (Result.Columns is not [var x, var y] || !(time ? x.Type == "datetime" : IsNumeric(x.Type)) || !IsNumeric(y.Type))
        {
            notice = $"To draw a {Result.Visualization}, project exactly two columns: " +
                $"{(time ? "a datetime" : "a numeric")} X column followed by a numeric Y column. The query results are shown below.";
            return;
        }

        var xs = new Axis(x.Type);
        var ys = new Axis(y.Type);
        var samples = new List<(int Row, double X, double Y)>();
        var rows = Result.ResultRows!;
        for (int i = 0; i < rows.Count; i++)
        {
            var xValue = rows[i][x.Name];
            var yValue = rows[i][y.Name];
            if (xValue is null || yValue is null || !IsFinite(xValue) || !IsFinite(yValue)) continue;
            samples.Add((i, xs.Add(xValue), ys.Add(yValue)));
        }
        if (samples.Count == 0)
        {
            notice = rows.Count == 0 ? "No chart to draw: the query returned no rows." :
                "No chart to draw: no row has a finite value on both axes.";
            return;
        }

        notice = null;
        description = $"{Result.Visualization}: {y.Name} by {x.Name}";
        (xLow, xHigh) = xs.Labels;
        (yLow, yHigh) = ys.Labels;
        var path = new StringBuilder();
        int previous = -2;
        foreach (var sample in samples)
        {
            var px = (80 + 544 * xs.Fraction(sample.X)).ToString("0.###", CultureInfo.InvariantCulture);
            var py = (168 - 152 * ys.Fraction(sample.Y)).ToString("0.###", CultureInfo.InvariantCulture);
            path.Append(sample.Row == previous + 1 ? 'L' : 'M').Append(px).Append(' ').Append(py);
            points.Add((px, py));
            previous = sample.Row;
        }
        line = path.ToString();
    }

    private static bool IsNumeric(string type) => type is "int" or "long" or "real" or "decimal";
    private static bool IsFinite(object value) => value switch
    {
        double d => double.IsFinite(d),
        float f => float.IsFinite(f),
        _ => true
    };

    private sealed class Axis(string type)
    {
        private double min = double.PositiveInfinity, max = double.NegativeInfinity;
        private object? low, high;
        private decimal? origin;

        internal (string, string) Labels => (Label(low!), Label(high!));

        internal double Add(object value)
        {
            var n = type == "real" ? Convert.ToDouble(value, CultureInfo.InvariantCulture) : Offset(value);
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
