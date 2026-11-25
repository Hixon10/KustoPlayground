namespace KustoPlayground.Core.Tests;

public class MakeSeriesNeonSchemaNamingPipelinesAndErrorsTests
{
    #region Output Column Naming, Ordering, and getschema

    [Test]
    [Description("make-series - output column order is strictly [by columns], [aggregate columns], [axis column last]")]
    public void MakeSeries_OutputColumnOrder_ByThenAggregatesThenAxis()
    {
        Table table = MakeSeriesNeonTestUtils.CreateTable(
            "Events",
            [
                MakeSeriesNeonTestUtils.CreateColumn("Ts", typeof(DateTime), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("K1", typeof(string), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("K2", typeof(int), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Val", typeof(int), isNullable: true)
            ],
            [
                new Dictionary<string, object?> { { "Ts", MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0) }, { "K1", "a" }, { "K2", 1 }, { "Val", 10 } }
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            """
            Events
            | make-series firstAgg=sum(Val), secondAgg=count()
              on Ts from datetime(2025-01-01T00:00:00Z) to datetime(2025-01-01T02:00:00Z) step 1h
              by K2, K1
            """);

        MakeSeriesNeonTestUtils.AssertColumnOrder(row, "K2", "K1", "firstAgg", "secondAgg", "Ts");
    }

    [Test]
    [Description("make-series - unnamed aggregates receive standard KQL auto-generated names and numeric disambiguation suffixes")]
    public void MakeSeries_UnnamedAggregateAutoNaming_AndDisambiguation()
    {
        // Oracle-verified against ADX: count(), count(), sum(Val) -> count_, count_1, sum_Val.
        Table table = MakeSeriesNeonTestUtils.CreateTable(
            "Events",
            [
                MakeSeriesNeonTestUtils.CreateColumn("X", typeof(long), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Val", typeof(int), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Flag", typeof(bool), isNullable: true)
            ],
            [
                new Dictionary<string, object?> { { "X", 0L }, { "Val", 10 }, { "Flag", true } },
                new Dictionary<string, object?> { { "X", 2L }, { "Val", 20 }, { "Flag", false } }
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            """
            Events
            | make-series
                count(),
                count(),
                sum(Val),
                avg(Val),
                min(Val),
                max(Val),
                dcount(Val),
                countif(Flag),
                sumif(Val, Flag),
                percentile(Val, 50)
              on X from 0 to 3 step 1
            """);

        MakeSeriesNeonTestUtils.AssertColumnOrder(
            row,
            "count_",
            "count_1",
            "sum_Val",
            "avg_Val",
            "min_Val",
            "max_Val",
            "dcount_Val",
            "countif_Flag",
            "sumif_Val",
            "percentile_Val_50",
            "X");

        MakeSeriesNeonTestUtils.AssertLongSeries(row, "count_", 1, 0, 1);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "count_1", 1, 0, 1);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "sum_Val", 10, 0, 20);
        MakeSeriesNeonTestUtils.AssertDoubleSeries(row, "avg_Val", 10.0, 0.0, 20.0);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "min_Val", 10, 0, 20);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "max_Val", 10, 0, 20);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "dcount_Val", 1, 0, 1);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "countif_Flag", 1, 0, 0);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "sumif_Val", 10, 0, 0);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "percentile_Val_50", 10, 0, 20);
    }

    [Test]
    [Description("make-series - getschema reports scalar types for by columns and dynamic (System.Object) for all aggregate and axis columns")]
    public void MakeSeries_GetSchema_ScalarByColumnsAndDynamicSeriesColumns()
    {
        // Oracle-verified against ADX: all aggregate columns (including percentile & take_any) and the axis column have ColumnType="dynamic", DataType="System.Object".
        Table table = MakeSeriesNeonTestUtils.CreateTable(
            "Events",
            [
                MakeSeriesNeonTestUtils.CreateColumn("Ts", typeof(DateTime), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Grp", typeof(string), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Code", typeof(int), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Active", typeof(bool), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Val", typeof(double), isNullable: true)
            ],
            [
                new Dictionary<string, object?>
                {
                    { "Ts", MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0) },
                    { "Grp", "a" },
                    { "Code", 1 },
                    { "Active", true },
                    { "Val", 10.5 }
                }
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyList<IReadOnlyDictionary<string, object?>> schema = MakeSeriesNeonTestUtils.ExecuteAndAssertSuccess(
            db,
            """
            Events
            | make-series c=count(), s=sum(Val), a=avg(Val), p50=percentile(Val, 50), ta=take_any(Val)
              on Ts from datetime(2025-01-01T00:00:00Z) to datetime(2025-01-01T02:00:00Z) step 1h
              by Grp, Code, Active
            | getschema
            """);

        Assert.That(schema, Has.Count.EqualTo(9));
        MakeSeriesNeonTestUtils.AssertSchemaRow(schema, 0, "Grp", "string", "System.String");
        MakeSeriesNeonTestUtils.AssertSchemaRow(schema, 1, "Code", "int", "System.Int32");
        MakeSeriesNeonTestUtils.AssertSchemaRow(schema, 2, "Active", "bool", "System.SByte");
        MakeSeriesNeonTestUtils.AssertSchemaRow(schema, 3, "c", "dynamic", "System.Object");
        MakeSeriesNeonTestUtils.AssertSchemaRow(schema, 4, "s", "dynamic", "System.Object");
        MakeSeriesNeonTestUtils.AssertSchemaRow(schema, 5, "a", "dynamic", "System.Object");
        MakeSeriesNeonTestUtils.AssertSchemaRow(schema, 6, "p50", "dynamic", "System.Object");
        MakeSeriesNeonTestUtils.AssertSchemaRow(schema, 7, "ta", "dynamic", "System.Object");
        MakeSeriesNeonTestUtils.AssertSchemaRow(schema, 8, "Ts", "dynamic", "System.Object");
    }

    [Test]
    [Description("make-series - gettype() returns 'array' for both aggregate and axis columns and every series matches axis array_length")]
    public void MakeSeries_GetTypeIsArray_AndArrayLengthInvariant()
    {
        Table table = MakeSeriesNeonTestUtils.CreateKeyedDateTimeTable(
            [
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0, 10), "A", 10),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 3, 20), "B", 20)
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = MakeSeriesNeonTestUtils.ExecuteAndAssertSuccess(
            db,
            """
            Events
            | make-series s=sum(Val), c=count()
              on Ts from datetime(2025-01-01T00:00:00Z) to datetime(2025-01-01T05:00:00Z) step 1h
              by Grp
            | project
                Grp,
                sType=gettype(s),
                axisType=gettype(Ts),
                sLen=array_length(s),
                cLen=array_length(c),
                axisLen=array_length(Ts)
            """);

        Assert.That(rows, Has.Count.EqualTo(2));
        foreach (IReadOnlyDictionary<string, object?> row in rows)
        {
            Assert.That(row["sType"], Is.EqualTo("array"));
            Assert.That(row["axisType"], Is.EqualTo("array"));
            Assert.That(Convert.ToInt64(row["sLen"], System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo(5L));
            Assert.That(Convert.ToInt64(row["cLen"], System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo(5L));
            Assert.That(Convert.ToInt64(row["axisLen"], System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo(5L));
        }
    }

    #endregion

    #region Upstream and Downstream Pipeline Composition

    [Test]
    [Description("make-series - works directly on inline datatable(...) without pre-existing tables")]
    public void MakeSeries_InlineDatatableInput()
    {
        var db = new KustoDatabase();

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            """
            datatable(Ts:datetime, Val:int)[
                datetime(2025-01-01T00:10:00Z), 10,
                datetime(2025-01-01T02:40:00Z), 30
            ]
            | make-series s=sum(Val) on Ts
              from datetime(2025-01-01T00:00:00Z) to datetime(2025-01-01T03:00:00Z) step 1h
            """);

        MakeSeriesNeonTestUtils.AssertLongSeries(row, "s", 10, 0, 30);
    }

    [Test]
    [Description("make-series - upstream where and extend (computing derived axis or metric) feed cleanly into make-series")]
    public void MakeSeries_UpstreamWhereAndExtend()
    {
        Table table = MakeSeriesNeonTestUtils.CreateKeyedDateTimeTable(
            [
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0, 15), "A", 10),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0, 45), "A", -5),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 2, 15), "A", 20),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 1, 15), "B", 100)
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            """
            Events
            | where Grp == "A" and Val > 0
            | extend ShiftedTs = Ts + 1h, Doubled = Val * 2
            | make-series s=sum(Doubled) on ShiftedTs
              from datetime(2025-01-01T01:00:00Z) to datetime(2025-01-01T04:00:00Z) step 1h
            """);

        MakeSeriesNeonTestUtils.AssertLongSeries(row, "s", 20, 0, 40);
        MakeSeriesNeonTestUtils.AssertDateTimeSeries(
            row,
            "ShiftedTs",
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 1),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 2),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 3));
    }

    [Test]
    [Description("make-series - upstream union and join pipelines feed cleanly into make-series")]
    public void MakeSeries_UpstreamUnionAndJoin()
    {
        Table t1 = MakeSeriesNeonTestUtils.CreateKeyedDateTimeTable(
            [(MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0, 10), "A", 10)],
            tableName: "LeftEvents");
        Table t2 = MakeSeriesNeonTestUtils.CreateKeyedDateTimeTable(
            [(MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 2, 10), "A", 30)],
            tableName: "RightEvents");
        Table dim = MakeSeriesNeonTestUtils.CreateTable(
            "GroupMeta",
            [
                MakeSeriesNeonTestUtils.CreateColumn("Grp", typeof(string), isNullable: false),
                MakeSeriesNeonTestUtils.CreateColumn("Tier", typeof(string), isNullable: false)
            ],
            [
                new Dictionary<string, object?> { { "Grp", "A" }, { "Tier", "Gold" } }
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(t1, t2, dim);

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            """
            union LeftEvents, RightEvents
            | join kind=inner GroupMeta on Grp
            | make-series s=sum(Val) on Ts
              from datetime(2025-01-01T00:00:00Z) to datetime(2025-01-01T03:00:00Z) step 1h
              by Tier
            """);

        Assert.That(row["Tier"], Is.EqualTo("Gold"));
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "s", 10, 0, 30);
    }

    [Test]
    [Description("make-series - two-stage summarize followed by make-series aggregates pre-bucketed summaries")]
    public void MakeSeries_AfterSummarize()
    {
        Table table = MakeSeriesNeonTestUtils.CreateKeyedDateTimeTable(
            [
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0, 5), "A", 10),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0, 20), "A", 20),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0, 40), "A", 30),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 2, 10), "A", 50)
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            """
            Events
            | summarize HalfHourMax = max(Val) by Grp, Bucket = bin(Ts, 30m)
            | make-series s=sum(HalfHourMax) on Bucket
              from datetime(2025-01-01T00:00:00Z) to datetime(2025-01-01T03:00:00Z) step 1h
              by Grp
            """);

        // Hour 0 has two 30m buckets: max(10,20)=20 and max(30)=30 -> sum=50; Hour 1 -> 0; Hour 2 -> 50
        Assert.That(row["Grp"], Is.EqualTo("A"));
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "s", 50, 0, 50);
    }

    [Test]
    [Description("make-series - downstream array functions (array_sum, array_slice, array_reverse, indexing) and sort/project work on series arrays")]
    public void MakeSeries_DownstreamArrayFunctionsAndSort()
    {
        Table table = MakeSeriesNeonTestUtils.CreateKeyedDateTimeTable(
            [
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0, 10), "B", 1),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 1, 10), "B", 2),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 2, 10), "B", 3),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0, 10), "A", 10),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 2, 10), "A", 30)
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = MakeSeriesNeonTestUtils.ExecuteAndAssertSuccess(
            db,
            """
            Events
            | make-series s=sum(Val) on Ts
              from datetime(2025-01-01T00:00:00Z) to datetime(2025-01-01T03:00:00Z) step 1h
              by Grp
            | extend
                Total = array_sum(s),
                First = tolong(s[0]),
                Last = tolong(s[-1]),
                Middle = array_slice(s, 1, 2),
                Rev = array_reverse(s)
            | project-away Ts
            | sort by Grp asc
            """);

        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows[0]["Grp"], Is.EqualTo("A"));
        Assert.That(Convert.ToDouble(rows[0]["Total"], System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo(40.0).Within(1e-9));
        Assert.That(Convert.ToInt64(rows[0]["First"], System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo(10L));
        Assert.That(Convert.ToInt64(rows[0]["Last"], System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo(30L));
        MakeSeriesNeonTestUtils.AssertLongSeries(rows[0], "Middle", 0, 30);
        MakeSeriesNeonTestUtils.AssertLongSeries(rows[0], "Rev", 30, 0, 10);

        Assert.That(rows[1]["Grp"], Is.EqualTo("B"));
        Assert.That(Convert.ToDouble(rows[1]["Total"], System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo(6.0).Within(1e-9));
        MakeSeriesNeonTestUtils.AssertLongSeries(rows[1], "Rev", 3, 2, 1);
    }

    [Test]
    [Description("make-series - downstream mv-expand round-trips paired series and axis arrays back into tabular rows")]
    public void MakeSeries_DownstreamMvExpand_RoundTrip()
    {
        Table table = MakeSeriesNeonTestUtils.CreateKeyedDateTimeTable(
            [
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0, 10), "A", 10),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 2, 10), "A", 30)
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = MakeSeriesNeonTestUtils.ExecuteAndAssertSuccess(
            db,
            """
            Events
            | make-series s=sum(Val) default=-1 on Ts
              from datetime(2025-01-01T00:00:00Z) to datetime(2025-01-01T03:00:00Z) step 1h
              by Grp
            | mv-expand Ts to typeof(datetime), s to typeof(long)
            | sort by Ts asc
            """);

        Assert.That(rows, Has.Count.EqualTo(3));
        Assert.That(rows[0]["Grp"], Is.EqualTo("A"));
        Assert.That(rows[0]["Ts"], Is.EqualTo(MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0)));
        Assert.That(Convert.ToInt64(rows[0]["s"], System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo(10L));

        Assert.That(rows[1]["Ts"], Is.EqualTo(MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 1)));
        Assert.That(Convert.ToInt64(rows[1]["s"], System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo(-1L));

        Assert.That(rows[2]["Ts"], Is.EqualTo(MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 2)));
        Assert.That(Convert.ToInt64(rows[2]["s"], System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo(30L));
    }

    [Test]
    [Description("make-series - downstream mv-apply transforms each series row independently")]
    public void MakeSeries_DownstreamMvApply()
    {
        Table table = MakeSeriesNeonTestUtils.CreateKeyedDateTimeTable(
            [
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0, 10), "A", 10),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 2, 10), "A", 30),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 1, 10), "B", 5)
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = MakeSeriesNeonTestUtils.ExecuteAndAssertSuccess(
            db,
            """
            Events
            | make-series s=sum(Val) on Ts
              from datetime(2025-01-01T00:00:00Z) to datetime(2025-01-01T03:00:00Z) step 1h
              by Grp
            | mv-apply s to typeof(long) on (
                where s > 0
                | summarize PositiveBins = count(), PositiveSum = sum(s)
              )
            | sort by Grp asc
            """);

        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows[0]["Grp"], Is.EqualTo("A"));
        Assert.That(Convert.ToInt64(rows[0]["PositiveBins"], System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo(2L));
        Assert.That(Convert.ToInt64(rows[0]["PositiveSum"], System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo(40L));

        Assert.That(rows[1]["Grp"], Is.EqualTo("B"));
        Assert.That(Convert.ToInt64(rows[1]["PositiveBins"], System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo(1L));
        Assert.That(Convert.ToInt64(rows[1]["PositiveSum"], System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo(5L));
    }

    [Test]
    [Description("make-series - joining two make-series results via let and materialize, and invoking make-series inside a user-defined tabular function")]
    public void MakeSeries_JoinTwoSeries_AndUserDefinedTabularFunction()
    {
        Table table = MakeSeriesNeonTestUtils.CreateKeyedDateTimeTable(
            [
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0, 10), "A", 10),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 1, 10), "A", 20),
                (MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0, 10), "B", 5)
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyList<IReadOnlyDictionary<string, object?>> joined = MakeSeriesNeonTestUtils.ExecuteAndAssertSuccess(
            db,
            """
            let base = materialize(Events);
            let s1 = base | make-series total=sum(Val) on Ts from datetime(2025-01-01T00:00:00Z) to datetime(2025-01-01T02:00:00Z) step 1h by Grp;
            let s2 = base | make-series cnt=count() on Ts from datetime(2025-01-01T00:00:00Z) to datetime(2025-01-01T02:00:00Z) step 1h by Grp;
            s1 | join kind=inner s2 on Grp | project Grp, total, cnt | sort by Grp asc
            """);

        Assert.That(joined, Has.Count.EqualTo(2));
        Assert.That(joined[0]["Grp"], Is.EqualTo("A"));
        MakeSeriesNeonTestUtils.AssertLongSeries(joined[0], "total", 10, 20);
        MakeSeriesNeonTestUtils.AssertLongSeries(joined[0], "cnt", 1, 1);

        Assert.That(joined[1]["Grp"], Is.EqualTo("B"));
        MakeSeriesNeonTestUtils.AssertLongSeries(joined[1], "total", 5, 0);
        MakeSeriesNeonTestUtils.AssertLongSeries(joined[1], "cnt", 1, 0);

        IReadOnlyDictionary<string, object?> udfRow = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            """
            let HourlySum = (T:(Ts:datetime, Grp:string, Val:int)) {
                T | make-series s=sum(Val) on Ts from datetime(2025-01-01T00:00:00Z) to datetime(2025-01-01T02:00:00Z) step 1h
            };
            HourlySum(Events)
            """);

        MakeSeriesNeonTestUtils.AssertLongSeries(udfRow, "s", 15, 20);
    }

    #endregion

    #region Semantic and Runtime Error Cases

    [Test]
    [Description("make-series - kind=nonempty combined with by clause produces an error on both empty and non-empty input")]
    public void MakeSeries_Error_KindNonEmptyWithBy()
    {
        Table nonEmpty = MakeSeriesNeonTestUtils.CreateKeyedDateTimeTable(
            [(MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0, 10), "A", 10)],
            tableName: "NonEmpty");
        Table empty = MakeSeriesNeonTestUtils.CreateEmptyTable(
            "EmptyEvents",
            MakeSeriesNeonTestUtils.CreateColumn("Ts", typeof(DateTime), isNullable: true),
            MakeSeriesNeonTestUtils.CreateColumn("Grp", typeof(string), isNullable: true),
            MakeSeriesNeonTestUtils.CreateColumn("Val", typeof(int), isNullable: true));
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(nonEmpty, empty);

        MakeSeriesNeonTestUtils.ExecuteAndAssertError(
            db,
            """
            EmptyEvents
            | make-series kind=nonempty s=sum(Val) on Ts
              from datetime(2025-01-01T00:00:00Z) to datetime(2025-01-01T02:00:00Z) step 1h
              by Grp
            """);

        MakeSeriesNeonTestUtils.ExecuteAndAssertError(
            db,
            """
            NonEmpty
            | make-series kind=nonempty s=sum(Val) on Ts
              from datetime(2025-01-01T00:00:00Z) to datetime(2025-01-01T02:00:00Z) step 1h
              by Grp
            """);
    }

    [Test]
    [Description("make-series - zero or negative step values are rejected across long, real, and datetime/timespan axes")]
    [TestCase("LongEvents | make-series s=sum(Val) on X from 0 to 10 step 0")]
    [TestCase("LongEvents | make-series s=sum(Val) on X from 0 to 10 step -2")]
    [TestCase("RealEvents | make-series s=sum(Val) on Ax from 0.0 to 2.0 step 0.0")]
    [TestCase("RealEvents | make-series s=sum(Val) on Ax from 0.0 to 2.0 step -0.5")]
    [TestCase("DtEvents | make-series s=sum(Val) on Ts from datetime(2025-01-01T00:00:00Z) to datetime(2025-01-01T02:00:00Z) step 0s")]
    [TestCase("DtEvents | make-series s=sum(Val) on Ts from datetime(2025-01-01T00:00:00Z) to datetime(2025-01-01T02:00:00Z) step -1h")]
    public void MakeSeries_Error_ZeroOrNegativeStep(string query)
    {
        Table longTable = MakeSeriesNeonTestUtils.CreateLongValueTable([(0, 10)], tableName: "LongEvents");
        Table realTable = MakeSeriesNeonTestUtils.CreateTable(
            "RealEvents",
            [
                MakeSeriesNeonTestUtils.CreateColumn("Ax", typeof(double), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Val", typeof(double), isNullable: true)
            ],
            [new Dictionary<string, object?> { { "Ax", 0.5 }, { "Val", 1.0 } }]);
        Table dtTable = MakeSeriesNeonTestUtils.CreateDateTimeIntTable(
            [(MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0), 10)],
            tableName: "DtEvents");
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(longTable, realTable, dtTable);

        MakeSeriesNeonTestUtils.ExecuteAndAssertError(db, query);
    }

    [Test]
    [Description("make-series - null from, to, or step range parameters are rejected")]
    [TestCase("LongEvents | make-series s=sum(Val) on X from long(null) to 5 step 1")]
    [TestCase("LongEvents | make-series s=sum(Val) on X from 0 to long(null) step 1")]
    [TestCase("LongEvents | make-series s=sum(Val) on X from 0 to 5 step long(null)")]
    [TestCase("DtEvents | make-series s=sum(Val) on Ts from datetime(null) to datetime(2025-01-01T02:00:00Z) step 1h")]
    [TestCase("DtEvents | make-series s=sum(Val) on Ts from datetime(2025-01-01T00:00:00Z) to datetime(null) step 1h")]
    [TestCase("DtEvents | make-series s=sum(Val) on Ts from datetime(2025-01-01T00:00:00Z) to datetime(2025-01-01T02:00:00Z) step timespan(null)")]
    public void MakeSeries_Error_NullRangeParameters(string query)
    {
        Table longTable = MakeSeriesNeonTestUtils.CreateLongValueTable([(0, 10)], tableName: "LongEvents");
        Table dtTable = MakeSeriesNeonTestUtils.CreateDateTimeIntTable(
            [(MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0), 10)],
            tableName: "DtEvents");
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(longTable, dtTable);

        MakeSeriesNeonTestUtils.ExecuteAndAssertError(db, query);
    }

    [Test]
    [Description("make-series - unsupported axis column types (string, bool, guid, dynamic) are rejected")]
    [TestCase("Events | make-series c=count() on StrCol step 1")]
    [TestCase("Events | make-series c=count() on BoolCol step 1")]
    [TestCase("Events | make-series c=count() on GuidCol step 1")]
    [TestCase("Events | make-series c=count() on DynCol step 1")]
    public void MakeSeries_Error_UnsupportedAxisColumnTypes(string query)
    {
        Table table = MakeSeriesNeonTestUtils.CreateTable(
            "Events",
            [
                MakeSeriesNeonTestUtils.CreateColumn("StrCol", typeof(string), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("BoolCol", typeof(bool), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("GuidCol", typeof(Guid), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("DynCol", typeof(object), isNullable: true)
            ],
            [
                new Dictionary<string, object?>
                {
                    { "StrCol", "a" },
                    { "BoolCol", true },
                    { "GuidCol", Guid.NewGuid() },
                    { "DynCol", new object?[] { 1, 2 } }
                }
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        MakeSeriesNeonTestUtils.ExecuteAndAssertError(db, query);
    }

    [Test]
    [Description("make-series - mismatched axis, bound, and step types are rejected")]
    [TestCase("Events | make-series s=sum(Val) on Ts from datetime(2025-01-01T00:00:00Z) to datetime(2025-01-01T02:00:00Z) step 1")]
    [TestCase("Events | make-series s=sum(Val) on X from 0 to 10 step 1h")]
    [TestCase("Events | make-series s=sum(Val) on Ts from 0 to 10 step 1h")]
    [TestCase("Events | make-series s=sum(Val) on X from datetime(2025-01-01T00:00:00Z) to datetime(2025-01-01T02:00:00Z) step 1")]
    public void MakeSeries_Error_MismatchedAxisAndStepOrBoundTypes(string query)
    {
        Table table = MakeSeriesNeonTestUtils.CreateTable(
            "Events",
            [
                MakeSeriesNeonTestUtils.CreateColumn("Ts", typeof(DateTime), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("X", typeof(long), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Val", typeof(int), isNullable: true)
            ],
            [
                new Dictionary<string, object?>
                {
                    { "Ts", MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0) },
                    { "X", 0L },
                    { "Val", 10 }
                }
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        MakeSeriesNeonTestUtils.ExecuteAndAssertError(db, query);
    }

    [Test]
    [Description("make-series - on clause rejects aliased or computed expressions and requires an existing column name")]
    [TestCase("Events | make-series s=sum(Val) on Axis=Ts from datetime(2025-01-01T00:00:00Z) to datetime(2025-01-01T02:00:00Z) step 1h")]
    [TestCase("Events | make-series s=sum(Val) on Ts + 1h from datetime(2025-01-01T01:00:00Z) to datetime(2025-01-01T03:00:00Z) step 1h")]
    [TestCase("Events | make-series s=sum(Val) on Axis=(Ts + 1h) from datetime(2025-01-01T01:00:00Z) to datetime(2025-01-01T03:00:00Z) step 1h")]
    public void MakeSeries_Error_OnClauseMustBeExistingColumn(string query)
    {
        // Oracle-verified against ADX (SEM0100).
        Table table = MakeSeriesNeonTestUtils.CreateDateTimeIntTable(
            [(MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 0), 10)]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        MakeSeriesNeonTestUtils.ExecuteAndAssertError(db, query);
    }

    [Test]
    [Description("make-series - unsupported non-numeric, collection, and multi-column aggregations are rejected")]
    [TestCase("Events | make-series a=make_list(Val) on X from 0 to 3 step 1")]
    [TestCase("Events | make-series a=make_set(Val) on X from 0 to 3 step 1")]
    [TestCase("Events | make-series a=make_bag(BagCol) on X from 0 to 3 step 1")]
    [TestCase("Events | make-series arg_max(Val, Label) on X from 0 to 3 step 1")]
    [TestCase("Events | make-series arg_min(Val, Label) on X from 0 to 3 step 1")]
    [TestCase("Events | make-series percentiles(Val, 50, 90) on X from 0 to 3 step 1")]
    [TestCase("Events | make-series a=take_any(Label) on X from 0 to 3 step 1")]
    [TestCase("Events | make-series a=sum(SpanCol) on X from 0 to 3 step 1")]
    public void MakeSeries_Error_UnsupportedAggregations(string query)
    {
        // Oracle-verified against ADX (SEM0455).
        Table table = MakeSeriesNeonTestUtils.CreateTable(
            "Events",
            [
                MakeSeriesNeonTestUtils.CreateColumn("X", typeof(long), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Val", typeof(int), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Label", typeof(string), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("SpanCol", typeof(TimeSpan), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("BagCol", typeof(object), isNullable: true)
            ],
            [
                new Dictionary<string, object?>
                {
                    { "X", 0L },
                    { "Val", 10 },
                    { "Label", "hi" },
                    { "SpanCol", TimeSpan.FromMinutes(5) },
                    { "BagCol", new Dictionary<string, object?> { { "k", 1 } } }
                }
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        MakeSeriesNeonTestUtils.ExecuteAndAssertError(db, query);
    }

    [Test]
    [Description("make-series - invalid non-numeric default types (string, bool, datetime, timespan) are rejected")]
    [TestCase("Events | make-series s=sum(Val) default=\"abc\" on X from 0 to 3 step 1")]
    [TestCase("Events | make-series s=sum(Val) default=true on X from 0 to 3 step 1")]
    [TestCase("Events | make-series s=sum(Val) default=datetime(2025-01-01T00:00:00Z) on X from 0 to 3 step 1")]
    [TestCase("Events | make-series s=sum(Val) default=1h on X from 0 to 3 step 1")]
    public void MakeSeries_Error_InvalidDefaultType(string query)
    {
        // Oracle-verified against ADX (SEM0481).
        Table table = MakeSeriesNeonTestUtils.CreateLongValueTable([(0, 10)]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        MakeSeriesNeonTestUtils.ExecuteAndAssertError(db, query);
    }

    [Test]
    [Description("make-series - duplicate explicit column names, unknown columns, non-aggregate column expressions, non-constant step, and invalid kind are rejected")]
    [TestCase("Events | make-series s=sum(Val), s=count() on X from 0 to 3 step 1")]
    [TestCase("Events | make-series Grp=sum(Val) on X from 0 to 3 step 1 by Grp")]
    [TestCase("Events | make-series s=sum(MissingCol) on X from 0 to 3 step 1")]
    [TestCase("Events | make-series s=sum(Val) on MissingAxis from 0 to 3 step 1")]
    [TestCase("Events | make-series s=sum(Val) on X from 0 to 3 step 1 by MissingGroup")]
    [TestCase("Events | make-series s=Val on X from 0 to 3 step 1")]
    [TestCase("Events | make-series s=sum(Val) on X from 0 to 3 step Val")]
    [TestCase("Events | make-series kind=invalid s=sum(Val) on X from 0 to 3 step 1")]
    public void MakeSeries_Error_InvalidColumnsDuplicatesAndOptions(string query)
    {
        Table table = MakeSeriesNeonTestUtils.CreateTable(
            "Events",
            [
                MakeSeriesNeonTestUtils.CreateColumn("X", typeof(long), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Grp", typeof(string), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Val", typeof(long), isNullable: true)
            ],
            [
                new Dictionary<string, object?> { { "X", 0L }, { "Grp", "A" }, { "Val", 10L } }
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        MakeSeriesNeonTestUtils.ExecuteAndAssertError(db, query);
    }

    [Test]
    public void MakeSeries_AggregateNamedLikeNumericAxis_IsRenamed()
    {
        var db = MakeSeriesNeonTestUtils.CreateDatabase(MakeSeriesNeonTestUtils.CreateLongValueTable([(0, 10)]));
        var row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(db,
            "Events | make-series X=sum(Val) on X from 0 to 3 step 1");
        MakeSeriesNeonTestUtils.AssertColumnOrder(row, "X1", "X");
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "X1", 10, 0, 0);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "X", 0, 1, 2);
    }

    #endregion
}
