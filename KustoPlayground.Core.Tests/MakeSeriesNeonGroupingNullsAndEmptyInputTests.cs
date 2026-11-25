namespace KustoPlayground.Core.Tests;

public class MakeSeriesNeonGroupingNullsAndEmptyInputTests
{
    #region Grouping with by Clause

    [Test]
    [Description("make-series - single string by column creates one row per group with full shared explicit axis padded with defaults")]
    public void MakeSeries_SingleByColumn_PadsEachGroupToFullRange()
    {
        Table table = MakeSeriesNeonTestUtils.CreateKeyedDateTimeTable(
            [
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0, 10), "A", 10),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0, 50), "A", 5),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 2, 0), "A", 30),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 1, 15), "B", 20),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 3, 45), "C", 40)
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = MakeSeriesNeonTestUtils.ExecuteAndAssertSuccess(
            db,
            """
            Events
            | make-series s=sum(Val) default=-1 on Ts
              from datetime(2025-01-01T00:00:00Z) to datetime(2025-01-01T04:00:00Z) step 1h
              by Grp
            """);

        Assert.That(rows, Has.Count.EqualTo(3));

        DateTime[] expectedAxis =
        [
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 1),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 2),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 3)
        ];

        IReadOnlyDictionary<string, object?> rowA = MakeSeriesNeonTestUtils.FindRowByKey(rows, "Grp", "A");
        MakeSeriesNeonTestUtils.AssertColumnOrder(rowA, "Grp", "s", "Ts");
        MakeSeriesNeonTestUtils.AssertLongSeries(rowA, "s", 15, -1, 30, -1);
        MakeSeriesNeonTestUtils.AssertDateTimeSeries(rowA, "Ts", expectedAxis[0], expectedAxis[1], expectedAxis[2], expectedAxis[3]);

        IReadOnlyDictionary<string, object?> rowB = MakeSeriesNeonTestUtils.FindRowByKey(rows, "Grp", "B");
        MakeSeriesNeonTestUtils.AssertLongSeries(rowB, "s", -1, 20, -1, -1);
        MakeSeriesNeonTestUtils.AssertDateTimeSeries(rowB, "Ts", expectedAxis[0], expectedAxis[1], expectedAxis[2], expectedAxis[3]);

        IReadOnlyDictionary<string, object?> rowC = MakeSeriesNeonTestUtils.FindRowByKey(rows, "Grp", "C");
        MakeSeriesNeonTestUtils.AssertLongSeries(rowC, "s", -1, -1, -1, 40);
        MakeSeriesNeonTestUtils.AssertDateTimeSeries(rowC, "Ts", expectedAxis[0], expectedAxis[1], expectedAxis[2], expectedAxis[3]);
    }

    [Test]
    [Description("make-series - multiple by columns emit only observed key combinations, never unobserved Cartesian pairs")]
    public void MakeSeries_MultipleByColumns_OnlyObservedCombinations()
    {
        Table table = MakeSeriesNeonTestUtils.CreateTable(
            "Events",
            [
                MakeSeriesNeonTestUtils.CreateColumn("X", typeof(long), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Region", typeof(string), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Service", typeof(string), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Val", typeof(int), isNullable: true)
            ],
            [
                new Dictionary<string, object?> { { "X", 0L }, { "Region", "us" }, { "Service", "api" }, { "Val", 10 } },
                new Dictionary<string, object?> { { "X", 2L }, { "Region", "us" }, { "Service", "api" }, { "Val", 20 } },
                new Dictionary<string, object?> { { "X", 1L }, { "Region", "eu" }, { "Service", "web" }, { "Val", 30 } }
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = MakeSeriesNeonTestUtils.ExecuteAndAssertSuccess(
            db,
            "Events | make-series s=sum(Val) on X from 0 to 3 step 1 by Region, Service");

        // ("us","web") and ("eu","api") never occur -> only 2 rows
        Assert.That(rows, Has.Count.EqualTo(2));

        IReadOnlyDictionary<string, object?> usApi = MakeSeriesNeonTestUtils.FindRowByKeys(rows, ("Region", "us"), ("Service", "api"));
        MakeSeriesNeonTestUtils.AssertColumnOrder(usApi, "Region", "Service", "s", "X");
        MakeSeriesNeonTestUtils.AssertLongSeries(usApi, "s", 10, 0, 20);

        IReadOnlyDictionary<string, object?> euWeb = MakeSeriesNeonTestUtils.FindRowByKeys(rows, ("Region", "eu"), ("Service", "web"));
        MakeSeriesNeonTestUtils.AssertLongSeries(euWeb, "s", 0, 30, 0);
    }

    [Test]
    [Description("make-series - by supports aliased expressions (tolower, bin) and unnamed expression keys")]
    public void MakeSeries_ByWithExpressionsAndAliases()
    {
        Table table = MakeSeriesNeonTestUtils.CreateTable(
            "Events",
            [
                MakeSeriesNeonTestUtils.CreateColumn("X", typeof(long), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Grp", typeof(string), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Score", typeof(int), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Val", typeof(int), isNullable: true)
            ],
            [
                new Dictionary<string, object?> { { "X", 0L }, { "Grp", "Alpha" }, { "Score", 12 }, { "Val", 10 } },
                new Dictionary<string, object?> { { "X", 1L }, { "Grp", "ALPHA" }, { "Score", 19 }, { "Val", 20 } },
                new Dictionary<string, object?> { { "X", 2L }, { "Grp", "Beta" }, { "Score", 25 }, { "Val", 30 } }
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = MakeSeriesNeonTestUtils.ExecuteAndAssertSuccess(
            db,
            """
            Events
            | make-series s=sum(Val) on X from 0 to 3 step 1
              by NormGrp=tolower(Grp), ScoreBucket=bin(Score, 10)
            """);

        Assert.That(rows, Has.Count.EqualTo(2));

        IReadOnlyDictionary<string, object?> alphaRow = MakeSeriesNeonTestUtils.FindRowByKeys(rows, ("NormGrp", "alpha"), ("ScoreBucket", 10));
        MakeSeriesNeonTestUtils.AssertColumnOrder(alphaRow, "NormGrp", "ScoreBucket", "s", "X");
        MakeSeriesNeonTestUtils.AssertLongSeries(alphaRow, "s", 10, 20, 0);

        IReadOnlyDictionary<string, object?> betaRow = MakeSeriesNeonTestUtils.FindRowByKeys(rows, ("NormGrp", "beta"), ("ScoreBucket", 20));
        MakeSeriesNeonTestUtils.AssertLongSeries(betaRow, "s", 0, 0, 30);
    }

    [Test]
    [Description("make-series - string by keys are case-sensitive and empty string is a valid distinct group key")]
    public void MakeSeries_ByStringKeys_CaseSensitiveAndEmptyString()
    {
        Table table = MakeSeriesNeonTestUtils.CreateTable(
            "Events",
            [
                MakeSeriesNeonTestUtils.CreateColumn("X", typeof(long), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Grp", typeof(string), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Val", typeof(int), isNullable: true)
            ],
            [
                new Dictionary<string, object?> { { "X", 0L }, { "Grp", "A" }, { "Val", 1 } },
                new Dictionary<string, object?> { { "X", 0L }, { "Grp", "a" }, { "Val", 2 } },
                new Dictionary<string, object?> { { "X", 0L }, { "Grp", "" }, { "Val", 4 } },
                new Dictionary<string, object?> { { "X", 1L }, { "Grp", "" }, { "Val", 8 } }
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = MakeSeriesNeonTestUtils.ExecuteAndAssertSuccess(
            db,
            "Events | make-series s=sum(Val) on X from 0 to 2 step 1 by Grp");

        Assert.That(rows, Has.Count.EqualTo(3));
        MakeSeriesNeonTestUtils.AssertLongSeries(MakeSeriesNeonTestUtils.FindRowByKey(rows, "Grp", "A"), "s", 1, 0);
        MakeSeriesNeonTestUtils.AssertLongSeries(MakeSeriesNeonTestUtils.FindRowByKey(rows, "Grp", "a"), "s", 2, 0);
        MakeSeriesNeonTestUtils.AssertLongSeries(MakeSeriesNeonTestUtils.FindRowByKey(rows, "Grp", ""), "s", 4, 8);
    }

    [Test]
    [Description("make-series - by supports bool, int, long, real, decimal, datetime, timespan, and guid scalar keys including null key groups")]
    public void MakeSeries_ByAllScalarTypes_IncludingNullGroups()
    {
        Guid g1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
        DateTime dt1 = MakeSeriesNeonTestUtils.Utc(2025, 6, 1, 12, 0);
        TimeSpan sp1 = TimeSpan.FromMinutes(30);

        Table table = MakeSeriesNeonTestUtils.CreateTable(
            "Events",
            [
                MakeSeriesNeonTestUtils.CreateColumn("X", typeof(long), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Flag", typeof(bool), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Num", typeof(int), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("BigNum", typeof(long), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Ratio", typeof(double), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Money", typeof(decimal), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("TagDt", typeof(DateTime), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("TagSpan", typeof(TimeSpan), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Id", typeof(Guid), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Val", typeof(int), isNullable: true)
            ],
            [
                new Dictionary<string, object?>
                {
                    { "X", 0L }, { "Flag", true }, { "Num", 1 }, { "BigNum", 1000L },
                    { "Ratio", 1.5 }, { "Money", 12.50m }, { "TagDt", dt1 }, { "TagSpan", sp1 },
                    { "Id", g1 }, { "Val", 10 }
                },
                new Dictionary<string, object?>
                {
                    { "X", 2L }, { "Flag", true }, { "Num", 1 }, { "BigNum", 1000L },
                    { "Ratio", 1.5 }, { "Money", 12.50m }, { "TagDt", dt1 }, { "TagSpan", sp1 },
                    { "Id", g1 }, { "Val", 20 }
                },
                new Dictionary<string, object?>
                {
                    { "X", 0L }, { "Flag", null }, { "Num", null }, { "BigNum", null },
                    { "Ratio", null }, { "Money", null }, { "TagDt", null }, { "TagSpan", null },
                    { "Id", null }, { "Val", 5 }
                },
                new Dictionary<string, object?>
                {
                    { "X", 1L }, { "Flag", null }, { "Num", null }, { "BigNum", null },
                    { "Ratio", null }, { "Money", null }, { "TagDt", null }, { "TagSpan", null },
                    { "Id", null }, { "Val", 15 }
                }
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = MakeSeriesNeonTestUtils.ExecuteAndAssertSuccess(
            db,
            """
            Events
            | make-series s=sum(Val) on X from 0 to 3 step 1
              by Flag, Num, BigNum, Ratio, Money, TagDt, TagSpan, Id
            """);

        Assert.That(rows, Has.Count.EqualTo(2));

        IReadOnlyDictionary<string, object?> nonNullRow = MakeSeriesNeonTestUtils.FindRowByKeys(
            rows,
            ("Flag", true),
            ("Num", 1),
            ("BigNum", 1000L),
            ("Ratio", 1.5),
            ("Money", 12.50m),
            ("TagDt", dt1),
            ("TagSpan", sp1),
            ("Id", g1));
        MakeSeriesNeonTestUtils.AssertLongSeries(nonNullRow, "s", 10, 0, 20);

        IReadOnlyDictionary<string, object?> nullRow = MakeSeriesNeonTestUtils.FindRowByKeys(
            rows,
            ("Flag", null),
            ("Num", null),
            ("BigNum", null),
            ("Ratio", null),
            ("Money", null),
            ("TagDt", null),
            ("TagSpan", null),
            ("Id", null));
        MakeSeriesNeonTestUtils.AssertLongSeries(nullRow, "s", 5, 15, 0);
    }

    [Test]
    [Description("make-series - multi-key by distinguishes partial nulls from non-nulls and merges identical partial-null keys")]
    public void MakeSeries_MultiKeyBy_PartialNullKeys()
    {
        Table table = MakeSeriesNeonTestUtils.CreateTable(
            "Events",
            [
                MakeSeriesNeonTestUtils.CreateColumn("X", typeof(long), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Grp", typeof(string), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Code", typeof(int), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Val", typeof(int), isNullable: true)
            ],
            [
                new Dictionary<string, object?> { { "X", 0L }, { "Grp", "A" }, { "Code", null }, { "Val", 10 } },
                new Dictionary<string, object?> { { "X", 1L }, { "Grp", "A" }, { "Code", null }, { "Val", 20 } },
                new Dictionary<string, object?> { { "X", 0L }, { "Grp", "A" }, { "Code", 1 }, { "Val", 30 } },
                new Dictionary<string, object?> { { "X", 1L }, { "Grp", "" }, { "Code", null }, { "Val", 40 } }
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = MakeSeriesNeonTestUtils.ExecuteAndAssertSuccess(
            db,
            "Events | make-series s=sum(Val) on X from 0 to 2 step 1 by Grp, Code");

        Assert.That(rows, Has.Count.EqualTo(3));
        MakeSeriesNeonTestUtils.AssertLongSeries(
            MakeSeriesNeonTestUtils.FindRowByKeys(rows, ("Grp", "A"), ("Code", null)),
            "s", 10, 20);
        MakeSeriesNeonTestUtils.AssertLongSeries(
            MakeSeriesNeonTestUtils.FindRowByKeys(rows, ("Grp", "A"), ("Code", 1)),
            "s", 30, 0);
        MakeSeriesNeonTestUtils.AssertLongSeries(
            MakeSeriesNeonTestUtils.FindRowByKeys(rows, ("Grp", ""), ("Code", null)),
            "s", 0, 40);
    }

    [Test]
    [Description("make-series - many groups (25) and many bins (100) maintain exact per-group and per-bin alignment without cross-group leakage")]
    public void MakeSeries_ManyGroupsAndManyBins_ScaleAndIsolation()
    {
        const int groupCount = 25;
        const int binCount = 100;

        var rows = new List<Dictionary<string, object?>>();
        for (int g = 0; g < groupCount; g++)
        {
            for (int b = g; b < binCount; b += groupCount)
            {
                rows.Add(new Dictionary<string, object?>
                {
                    { "X", (long)b },
                    { "Grp", $"G{g:D2}" },
                    { "Val", (g + 1) * 100 + b }
                });
            }
        }

        Table table = MakeSeriesNeonTestUtils.CreateTable(
            "Events",
            [
                MakeSeriesNeonTestUtils.CreateColumn("X", typeof(long), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Grp", typeof(string), isNullable: false),
                MakeSeriesNeonTestUtils.CreateColumn("Val", typeof(int), isNullable: false)
            ],
            rows,
            shuffle: true);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyList<IReadOnlyDictionary<string, object?>> resultRows = MakeSeriesNeonTestUtils.ExecuteAndAssertSuccess(
            db,
            $"Events | make-series s=sum(Val) default=-1 on X from 0 to {binCount} step 1 by Grp");

        Assert.That(resultRows, Has.Count.EqualTo(groupCount));
        long?[] expectedAxis = Enumerable.Range(0, binCount).Select(i => (long?)i).ToArray();

        for (int g = 0; g < groupCount; g++)
        {
            IReadOnlyDictionary<string, object?> groupRow = MakeSeriesNeonTestUtils.FindRowByKey(resultRows, "Grp", $"G{g:D2}");
            long?[] expectedSeries = Enumerable.Range(0, binCount)
                .Select(b => (b >= g && (b - g) % groupCount == 0) ? (long?)((g + 1) * 100 + b) : -1L)
                .ToArray();

            MakeSeriesNeonTestUtils.AssertLongSeries(groupRow, "s", expectedSeries);
            MakeSeriesNeonTestUtils.AssertLongSeries(groupRow, "X", expectedAxis);
        }
    }

    #endregion

    #region Null Axis Values and Null Metric Values

    [Test]
    [Description("make-series - rows with null axis values are silently dropped with explicit bounds, omitted bounds, and by grouping")]
    public void MakeSeries_NullAxisValues_SilentlyDropped()
    {
        Table table = MakeSeriesNeonTestUtils.CreateKeyedDateTimeTable(
            [
                (null, "A", 999),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0, 10), "A", 10),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 2, 10), "A", 20),
                (null, "NullOnlyGroup", 500)
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        // Explicit bounds, no by
        IReadOnlyDictionary<string, object?> explicitRow = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            """
            Events
            | make-series s=sum(Val), c=count() on Ts
              from datetime(2025-01-01T00:00:00Z) to datetime(2025-01-01T03:00:00Z) step 1h
            """);

        MakeSeriesNeonTestUtils.AssertLongSeries(explicitRow, "s", 10, 0, 20);
        MakeSeriesNeonTestUtils.AssertLongSeries(explicitRow, "c", 1, 0, 1);

        // Omitted bounds, no by
        IReadOnlyDictionary<string, object?> omittedRow = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            "Events | make-series s=sum(Val) on Ts step 1h");

        MakeSeriesNeonTestUtils.AssertLongSeries(omittedRow, "s", 10, 0, 20);

        // With by: group whose only rows have null axis produces NO row
        IReadOnlyList<IReadOnlyDictionary<string, object?>> groupedRows = MakeSeriesNeonTestUtils.ExecuteAndAssertSuccess(
            db,
            """
            Events
            | make-series s=sum(Val) on Ts
              from datetime(2025-01-01T00:00:00Z) to datetime(2025-01-01T03:00:00Z) step 1h
              by Grp
            """);

        Assert.That(groupedRows, Has.Count.EqualTo(1));
        Assert.That(groupedRows[0]["Grp"], Is.EqualTo("A"));
        MakeSeriesNeonTestUtils.AssertLongSeries(groupedRows[0], "s", 10, 0, 20);
    }

    [Test]
    [Description("make-series - when every row has a null axis value, returns 0 rows normally and 1 default-filled row with kind=nonempty")]
    public void MakeSeries_AllNullAxisValues_NormalVsKindNonEmpty()
    {
        Table table = MakeSeriesNeonTestUtils.CreateLongValueTable(
            [
                (null, 10),
                (null, 20)
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        MakeSeriesNeonTestUtils.ExecuteAndAssertEmpty(
            db,
            "Events | make-series s=sum(Val) on X from 0 to 3 step 1");

        MakeSeriesNeonTestUtils.ExecuteAndAssertEmpty(
            db,
            "Events | make-series s=sum(Val) on X step 1");

        IReadOnlyDictionary<string, object?> nonEmptyRow = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            "Events | make-series kind=nonempty s=sum(Val) default=-1 on X from 0 to 3 step 1");

        MakeSeriesNeonTestUtils.AssertLongSeries(nonEmptyRow, "s", -1, -1, -1);
        MakeSeriesNeonTestUtils.AssertLongSeries(nonEmptyRow, "X", 0, 1, 2);
    }

    [Test]
    [Description("make-series - mixed null and non-null metric values in the same bin ignore nulls for sum/avg/min/max/dcount while count() counts all rows")]
    public void MakeSeries_MixedNullAndNonNullValuesInSameBin()
    {
        Table table = MakeSeriesNeonTestUtils.CreateDateTimeValueTable(
            [
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0, 10), 10.0),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0, 20), null),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0, 30), 30.0),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0, 40), null)
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            """
            Events
            | make-series c=count(), s=sum(Val), a=avg(Val), mn=min(Val), mx=max(Val), dc=dcount(Val)
              on Ts from datetime(2025-01-01T00:00:00Z) to datetime(2025-01-01T02:00:00Z) step 1h
            """);

        MakeSeriesNeonTestUtils.AssertLongSeries(row, "c", 4, 0);
        MakeSeriesNeonTestUtils.AssertDoubleSeries(row, "s", 40.0, 0.0);
        MakeSeriesNeonTestUtils.AssertDoubleSeries(row, "a", 20.0, 0.0);
        MakeSeriesNeonTestUtils.AssertDoubleSeries(row, "mn", 10.0, 0.0);
        MakeSeriesNeonTestUtils.AssertDoubleSeries(row, "mx", 30.0, 0.0);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "dc", 2, 0);
    }

    [Test]
    [Description("make-series - NaN aggregate results (pure NaN or finite+NaN in a bin) are replaced with the series default value, while count() still counts rows")]
    public void MakeSeries_NaNMetricValuesInBin_ReplacedWithDefault()
    {
        // Oracle-verified against ADX: sum/avg/min/max propagating NaN in a populated bin fall back to default.
        Table table = MakeSeriesNeonTestUtils.CreateTable(
            "Events",
            [
                MakeSeriesNeonTestUtils.CreateColumn("X", typeof(long), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Val", typeof(double), isNullable: true)
            ],
            [
                new Dictionary<string, object?> { { "X", 0L }, { "Val", double.NaN } },
                new Dictionary<string, object?> { { "X", 1L }, { "Val", 10.0 } },
                new Dictionary<string, object?> { { "X", 1L }, { "Val", double.NaN } },
                new Dictionary<string, object?> { { "X", 2L }, { "Val", 5.0 } }
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            """
            Events
            | make-series c=count(), s=sum(Val) default=-1, a=avg(Val) default=-2, mn=min(Val) default=-3, mx=max(Val) default=-4
              on X from 0 to 4 step 1
            """);

        MakeSeriesNeonTestUtils.AssertLongSeries(row, "c", 1, 2, 1, 0);
        MakeSeriesNeonTestUtils.AssertDoubleSeries(row, "s", -1.0, -1.0, 5.0, -1.0);
        MakeSeriesNeonTestUtils.AssertDoubleSeries(row, "a", -2.0, -2.0, 5.0, -2.0);
        MakeSeriesNeonTestUtils.AssertDoubleSeries(row, "mn", -3.0, -3.0, 5.0, -3.0);
        MakeSeriesNeonTestUtils.AssertDoubleSeries(row, "mx", -4.0, -4.0, 5.0, -4.0);
    }

    [Test]
    [Description("make-series - single-row input table works with omitted bounds, explicit bounds, and by grouping")]
    public void MakeSeries_SingleRowInput_WithAndWithoutBy()
    {
        Table table = MakeSeriesNeonTestUtils.CreateKeyedDateTimeTable(
            [
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 1, 30), "Solo", 42)
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyDictionary<string, object?> omittedNoBy = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            "Events | make-series s=sum(Val) on Ts step 1h");

        MakeSeriesNeonTestUtils.AssertLongSeries(omittedNoBy, "s", 42);
        MakeSeriesNeonTestUtils.AssertDateTimeSeries(omittedNoBy, "Ts", MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 1));

        IReadOnlyList<IReadOnlyDictionary<string, object?>> explicitWithBy = MakeSeriesNeonTestUtils.ExecuteAndAssertSuccess(
            db,
            """
            Events
            | make-series s=sum(Val) default=-1 on Ts
              from datetime(2025-01-01T00:00:00Z) to datetime(2025-01-01T03:00:00Z) step 1h
              by Grp
            """);

        Assert.That(explicitWithBy, Has.Count.EqualTo(1));
        Assert.That(explicitWithBy[0]["Grp"], Is.EqualTo("Solo"));
        MakeSeriesNeonTestUtils.AssertLongSeries(explicitWithBy[0], "s", -1, 42, -1);
    }

    #endregion

    #region Empty Input, Out-Of-Range Rows, and Partial-Group Filtering

    [Test]
    [Description("make-series - empty input table returns 0 rows with explicit bounds, omitted bounds, or by grouping")]
    public void MakeSeries_EmptyInputTable_ReturnsZeroRows()
    {
        Table empty = MakeSeriesNeonTestUtils.CreateEmptyTable(
            "Events",
            MakeSeriesNeonTestUtils.CreateColumn("Ts", typeof(DateTime), isNullable: true),
            MakeSeriesNeonTestUtils.CreateColumn("Grp", typeof(string), isNullable: true),
            MakeSeriesNeonTestUtils.CreateColumn("Val", typeof(int), isNullable: true));
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(empty);

        MakeSeriesNeonTestUtils.ExecuteAndAssertEmpty(
            db,
            "Events | make-series s=sum(Val) on Ts from datetime(2025-01-01T00:00:00Z) to datetime(2025-01-01T03:00:00Z) step 1h");

        MakeSeriesNeonTestUtils.ExecuteAndAssertEmpty(
            db,
            "Events | make-series s=sum(Val) on Ts from datetime(2025-01-01T00:00:00Z) to datetime(2025-01-01T03:00:00Z) step 1h by Grp");

        MakeSeriesNeonTestUtils.ExecuteAndAssertEmpty(
            db,
            "Events | make-series s=sum(Val) on Ts step 1h");
    }

    [Test]
    [Description("make-series - upstream where filtering out all rows returns 0 rows normally and 1 default row with kind=nonempty")]
    public void MakeSeries_FilteredEmptyInput_NormalVsKindNonEmpty()
    {
        Table table = MakeSeriesNeonTestUtils.CreateLongValueTable([(0, 10), (1, 20)]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        MakeSeriesNeonTestUtils.ExecuteAndAssertEmpty(
            db,
            "Events | where Val > 1000 | make-series s=sum(Val) on X from 0 to 3 step 1");

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            "Events | where Val > 1000 | make-series kind=nonempty s=sum(Val) default=-7 on X from 0 to 3 step 1");

        MakeSeriesNeonTestUtils.AssertLongSeries(row, "s", -7, -7, -7);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "X", 0, 1, 2);
    }

    [Test]
    [Description("make-series - non-empty table where all rows fall outside [from, to) returns 0 rows both with and without by")]
    public void MakeSeries_AllRowsOutsideRange_ReturnsZeroRows()
    {
        Table table = MakeSeriesNeonTestUtils.CreateKeyedDateTimeTable(
            [
                (MakeSeriesNeonTestUtils.Utc(2024, 12, 31, 23, 0), "A", 10),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 2, 0, 0), "B", 20)
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        MakeSeriesNeonTestUtils.ExecuteAndAssertEmpty(
            db,
            """
            Events
            | make-series s=sum(Val) on Ts
              from datetime(2025-01-01T00:00:00Z) to datetime(2025-01-01T03:00:00Z) step 1h
            """);

        MakeSeriesNeonTestUtils.ExecuteAndAssertEmpty(
            db,
            """
            Events
            | make-series s=sum(Val) on Ts
              from datetime(2025-01-01T00:00:00Z) to datetime(2025-01-01T03:00:00Z) step 1h
              by Grp
            """);
    }

    [Test]
    [Description("make-series - when one by-group has rows in range and another only has rows outside range, only the in-range group is emitted")]
    public void MakeSeries_PartialGroupOutOfRange_OmitsOutOfRangeGroup()
    {
        Table table = MakeSeriesNeonTestUtils.CreateKeyedDateTimeTable(
            [
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 1, 0), "InRange", 10),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 10, 0), "OutOfRange", 20),
                (MakeSeriesNeonTestUtils.Utc(2024, 12, 31, 12, 0), "OutOfRange", 30)
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = MakeSeriesNeonTestUtils.ExecuteAndAssertSuccess(
            db,
            """
            Events
            | make-series s=sum(Val) default=-1 on Ts
              from datetime(2025-01-01T00:00:00Z) to datetime(2025-01-01T03:00:00Z) step 1h
              by Grp
            """);

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Grp"], Is.EqualTo("InRange"));
        MakeSeriesNeonTestUtils.AssertLongSeries(rows[0], "s", -1, 10, -1);
    }

    #endregion

    #region kind=nonempty Semantics

    [Test]
    [Description("make-series - kind=nonempty on empty table with explicit from/to produces 1 row filled with implicit, explicit, and null defaults")]
    public void MakeSeries_KindNonEmpty_EmptyTable_ExplicitBounds_MixedDefaults()
    {
        Table empty = MakeSeriesNeonTestUtils.CreateEmptyTable(
            "Events",
            MakeSeriesNeonTestUtils.CreateColumn("Ts", typeof(DateTime), isNullable: true),
            MakeSeriesNeonTestUtils.CreateColumn("Val", typeof(int), isNullable: true));
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(empty);

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            """
            Events
            | make-series kind=nonempty
                c=count(),
                s=sum(Val) default=-1,
                a=avg(Val) default=double(null)
              on Ts from datetime(2025-01-01T00:00:00Z) to datetime(2025-01-01T03:00:00Z) step 1h
            """);

        MakeSeriesNeonTestUtils.AssertColumnOrder(row, "c", "s", "a", "Ts");
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "c", 0, 0, 0);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "s", -1, -1, -1);
        MakeSeriesNeonTestUtils.AssertDoubleSeries(row, "a", null, null, null);
        MakeSeriesNeonTestUtils.AssertDateTimeSeries(
            row,
            "Ts",
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 1),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 2));
    }

    [Test]
    [Description("make-series - kind=nonempty when all input rows fall outside [from, to) still produces 1 default-filled row")]
    public void MakeSeries_KindNonEmpty_AllRowsOutsideRange_ReturnsDefaultRow()
    {
        Table table = MakeSeriesNeonTestUtils.CreateLongValueTable([(100, 10), (200, 20)]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            "Events | make-series kind=nonempty s=sum(Val) default=-1 on X from 0 to 3 step 1");

        MakeSeriesNeonTestUtils.AssertLongSeries(row, "s", -1, -1, -1);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "X", 0, 1, 2);
    }

    [Test]
    [Description("make-series - kind=nonempty on non-empty matching input behaves identically to normal make-series")]
    public void MakeSeries_KindNonEmpty_WithMatchingData_PreservesValues()
    {
        Table table = MakeSeriesNeonTestUtils.CreateLongValueTable([(0, 10), (2, 30)]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            "Events | make-series kind=nonempty s=sum(Val) default=-1 on X from 0 to 3 step 1");

        MakeSeriesNeonTestUtils.AssertLongSeries(row, "s", 10, -1, 30);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "X", 0, 1, 2);
    }

    [Test]
    [Description("make-series - kind=nonempty with in range(start, stop, step) on empty input produces 1 inclusive default-filled row")]
    public void MakeSeries_KindNonEmpty_InRangeSyntax()
    {
        // Oracle-verified against ADX: empty table + kind=nonempty + in range(0, 4, 2) -> s=[-1,-1,-1], x=[0,2,4].
        Table empty = MakeSeriesNeonTestUtils.CreateEmptyTable(
            "Events",
            MakeSeriesNeonTestUtils.CreateColumn("X", typeof(long), isNullable: true),
            MakeSeriesNeonTestUtils.CreateColumn("Val", typeof(int), isNullable: true));
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(empty);

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            "Events | make-series kind=nonempty s=sum(Val) default=-1 on X in range(0, 4, 2)");

        MakeSeriesNeonTestUtils.AssertLongSeries(row, "s", -1, -1, -1);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "X", 0, 2, 4);
    }

    [Test]
    [Description("make-series - kind=nonempty returns 0 rows when from == to or from > to")]
    public void MakeSeries_KindNonEmpty_EmptyOrInvertedRange_ReturnsZeroRows()
    {
        // Oracle-verified against ADX.
        Table empty = MakeSeriesNeonTestUtils.CreateEmptyTable(
            "Events",
            MakeSeriesNeonTestUtils.CreateColumn("X", typeof(long), isNullable: true),
            MakeSeriesNeonTestUtils.CreateColumn("Val", typeof(int), isNullable: true));
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(empty);

        MakeSeriesNeonTestUtils.ExecuteAndAssertEmpty(
            db,
            "Events | make-series kind=nonempty s=sum(Val) default=-1 on X from 5 to 5 step 1");

        MakeSeriesNeonTestUtils.ExecuteAndAssertEmpty(
            db,
            "Events | make-series kind=nonempty s=sum(Val) default=-1 on X from 5 to 2 step 1");
    }

    #endregion

    #region Query Hints

    [Test]
    [Description("make-series - hint.shufflekey and hint.num_partitions are accepted without altering results")]
    public void MakeSeries_QueryHints_AcceptedWithoutChangingResults()
    {
        Table table = MakeSeriesNeonTestUtils.CreateKeyedDateTimeTable(
            [
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0, 10), "A", 10),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 2, 10), "A", 20),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 1, 10), "B", 30)
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyList<IReadOnlyDictionary<string, object?>> shuffleRows = MakeSeriesNeonTestUtils.ExecuteAndAssertSuccess(
            db,
            """
            Events
            | make-series hint.shufflekey=Grp s=sum(Val) on Ts
              from datetime(2025-01-01T00:00:00Z) to datetime(2025-01-01T03:00:00Z) step 1h
              by Grp
            """);

        Assert.That(shuffleRows, Has.Count.EqualTo(2));
        MakeSeriesNeonTestUtils.AssertLongSeries(MakeSeriesNeonTestUtils.FindRowByKey(shuffleRows, "Grp", "A"), "s", 10, 0, 20);
        MakeSeriesNeonTestUtils.AssertLongSeries(MakeSeriesNeonTestUtils.FindRowByKey(shuffleRows, "Grp", "B"), "s", 0, 30, 0);

        IReadOnlyList<IReadOnlyDictionary<string, object?>> partitionRows = MakeSeriesNeonTestUtils.ExecuteAndAssertSuccess(
            db,
            """
            Events
            | make-series hint.num_partitions=4 s=sum(Val) on Ts
              from datetime(2025-01-01T00:00:00Z) to datetime(2025-01-01T03:00:00Z) step 1h
              by Grp
            """);

        Assert.That(partitionRows, Has.Count.EqualTo(2));
        MakeSeriesNeonTestUtils.AssertLongSeries(MakeSeriesNeonTestUtils.FindRowByKey(partitionRows, "Grp", "A"), "s", 10, 0, 20);
        MakeSeriesNeonTestUtils.AssertLongSeries(MakeSeriesNeonTestUtils.FindRowByKey(partitionRows, "Grp", "B"), "s", 0, 30, 0);
    }

    #endregion
}
