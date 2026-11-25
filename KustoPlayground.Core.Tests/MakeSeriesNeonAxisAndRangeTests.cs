namespace KustoPlayground.Core.Tests;

public class MakeSeriesNeonAxisAndRangeTests
{
    #region Basic DateTime Axis and Bin Intervals

    [Test]
    [Description("make-series - basic datetime axis with explicit from/to/step sorts unsorted rows into aligned ascending bins")]
    public void MakeSeries_DateTimeAxis_ExplicitFromToStep_UnsortedInput()
    {
        Table table = MakeSeriesNeonTestUtils.CreateDateTimeIntTable(
            [
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 3, 15), 40),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0, 5), 10),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0, 45), 15),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 2, 0), 30)
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            """
            Events
            | make-series total=sum(Val), cnt=count() on Ts
              from datetime(2025-01-01T00:00:00Z) to datetime(2025-01-01T04:00:00Z) step 1h
            """);

        MakeSeriesNeonTestUtils.AssertColumnOrder(row, "total", "cnt", "Ts");
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "total", 25, 0, 30, 40);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "cnt", 2, 0, 1, 1);
        MakeSeriesNeonTestUtils.AssertDateTimeSeries(
            row,
            "Ts",
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 1),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 2),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 3));
    }

    [Test]
    [Description("make-series - half-open bin boundary [binStart, binStart + step) places exact boundary values in the right bin and excludes rows before from or at/after aligned to")]
    public void MakeSeries_DateTimeAxis_HalfOpenBinBoundaries_AndRangeFiltering()
    {
        Table table = MakeSeriesNeonTestUtils.CreateDateTimeIntTable(
            [
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0, 59, 59), 100),  // strictly before from -> excluded
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 1, 0, 0), 1),      // exact from -> bin 0
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 1, 59, 59), 2),    // end of bin 0 -> bin 0
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 2, 0, 0), 4),      // exact bin 1 start -> bin 1
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 3, 0, 0), 8),      // exact aligned to -> excluded
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 3, 0, 1), 16)      // strictly after to -> excluded
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            """
            Events
            | make-series s=sum(Val) on Ts
              from datetime(2025-01-01T01:00:00Z) to datetime(2025-01-01T03:00:00Z) step 1h
            """);

        MakeSeriesNeonTestUtils.AssertLongSeries(row, "s", 3, 4);
        MakeSeriesNeonTestUtils.AssertDateTimeSeries(
            row,
            "Ts",
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 1),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 2));
    }

    [Test]
    [Description("make-series - various datetime step granularities (1d, 30m, 15s, 500ms) align accurately")]
    public void MakeSeries_DateTimeAxis_SubSecondAndMultiDaySteps()
    {
        Table subSecondTable = MakeSeriesNeonTestUtils.CreateDateTimeIntTable(
            [
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0, 0, 0, 100), 1),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0, 0, 0, 600), 2),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0, 0, 1, 200), 4)
            ],
            tableName: "SubSec");

        Table multiDayTable = MakeSeriesNeonTestUtils.CreateDateTimeIntTable(
            [
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 18, 0), 10),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 3, 6, 0), 30)
            ],
            tableName: "Days");

        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(subSecondTable, multiDayTable);

        IReadOnlyDictionary<string, object?> subSecRow = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            """
            SubSec
            | make-series s=sum(Val) on Ts
              from datetime(2025-01-01T00:00:00.000Z) to datetime(2025-01-01T00:00:01.500Z) step 500ms
            """);

        MakeSeriesNeonTestUtils.AssertLongSeries(subSecRow, "s", 1, 2, 4);
        MakeSeriesNeonTestUtils.AssertDateTimeSeries(
            subSecRow,
            "Ts",
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0, 0, 0, 0),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0, 0, 0, 500),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0, 0, 1, 0));

        IReadOnlyDictionary<string, object?> dayRow = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            """
            Days
            | make-series s=sum(Val) on Ts
              from datetime(2025-01-01T00:00:00Z) to datetime(2025-01-04T00:00:00Z) step 1d
            """);

        MakeSeriesNeonTestUtils.AssertLongSeries(dayRow, "s", 10, 0, 30);
        MakeSeriesNeonTestUtils.AssertDateTimeSeries(
            dayRow,
            "Ts",
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 2),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 3));
    }

    [Test]
    [Description("make-series - single-bin explicit range produces length-1 arrays")]
    public void MakeSeries_DateTimeAxis_SingleBinRange()
    {
        Table table = MakeSeriesNeonTestUtils.CreateDateTimeIntTable(
            [
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 10, 0), 7),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 10, 59), 8)
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            """
            Events
            | make-series s=sum(Val) on Ts
              from datetime(2025-01-01T10:00:00Z) to datetime(2025-01-01T11:00:00Z) step 1h
            """);

        MakeSeriesNeonTestUtils.AssertLongSeries(row, "s", 15);
        MakeSeriesNeonTestUtils.AssertDateTimeSeries(row, "Ts", MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 10));
    }

    #endregion

    #region Unaligned From / To Anchoring and Edge Ranges

    [Test]
    [Description("make-series - unaligned from anchors bins with bin_at(Axis, step, from) rather than epoch-aligned bin")]
    public void MakeSeries_UnalignedFrom_AnchorsBinsAtFrom()
    {
        Table table = MakeSeriesNeonTestUtils.CreateDateTimeIntTable(
            [
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0, 10), 999),  // before from=00:15 -> excluded
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0, 15), 10),   // bin 00:15
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 1, 10), 20),   // still in [00:15, 01:15) -> bin 00:15
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 1, 15), 30),   // bin 01:15
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 2, 50), 40),   // bin 02:15
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 3, 15), 888)   // exact to -> excluded
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            """
            Events
            | make-series s=sum(Val) on Ts
              from datetime(2025-01-01T00:15:00Z) to datetime(2025-01-01T03:15:00Z) step 1h
            """);

        MakeSeriesNeonTestUtils.AssertLongSeries(row, "s", 30, 30, 40);
        MakeSeriesNeonTestUtils.AssertDateTimeSeries(
            row,
            "Ts",
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0, 15),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 1, 15),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 2, 15));
    }

    [Test]
    [Description("make-series - unaligned to includes rows whose bin_at(Axis, step, from) < to even when raw Axis >= to")]
    public void MakeSeries_UnalignedTo_FiltersByBinStartLessThanTo_LongAxis()
    {
        // Oracle-verified against ADX: for from=0 to=5 step=2, bins are [0, 2, 4];
        // x=5 maps to bin_at(5, 2, 0)=4 (< 5), so x=5 is included in bin 4 while x=6 (bin 6 >= 5) is excluded.
        Table table = MakeSeriesNeonTestUtils.CreateLongValueTable(
            [
                (0, 10),
                (4, 20),
                (5, 30),
                (6, 40)
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            "Events | make-series c=count(), s=sum(Val) on X from 0 to 5 step 2");

        MakeSeriesNeonTestUtils.AssertLongSeries(row, "c", 1, 0, 2);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "s", 10, 0, 50);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "X", 0, 2, 4);
    }

    [Test]
    [Description("make-series - unaligned from and unaligned to on datetime axis includes rows in the final bin whose start is < to")]
    public void MakeSeries_UnalignedFromAndTo_DateTimeAxis()
    {
        Table table = MakeSeriesNeonTestUtils.CreateDateTimeIntTable(
            [
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 10, 15), 1),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 12, 30), 2),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 12, 50), 4),   // bin_at(12:50, 1h, 10:15) = 12:15 < 12:45 -> included
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 13, 15), 8)    // bin_at(13:15, 1h, 10:15) = 13:15 >= 12:45 -> excluded
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            """
            Events
            | make-series s=sum(Val) on Ts
              from datetime(2025-01-01T10:15:00Z) to datetime(2025-01-01T12:45:00Z) step 1h
            """);

        MakeSeriesNeonTestUtils.AssertLongSeries(row, "s", 1, 0, 6);
        MakeSeriesNeonTestUtils.AssertDateTimeSeries(
            row,
            "Ts",
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 10, 15),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 11, 15),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 12, 15));
    }

    [Test]
    [Description("make-series - from equal to to returns empty result set (0 rows)")]
    public void MakeSeries_FromEqualsTo_ReturnsZeroRows()
    {
        Table table = MakeSeriesNeonTestUtils.CreateLongValueTable([(5, 10), (6, 20)]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        MakeSeriesNeonTestUtils.ExecuteAndAssertEmpty(
            db,
            "Events | make-series s=sum(Val) on X from 5 to 5 step 1");
    }

    [Test]
    [Description("make-series - from greater than to returns empty result set (0 rows)")]
    public void MakeSeries_FromGreaterThanTo_ReturnsZeroRows()
    {
        Table table = MakeSeriesNeonTestUtils.CreateLongValueTable([(2, 10), (3, 20), (4, 30), (5, 40)]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        MakeSeriesNeonTestUtils.ExecuteAndAssertEmpty(
            db,
            "Events | make-series s=sum(Val) on X from 5 to 2 step 1");
    }

    #endregion

    #region Omitted From and/or To Bounds

    [Test]
    [Description("make-series - both from and to omitted derives inclusive [bin(min, step), bin(max, step)] range on datetime axis")]
    public void MakeSeries_BothFromAndToOmitted_DateTimeAxis_NoBy()
    {
        Table table = MakeSeriesNeonTestUtils.CreateDateTimeIntTable(
            [
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 10, 20), 10),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 12, 55), 30)
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            "Events | make-series s=sum(Val) on Ts step 1h");

        MakeSeriesNeonTestUtils.AssertLongSeries(row, "s", 10, 0, 30);
        MakeSeriesNeonTestUtils.AssertDateTimeSeries(
            row,
            "Ts",
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 10),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 11),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 12));
    }

    [Test]
    [Description("make-series - both from and to omitted on negative and positive long axis aligns with floor bin(x, step)")]
    public void MakeSeries_BothFromAndToOmitted_LongAxis_NegativeToPositive()
    {
        Table table = MakeSeriesNeonTestUtils.CreateLongValueTable(
            [
                (-5, 1),   // bin(-5, 2) = -6
                (-1, 2),   // bin(-1, 2) = -2
                (3, 4)     // bin(3, 2) = 2
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            "Events | make-series s=sum(Val) on X step 2");

        MakeSeriesNeonTestUtils.AssertLongSeries(row, "s", 1, 0, 2, 0, 4);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "X", -6, -4, -2, 0, 2);
    }

    [Test]
    [Description("make-series - both from and to omitted with single input row produces length-1 binned axis")]
    public void MakeSeries_BothFromAndToOmitted_SingleRow()
    {
        Table table = MakeSeriesNeonTestUtils.CreateDateTimeIntTable(
            [(MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 14, 37), 42)]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            "Events | make-series s=sum(Val) on Ts step 1h");

        MakeSeriesNeonTestUtils.AssertLongSeries(row, "s", 42);
        MakeSeriesNeonTestUtils.AssertDateTimeSeries(row, "Ts", MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 14));
    }

    [Test]
    [Description("make-series - both from and to omitted with by computes independent per-series min/max axis bounds for each group")]
    public void MakeSeries_BothFromAndToOmitted_WithBy_PerSeriesBounds()
    {
        // Oracle-verified against ADX: each group gets its own [bin(min_in_group, step), bin(max_in_group, step)].
        Table table = MakeSeriesNeonTestUtils.CreateKeyedDateTimeTable(
            [
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 10, 15), "A", 1),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 12, 45), "A", 2),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 11, 30), "B", 10),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 14, 5), "B", 20)
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = MakeSeriesNeonTestUtils.ExecuteAndAssertSuccess(
            db,
            "Events | make-series s=sum(Val) on Ts step 1h by Grp");

        Assert.That(rows, Has.Count.EqualTo(2));

        IReadOnlyDictionary<string, object?> rowA = MakeSeriesNeonTestUtils.FindRowByKey(rows, "Grp", "A");
        MakeSeriesNeonTestUtils.AssertLongSeries(rowA, "s", 1, 0, 2);
        MakeSeriesNeonTestUtils.AssertDateTimeSeries(
            rowA,
            "Ts",
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 10),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 11),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 12));

        IReadOnlyDictionary<string, object?> rowB = MakeSeriesNeonTestUtils.FindRowByKey(rows, "Grp", "B");
        MakeSeriesNeonTestUtils.AssertLongSeries(rowB, "s", 10, 0, 0, 20);
        MakeSeriesNeonTestUtils.AssertDateTimeSeries(
            rowB,
            "Ts",
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 11),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 12),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 13),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 14));
    }

    [Test]
    [Description("make-series - only to omitted anchors at from and ends each group at its own max bin inclusively")]
    public void MakeSeries_OnlyToOmitted_AnchoredAtFrom_PerSeriesEnd()
    {
        // Oracle-verified against ADX: from=10:15 anchors bins at :15; Group A ends at 12:15, Group B starts at 10:15 (padded) and ends at 13:15.
        Table table = MakeSeriesNeonTestUtils.CreateKeyedDateTimeTable(
            [
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 9, 50), "A", 999),  // < from -> excluded
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 10, 15), "A", 1),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 12, 45), "A", 2),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 11, 30), "B", 10),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 14, 5), "B", 20)
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = MakeSeriesNeonTestUtils.ExecuteAndAssertSuccess(
            db,
            """
            Events
            | make-series s=sum(Val) default=-1 on Ts
              from datetime(2025-01-01T10:15:00Z) step 1h by Grp
            """);

        Assert.That(rows, Has.Count.EqualTo(2));

        IReadOnlyDictionary<string, object?> rowA = MakeSeriesNeonTestUtils.FindRowByKey(rows, "Grp", "A");
        MakeSeriesNeonTestUtils.AssertLongSeries(rowA, "s", 1, -1, 2);
        MakeSeriesNeonTestUtils.AssertDateTimeSeries(
            rowA,
            "Ts",
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 10, 15),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 11, 15),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 12, 15));

        IReadOnlyDictionary<string, object?> rowB = MakeSeriesNeonTestUtils.FindRowByKey(rows, "Grp", "B");
        MakeSeriesNeonTestUtils.AssertLongSeries(rowB, "s", -1, 10, -1, 20);
        MakeSeriesNeonTestUtils.AssertDateTimeSeries(
            rowB,
            "Ts",
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 10, 15),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 11, 15),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 12, 15),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 13, 15));
    }

    [Test]
    [Description("make-series - only to omitted returns 0 rows when all rows are strictly before from")]
    public void MakeSeries_OnlyToOmitted_AllRowsBeforeFrom_ReturnsZeroRows()
    {
        Table table = MakeSeriesNeonTestUtils.CreateLongValueTable([(0, 10), (2, 20), (4, 30)]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        MakeSeriesNeonTestUtils.ExecuteAndAssertEmpty(
            db,
            "Events | make-series s=sum(Val) on X from 10 step 2");
    }

    [Test]
    [Description("make-series - only from omitted with aligned to includes bins up to aligned to and filters rows above to")]
    public void MakeSeries_OnlyFromOmitted_AlignedTo_LongAndDateTime()
    {
        // Oracle-verified against ADX: when from is omitted and to is aligned to step (e.g., to=14 step=2 or to=15:00 step=1h),
        // the axis runs from bin(min, step) through to inclusive.
        Table longTable = MakeSeriesNeonTestUtils.CreateLongValueTable(
            [
                (10, 1),
                (12, 2),
                (14, 3),
                (16, 99)
            ],
            tableName: "LongEvents");

        Table dtTable = MakeSeriesNeonTestUtils.CreateDateTimeIntTable(
            [
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 10, 15), 1),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 12, 45), 2),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 14, 15), 3)
            ],
            tableName: "DtEvents");

        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(longTable, dtTable);

        IReadOnlyDictionary<string, object?> longRow = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            "LongEvents | make-series s=sum(Val) on X to 14 step 2");

        MakeSeriesNeonTestUtils.AssertLongSeries(longRow, "s", 1, 2, 3);
        MakeSeriesNeonTestUtils.AssertLongSeries(longRow, "X", 10, 12, 14);

        IReadOnlyDictionary<string, object?> dtRow = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            """
            DtEvents
            | make-series s=sum(Val) on Ts to datetime(2025-01-01T15:00:00Z) step 1h
            """);

        MakeSeriesNeonTestUtils.AssertLongSeries(dtRow, "s", 1, 0, 2, 0, 3, 0);
        MakeSeriesNeonTestUtils.AssertDateTimeSeries(
            dtRow,
            "Ts",
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 10),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 11),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 12),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 13),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 14),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 15));
    }

    [Test]
    [Description("make-series - only from omitted with unaligned to anchors via bin_at(Axis, step, to) and ends at to - step")]
    public void MakeSeries_OnlyFromOmitted_UnalignedTo_AnchoredAtTo()
    {
        // Oracle-verified against ADX: to=15 step=2 anchors bins at odd numbers [9, 11, 13] and excludes x=15;
        // to=14:30 step=1h anchors bins at :30 ([09:30, 10:30, 11:30, 12:30, 13:30]) and excludes 14:30.
        Table longTable = MakeSeriesNeonTestUtils.CreateLongValueTable(
            [
                (10, 1),   // bin_at(10, 2, 15) = 9
                (12, 2),   // bin_at(12, 2, 15) = 11
                (14, 4),   // bin_at(14, 2, 15) = 13
                (15, 99)   // bin_at(15, 2, 15) = 15 >= 15 -> excluded
            ],
            tableName: "LongEvents");

        Table dtTable = MakeSeriesNeonTestUtils.CreateDateTimeIntTable(
            [
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 10, 15), 1),  // bin_at = 09:30
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 13, 45), 2),  // bin_at = 13:30
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 14, 30), 99)  // bin_at = 14:30 >= 14:30 -> excluded
            ],
            tableName: "DtEvents");

        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(longTable, dtTable);

        IReadOnlyDictionary<string, object?> longRow = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            "LongEvents | make-series s=sum(Val) on X to 15 step 2");

        MakeSeriesNeonTestUtils.AssertLongSeries(longRow, "s", 1, 2, 4);
        MakeSeriesNeonTestUtils.AssertLongSeries(longRow, "X", 9, 11, 13);

        IReadOnlyDictionary<string, object?> dtRow = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            """
            DtEvents
            | make-series s=sum(Val) on Ts to datetime(2025-01-01T14:30:00Z) step 1h
            """);

        MakeSeriesNeonTestUtils.AssertLongSeries(dtRow, "s", 1, 0, 0, 0, 2);
        MakeSeriesNeonTestUtils.AssertDateTimeSeries(
            dtRow,
            "Ts",
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 9, 30),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 10, 30),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 11, 30),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 12, 30),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 13, 30));
    }

    [Test]
    [Description("make-series - only from omitted with by computes independent per-series start bin up to shared to")]
    public void MakeSeries_OnlyFromOmitted_WithBy_PerSeriesStart()
    {
        Table table = MakeSeriesNeonTestUtils.CreateKeyedDateTimeTable(
            [
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 10, 15), "A", 1),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 12, 15), "A", 2),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 12, 30), "B", 10)
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = MakeSeriesNeonTestUtils.ExecuteAndAssertSuccess(
            db,
            """
            Events
            | make-series s=sum(Val) on Ts to datetime(2025-01-01T13:30:00Z) step 1h by Grp
            """);

        Assert.That(rows, Has.Count.EqualTo(2));

        IReadOnlyDictionary<string, object?> rowA = MakeSeriesNeonTestUtils.FindRowByKey(rows, "Grp", "A");
        MakeSeriesNeonTestUtils.AssertLongSeries(rowA, "s", 1, 0, 2, 0);
        MakeSeriesNeonTestUtils.AssertDateTimeSeries(
            rowA,
            "Ts",
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 9, 30),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 10, 30),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 11, 30),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 12, 30));

        IReadOnlyDictionary<string, object?> rowB = MakeSeriesNeonTestUtils.FindRowByKey(rows, "Grp", "B");
        MakeSeriesNeonTestUtils.AssertLongSeries(rowB, "s", 10);
        MakeSeriesNeonTestUtils.AssertDateTimeSeries(
            rowB,
            "Ts",
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 12, 30));
    }

    [Test]
    [Description("make-series - only from omitted returns 0 rows when all rows are at or above unaligned to")]
    public void MakeSeries_OnlyFromOmitted_AllRowsAtOrAboveTo_ReturnsZeroRows()
    {
        Table table = MakeSeriesNeonTestUtils.CreateLongValueTable([(15, 1), (20, 2)]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        MakeSeriesNeonTestUtils.ExecuteAndAssertEmpty(
            db,
            "Events | make-series s=sum(Val) on X to 15 step 2");
    }

    #endregion

    #region Alternate "in range(start, stop, step)" Syntax

    [Test]
    [Description("make-series - in range(start, stop, step) on aligned datetime bounds includes the stop bin inclusively")]
    public void MakeSeries_InRangeSyntax_AlignedDateTime_IncludesStopBin()
    {
        Table table = MakeSeriesNeonTestUtils.CreateDateTimeIntTable(
            [
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0, 15), 10),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 2, 30), 30),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 3, 0), 99)   // bin 03:00 > stop 02:00 -> excluded
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            """
            Events
            | make-series s=sum(Val) on Ts
              in range(datetime(2025-01-01T00:00:00Z), datetime(2025-01-01T02:00:00Z), 1h)
            """);

        MakeSeriesNeonTestUtils.AssertLongSeries(row, "s", 10, 0, 30);
        MakeSeriesNeonTestUtils.AssertDateTimeSeries(
            row,
            "Ts",
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 1),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 2));
    }

    [Test]
    [Description("make-series - in range(start, stop, step) uses bin(Axis, step) and spans [bin(start, step), bin(stop, step)] inclusively even for unaligned bounds")]
    public void MakeSeries_InRangeSyntax_UnalignedBounds_UsesBinAlignment()
    {
        // Oracle-verified against ADX: in range(1, 10, 3) bins via bin(X, 3) across [0, 3, 6, 9];
        // x=0 (< 1) falls into bin 0 and x=11 (> 10) falls into bin 9, so both are included.
        Table longTable = MakeSeriesNeonTestUtils.CreateLongValueTable(
            [
                (-1, 100), // bin(-1, 3) = -3 -> excluded
                (0, 1),    // bin 0 -> included
                (1, 1),    // bin 0 -> included
                (2, 1),    // bin 0 -> included
                (3, 1),    // bin 3 -> included
                (4, 1),    // bin 3 -> included
                (9, 1),    // bin 9 -> included
                (10, 1),   // bin 9 -> included
                (11, 1),   // bin 9 -> included
                (12, 100)  // bin 12 -> excluded
            ],
            tableName: "LongEvents");

        Table dtTable = MakeSeriesNeonTestUtils.CreateDateTimeIntTable(
            [
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0, 0), 1),   // < 00:15 but bin = 00:00 -> included
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0, 15), 2),  // bin = 00:00 -> included
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 2, 30), 3)   // > 02:15 but bin = 02:00 -> included
            ],
            tableName: "DtEvents");

        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(longTable, dtTable);

        IReadOnlyDictionary<string, object?> longRow = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            "LongEvents | make-series c=count() on X in range(1, 10, 3)");

        MakeSeriesNeonTestUtils.AssertLongSeries(longRow, "c", 3, 2, 0, 3);
        MakeSeriesNeonTestUtils.AssertLongSeries(longRow, "X", 0, 3, 6, 9);

        IReadOnlyDictionary<string, object?> dtRow = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            """
            DtEvents
            | make-series s=sum(Val) on Ts
              in range(datetime(2025-01-01T00:15:00Z), datetime(2025-01-01T02:15:00Z), 1h)
            """);

        MakeSeriesNeonTestUtils.AssertLongSeries(dtRow, "s", 3, 0, 3);
        MakeSeriesNeonTestUtils.AssertDateTimeSeries(
            dtRow,
            "Ts",
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 1),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 2));
    }

    [Test]
    [Description("make-series - in range(start, stop, step) combined with by, default, and multiple aggregations")]
    public void MakeSeries_InRangeSyntax_WithByAndDefault()
    {
        Table table = MakeSeriesNeonTestUtils.CreateKeyedDateTimeTable(
            [
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0, 10), "A", 10),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 2, 10), "A", 30),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 1, 20), "B", 20)
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = MakeSeriesNeonTestUtils.ExecuteAndAssertSuccess(
            db,
            """
            Events
            | make-series total=sum(Val) default=-1, cnt=count() on Ts
              in range(datetime(2025-01-01T00:00:00Z), datetime(2025-01-01T02:00:00Z), 1h)
              by Grp
            """);

        Assert.That(rows, Has.Count.EqualTo(2));

        IReadOnlyDictionary<string, object?> rowA = MakeSeriesNeonTestUtils.FindRowByKey(rows, "Grp", "A");
        MakeSeriesNeonTestUtils.AssertLongSeries(rowA, "total", 10, -1, 30);
        MakeSeriesNeonTestUtils.AssertLongSeries(rowA, "cnt", 1, 0, 1);

        IReadOnlyDictionary<string, object?> rowB = MakeSeriesNeonTestUtils.FindRowByKey(rows, "Grp", "B");
        MakeSeriesNeonTestUtils.AssertLongSeries(rowB, "total", -1, 20, -1);
        MakeSeriesNeonTestUtils.AssertLongSeries(rowB, "cnt", 0, 1, 0);
    }

    [Test]
    [Description("make-series - in range(start, stop, step) with start == stop produces a single bin when data matches, and start > stop produces 0 rows")]
    public void MakeSeries_InRangeSyntax_StartEqualsOrGreaterThanStop()
    {
        Table table = MakeSeriesNeonTestUtils.CreateLongValueTable([(0, 10), (2, 20), (3, 30)]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyDictionary<string, object?> singleBinRow = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            "Events | make-series s=sum(Val) on X in range(2, 2, 2)");

        MakeSeriesNeonTestUtils.AssertLongSeries(singleBinRow, "s", 50);
        MakeSeriesNeonTestUtils.AssertLongSeries(singleBinRow, "X", 2);

        MakeSeriesNeonTestUtils.ExecuteAndAssertEmpty(
            db,
            "Events | make-series s=sum(Val) on X in range(4, 0, 2)");
    }

    #endregion

    #region All Supported Axis Data Types

    [Test]
    [Description("make-series - long axis supports negative unaligned from spanning across zero")]
    public void MakeSeries_LongAxis_NegativeUnalignedSpanningZero()
    {
        Table table = MakeSeriesNeonTestUtils.CreateLongValueTable(
            [
                (-6, 99),  // < -5 -> excluded
                (-5, 1),
                (-4, 2),
                (-3, 4),
                (-2, 8),
                (-1, 16),
                (0, 32),
                (1, 99)    // exact to -> excluded
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            "Events | make-series c=count(), s=sum(Val) on X from -5 to 1 step 2");

        MakeSeriesNeonTestUtils.AssertLongSeries(row, "c", 2, 2, 2);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "s", 3, 12, 48);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "X", -5, -3, -1);
    }

    [Test]
    [Description("make-series - int axis column is supported with int bounds and step")]
    public void MakeSeries_IntAxis_ExplicitBoundsAndStep()
    {
        // Oracle-verified against ADX: int axis columns execute cleanly even though Kusto.Language may emit a client-only diagnostic.
        Table table = MakeSeriesNeonTestUtils.CreateTable(
            "IntEvents",
            [
                MakeSeriesNeonTestUtils.CreateColumn("X", typeof(int), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Val", typeof(int), isNullable: true)
            ],
            [
                new Dictionary<string, object?> { { "X", 0 }, { "Val", 1 } },
                new Dictionary<string, object?> { { "X", 2 }, { "Val", 2 } },
                new Dictionary<string, object?> { { "X", 3 }, { "Val", 3 } }
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            "IntEvents | make-series s=sum(Val) on X from 0 to 5 step 2");

        MakeSeriesNeonTestUtils.AssertLongSeries(row, "s", 1, 5, 0);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "X", 0, 2, 4);

        // Oracle-verified against ADX: omitted bounds on an int axis work with an int step.
        IReadOnlyDictionary<string, object?> omittedBoundsRow = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            "IntEvents | make-series s=sum(Val) on X step int(2)");

        MakeSeriesNeonTestUtils.AssertLongSeries(omittedBoundsRow, "s", 1, 5);
        MakeSeriesNeonTestUtils.AssertLongSeries(omittedBoundsRow, "X", 0, 2);
    }

    [Test]
    [Description("make-series - real (double) axis supports fractional and negative bounds and step")]
    public void MakeSeries_RealAxis_FractionalAndNegativeRange()
    {
        Table table = MakeSeriesNeonTestUtils.CreateTable(
            "RealEvents",
            [
                MakeSeriesNeonTestUtils.CreateColumn("Ax", typeof(double), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Val", typeof(double), isNullable: true)
            ],
            [
                new Dictionary<string, object?> { { "Ax", 0.1 }, { "Val", 1.5 } },
                new Dictionary<string, object?> { { "Ax", 0.49 }, { "Val", 0.5 } },
                new Dictionary<string, object?> { { "Ax", 1.2 }, { "Val", 3.0 } },
                new Dictionary<string, object?> { { "Ax", 2.0 }, { "Val", 99.0 } }
            ]);

        Table negativeRealTable = MakeSeriesNeonTestUtils.CreateTable(
            "NegRealEvents",
            [
                MakeSeriesNeonTestUtils.CreateColumn("Ax", typeof(double), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Val", typeof(double), isNullable: true)
            ],
            [
                new Dictionary<string, object?> { { "Ax", -1.2 }, { "Val", 10.0 } },
                new Dictionary<string, object?> { { "Ax", 0.7 }, { "Val", 20.0 } }
            ]);

        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table, negativeRealTable);

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            "RealEvents | make-series s=sum(Val) on Ax from 0.0 to 2.0 step 0.5");

        MakeSeriesNeonTestUtils.AssertDoubleSeries(row, "s", 2.0, 0.0, 3.0, 0.0);
        MakeSeriesNeonTestUtils.AssertDoubleSeries(row, "Ax", 0.0, 0.5, 1.0, 1.5);

        IReadOnlyDictionary<string, object?> negRow = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            "NegRealEvents | make-series s=sum(Val) on Ax from -1.5 to 1.5 step 1.0");

        MakeSeriesNeonTestUtils.AssertDoubleSeries(negRow, "s", 10.0, 0.0, 20.0);
        MakeSeriesNeonTestUtils.AssertDoubleSeries(negRow, "Ax", -1.5, -0.5, 0.5);
    }

    [Test]
    [Description("make-series - decimal axis supports decimal bounds and fractional decimal step")]
    public void MakeSeries_DecimalAxis_ExplicitBoundsAndStep()
    {
        Table table = MakeSeriesNeonTestUtils.CreateTable(
            "DecimalEvents",
            [
                MakeSeriesNeonTestUtils.CreateColumn("Ax", typeof(decimal), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Val", typeof(decimal), isNullable: true)
            ],
            [
                new Dictionary<string, object?> { { "Ax", 0.6m }, { "Val", 1.5m } },
                new Dictionary<string, object?> { { "Ax", 1.7m }, { "Val", 2.5m } }
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            "DecimalEvents | make-series s=sum(Val) on Ax from decimal(0) to decimal(2) step decimal(0.5)");

        MakeSeriesNeonTestUtils.AssertDecimalSeries(row, "s", 0m, 1.5m, 0m, 2.5m);
        MakeSeriesNeonTestUtils.AssertDecimalSeries(row, "Ax", 0m, 0.5m, 1.0m, 1.5m);
    }

    [Test]
    [Description("make-series - timespan axis works with explicit positive from/to/step, omitted bounds, and in range")]
    public void MakeSeries_TimeSpanAxis_ExplicitOmittedAndInRange()
    {
        Table table = MakeSeriesNeonTestUtils.CreateTable(
            "SpanEvents",
            [
                MakeSeriesNeonTestUtils.CreateColumn("Elapsed", typeof(TimeSpan), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Val", typeof(int), isNullable: true)
            ],
            [
                new Dictionary<string, object?> { { "Elapsed", TimeSpan.FromMinutes(0) }, { "Val", 1 } },
                new Dictionary<string, object?> { { "Elapsed", TimeSpan.FromMinutes(20) }, { "Val", 2 } },
                new Dictionary<string, object?> { { "Elapsed", TimeSpan.FromMinutes(46) }, { "Val", 4 } }
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        // Explicit positive from (1m..1h step 15m -> [1m, 16m, 31m, 46m])
        IReadOnlyDictionary<string, object?> explicitRow = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            "SpanEvents | make-series s=sum(Val) on Elapsed from 1m to 1h step 15m");

        MakeSeriesNeonTestUtils.AssertLongSeries(explicitRow, "s", 0, 2, 0, 4);
        MakeSeriesNeonTestUtils.AssertTimeSpanSeries(
            explicitRow,
            "Elapsed",
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(16),
            TimeSpan.FromMinutes(31),
            TimeSpan.FromMinutes(46));

        // Omitted bounds (starts at bin(0m, 15m) = 0m through bin(46m, 15m) = 45m)
        IReadOnlyDictionary<string, object?> omittedRow = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            "SpanEvents | make-series s=sum(Val) on Elapsed step 15m");

        MakeSeriesNeonTestUtils.AssertLongSeries(omittedRow, "s", 1, 2, 0, 4);
        MakeSeriesNeonTestUtils.AssertTimeSpanSeries(
            omittedRow,
            "Elapsed",
            TimeSpan.FromMinutes(0),
            TimeSpan.FromMinutes(15),
            TimeSpan.FromMinutes(30),
            TimeSpan.FromMinutes(45));

        // in range(15m, 45m, 15m) -> [15m, 30m, 45m]
        IReadOnlyDictionary<string, object?> inRangeRow = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            "SpanEvents | make-series s=sum(Val) on Elapsed in range(15m, 45m, 15m)");

        MakeSeriesNeonTestUtils.AssertLongSeries(inRangeRow, "s", 2, 0, 4);
        MakeSeriesNeonTestUtils.AssertTimeSpanSeries(
            inRangeRow,
            "Elapsed",
            TimeSpan.FromMinutes(15),
            TimeSpan.FromMinutes(30),
            TimeSpan.FromMinutes(45));
    }

    #endregion

    #region Dynamic Bounds via let, Arithmetic Expressions, and toscalar

    [Test]
    [Description("make-series - from, to, and step accept let variables and constant scalar arithmetic expressions")]
    public void MakeSeries_BoundsAndStep_FromLetAndConstantExpressions()
    {
        Table table = MakeSeriesNeonTestUtils.CreateDateTimeIntTable(
            [
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 2, 10), 10),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 4, 20), 20)
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            """
            let start = datetime(2025-01-01T02:00:00Z);
            let binSize = 30m * 2;
            Events
            | make-series s=sum(Val) on Ts from start to start + 3h step binSize
            """);

        MakeSeriesNeonTestUtils.AssertLongSeries(row, "s", 10, 0, 20);
        MakeSeriesNeonTestUtils.AssertDateTimeSeries(
            row,
            "Ts",
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 2),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 3),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 4));
    }

    [Test]
    [Description("make-series - from and to accept toscalar() expressions computed from the dataset")]
    public void MakeSeries_BoundsFromToScalar()
    {
        Table table = MakeSeriesNeonTestUtils.CreateLongValueTable(
            [
                (10, 5),
                (12, 15),
                (13, 25)
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            """
            let minX = toscalar(Events | summarize min(X));
            let maxX = toscalar(Events | summarize max(X)) + 1;
            Events
            | make-series s=sum(Val) on X from minX to maxX step 1
            """);

        MakeSeriesNeonTestUtils.AssertLongSeries(row, "s", 5, 0, 15, 25);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "X", 10, 11, 12, 13);
    }

    [Test]
    [Description("make-series - compatible numeric promotions between axis column and from/to/step literals (real axis with int bounds)")]
    public void MakeSeries_CompatibleNumericPromotion_RealAxisWithIntBounds()
    {
        Table table = MakeSeriesNeonTestUtils.CreateTable(
            "RealEvents",
            [
                MakeSeriesNeonTestUtils.CreateColumn("Ax", typeof(double), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Val", typeof(int), isNullable: true)
            ],
            [
                new Dictionary<string, object?> { { "Ax", 0.25 }, { "Val", 10 } },
                new Dictionary<string, object?> { { "Ax", 1.75 }, { "Val", 20 } }
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            "RealEvents | make-series s=sum(Val) on Ax from 0 to 2 step 0.5");

        MakeSeriesNeonTestUtils.AssertLongSeries(row, "s", 10, 0, 0, 20);
        MakeSeriesNeonTestUtils.AssertDoubleSeries(row, "Ax", 0.0, 0.5, 1.0, 1.5);
    }

    #endregion
}
