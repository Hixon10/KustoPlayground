namespace KustoPlayground.Core.Tests;

public class MakeSeriesNeonAggregationsAndDefaultsTests
{
    #region count(), cnt(), countif(), sum(), and sumif()

    [Test]
    [Description("make-series - count() and cnt() alias fill empty bins with 0 by default and support custom default")]
    public void MakeSeries_CountAndCntAlias_ImplicitAndExplicitDefault()
    {
        Table table = MakeSeriesNeonTestUtils.CreateLongValueTable(
            [
                (0, 10),
                (0, 20),
                (2, null),
                (3, 40)
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            """
            Events
            | make-series c1=count(), c2=cnt(), c3=count() default=-1
              on X from 0 to 4 step 1
            """);

        MakeSeriesNeonTestUtils.AssertLongSeries(row, "c1", 2, 0, 1, 1);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "c2", 2, 0, 1, 1);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "c3", 2, -1, 1, 1);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "X", 0, 1, 2, 3);
    }

    [Test]
    [Description("make-series - countif() returns 0 for populated bins with zero matching rows and default only for absent bins; null predicates count as false")]
    public void MakeSeries_CountIf_PopulatedZeroMatchVsAbsentBin_AndNullPredicate()
    {
        Table table = MakeSeriesNeonTestUtils.CreateTable(
            "Events",
            [
                MakeSeriesNeonTestUtils.CreateColumn("X", typeof(long), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Val", typeof(int), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Flag", typeof(bool), isNullable: true)
            ],
            [
                new Dictionary<string, object?> { { "X", 0L }, { "Val", 10 }, { "Flag", true } },
                new Dictionary<string, object?> { { "X", 0L }, { "Val", 20 }, { "Flag", false } },
                new Dictionary<string, object?> { { "X", 1L }, { "Val", 30 }, { "Flag", false } },
                new Dictionary<string, object?> { { "X", 2L }, { "Val", 40 }, { "Flag", null } },
                new Dictionary<string, object?> { { "X", 4L }, { "Val", 50 }, { "Flag", true } }
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            """
            Events
            | make-series
                cf=countif(Flag) default=-1,
                cfExpr=countif(Val >= 20 and Flag == true) default=-1
              on X from 0 to 5 step 1
            """);

        // Bin 0: 1 true, Bin 1: populated all-false -> 0, Bin 2: populated null -> 0, Bin 3: absent -> -1, Bin 4: 1 true
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "cf", 1, 0, 0, -1, 1);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "cfExpr", 0, 0, 0, -1, 1);
    }

    [Test]
    [Description("make-series - sum() supports int, long, real, and decimal columns with implicit type-appropriate zero defaults")]
    public void MakeSeries_Sum_AcrossNumericTypes_ImplicitDefaults()
    {
        Table table = MakeSeriesNeonTestUtils.CreateTable(
            "Events",
            [
                MakeSeriesNeonTestUtils.CreateColumn("X", typeof(long), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Vi", typeof(int), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Vl", typeof(long), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Vr", typeof(double), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Vd", typeof(decimal), isNullable: true)
            ],
            [
                new Dictionary<string, object?> { { "X", 0L }, { "Vi", 10 }, { "Vl", 100L }, { "Vr", 1.5 }, { "Vd", 2.25m } },
                new Dictionary<string, object?> { { "X", 0L }, { "Vi", 5 }, { "Vl", 50L }, { "Vr", 2.25 }, { "Vd", 0.75m } },
                new Dictionary<string, object?> { { "X", 2L }, { "Vi", -3 }, { "Vl", 300L }, { "Vr", -0.5 }, { "Vd", 4.5m } }
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            """
            Events
            | make-series si=sum(Vi), sl=sum(Vl), sr=sum(Vr), sd=sum(Vd)
              on X from 0 to 3 step 1
            """);

        MakeSeriesNeonTestUtils.AssertLongSeries(row, "si", 15, 0, -3);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "sl", 150, 0, 300);
        MakeSeriesNeonTestUtils.AssertDoubleSeries(row, "sr", 3.75, 0.0, -0.5);
        MakeSeriesNeonTestUtils.AssertDecimalSeries(row, "sd", 3.00m, 0m, 4.5m);
    }

    [Test]
    [Description("make-series - sumif() returns 0 for populated bins with no matching rows or all-null values, and default only for absent bins")]
    public void MakeSeries_SumIf_PopulatedVsAbsentBins()
    {
        Table table = MakeSeriesNeonTestUtils.CreateTable(
            "Events",
            [
                MakeSeriesNeonTestUtils.CreateColumn("X", typeof(long), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Val", typeof(int), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Flag", typeof(bool), isNullable: true)
            ],
            [
                new Dictionary<string, object?> { { "X", 0L }, { "Val", 10 }, { "Flag", true } },
                new Dictionary<string, object?> { { "X", 0L }, { "Val", 20 }, { "Flag", false } },
                new Dictionary<string, object?> { { "X", 1L }, { "Val", null }, { "Flag", true } },
                new Dictionary<string, object?> { { "X", 2L }, { "Val", 30 }, { "Flag", false } }
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            "Events | make-series s=sumif(Val, Flag) default=-1 on X from 0 to 4 step 1");

        MakeSeriesNeonTestUtils.AssertLongSeries(row, "s", 10, 0, 0, -1);
    }

    #endregion

    #region avg(), avgif(), min(), minif(), max(), and maxif()

    [Test]
    [Description("make-series - avg() computes floating-point mean, ignores nulls within a bin, and replaces populated all-null bins and absent bins with default")]
    public void MakeSeries_Avg_AndAvgIf_PopulatedNullAndZeroMatchReplaceWithDefault()
    {
        // Oracle-verified against ADX: avg/avgif yield NaN on all-null or zero-match bins, which make-series replaces with default.
        Table table = MakeSeriesNeonTestUtils.CreateTable(
            "Events",
            [
                MakeSeriesNeonTestUtils.CreateColumn("X", typeof(long), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Val", typeof(int), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Flag", typeof(bool), isNullable: true)
            ],
            [
                new Dictionary<string, object?> { { "X", 0L }, { "Val", 10 }, { "Flag", true } },
                new Dictionary<string, object?> { { "X", 0L }, { "Val", 20 }, { "Flag", true } },
                new Dictionary<string, object?> { { "X", 0L }, { "Val", null }, { "Flag", true } },
                new Dictionary<string, object?> { { "X", 1L }, { "Val", null }, { "Flag", true } },
                new Dictionary<string, object?> { { "X", 2L }, { "Val", 30 }, { "Flag", false } }
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            """
            Events
            | make-series
                aDef=avg(Val),
                aNeg=avg(Val) default=-1.0,
                aNull=avg(Val) default=double(null),
                aIf=avgif(Val, Flag) default=-5.0
              on X from 0 to 4 step 1
            """);

        MakeSeriesNeonTestUtils.AssertDoubleSeries(row, "aDef", 15.0, 0.0, 30.0, 0.0);
        MakeSeriesNeonTestUtils.AssertDoubleSeries(row, "aNeg", 15.0, -1.0, 30.0, -1.0);
        MakeSeriesNeonTestUtils.AssertDoubleSeries(row, "aNull", 15.0, null, 30.0, null);
        MakeSeriesNeonTestUtils.AssertDoubleSeries(row, "aIf", 15.0, -5.0, -5.0, -5.0);
    }

    [Test]
    [Description("make-series - min(), max(), minif(), and maxif() compute per-bin extrema across numeric types and fill empty/null/zero-match bins with default")]
    public void MakeSeries_MinMax_AndConditionalVariants()
    {
        Table table = MakeSeriesNeonTestUtils.CreateTable(
            "Events",
            [
                MakeSeriesNeonTestUtils.CreateColumn("X", typeof(long), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Val", typeof(double), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Flag", typeof(bool), isNullable: true)
            ],
            [
                new Dictionary<string, object?> { { "X", 0L }, { "Val", 10.5 }, { "Flag", true } },
                new Dictionary<string, object?> { { "X", 0L }, { "Val", -2.5 }, { "Flag", false } },
                new Dictionary<string, object?> { { "X", 0L }, { "Val", 4.0 }, { "Flag", true } },
                new Dictionary<string, object?> { { "X", 1L }, { "Val", null }, { "Flag", true } },
                new Dictionary<string, object?> { { "X", 2L }, { "Val", 8.0 }, { "Flag", false } }
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            """
            Events
            | make-series
                mn=min(Val) default=-99.0,
                mx=max(Val) default=-99.0,
                mnIf=minif(Val, Flag) default=-99.0,
                mxIf=maxif(Val, Flag) default=-99.0
              on X from 0 to 4 step 1
            """);

        MakeSeriesNeonTestUtils.AssertDoubleSeries(row, "mn", -2.5, -99.0, 8.0, -99.0);
        MakeSeriesNeonTestUtils.AssertDoubleSeries(row, "mx", 10.5, -99.0, 8.0, -99.0);
        MakeSeriesNeonTestUtils.AssertDoubleSeries(row, "mnIf", 4.0, -99.0, -99.0, -99.0);
        MakeSeriesNeonTestUtils.AssertDoubleSeries(row, "mxIf", 10.5, -99.0, -99.0, -99.0);
    }

    [Test]
    [Description("make-series - min() and max() over datetime column fill empty bins with DateTime.MinValue")]
    public void MakeSeries_MinMax_DateTimeColumn()
    {
        // Oracle-verified against ADX: min(dt)/max(dt) succeed and fill empty bins with 0001-01-01T00:00:00Z.
        Table table = MakeSeriesNeonTestUtils.CreateTable(
            "Events",
            [
                MakeSeriesNeonTestUtils.CreateColumn("X", typeof(long), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("SeenAt", typeof(DateTime), isNullable: true)
            ],
            [
                new Dictionary<string, object?> { { "X", 0L }, { "SeenAt", MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 1, 0) } },
                new Dictionary<string, object?> { { "X", 0L }, { "SeenAt", MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 2, 0) } },
                new Dictionary<string, object?> { { "X", 2L }, { "SeenAt", MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 5, 0) } }
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            "Events | make-series earliest=min(SeenAt), latest=max(SeenAt) on X from 0 to 3 step 1");

        MakeSeriesNeonTestUtils.AssertDateTimeSeries(
            row,
            "earliest",
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 1, 0),
            MakeSeriesNeonTestUtils.Utc(1, 1, 1, 0, 0),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 5, 0));

        MakeSeriesNeonTestUtils.AssertDateTimeSeries(
            row,
            "latest",
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 2, 0),
            MakeSeriesNeonTestUtils.Utc(1, 1, 1, 0, 0),
            MakeSeriesNeonTestUtils.Utc(2025, 1, 1, 5, 0));
    }

    #endregion

    #region dcount(), dcountif(), count_distinct(), and count_distinctif()

    [Test]
    [Description("make-series - dcount(), dcountif(), count_distinct(), and count_distinctif() count distinct non-null values per bin")]
    public void MakeSeries_DistinctCountAggregates_PopulatedVsAbsentBins()
    {
        Table table = MakeSeriesNeonTestUtils.CreateTable(
            "Events",
            [
                MakeSeriesNeonTestUtils.CreateColumn("X", typeof(long), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("User", typeof(string), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Ok", typeof(bool), isNullable: true)
            ],
            [
                new Dictionary<string, object?> { { "X", 0L }, { "User", "u1" }, { "Ok", true } },
                new Dictionary<string, object?> { { "X", 0L }, { "User", "u1" }, { "Ok", true } },
                new Dictionary<string, object?> { { "X", 0L }, { "User", "u2" }, { "Ok", false } },
                new Dictionary<string, object?> { { "X", 1L }, { "User", null }, { "Ok", true } },
                new Dictionary<string, object?> { { "X", 2L }, { "User", "u3" }, { "Ok", false } }
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            """
            Events
            | make-series
                dc=dcount(User) default=-1,
                dcAcc=dcount(User, 2) default=-1,
                dcIf=dcountif(User, Ok) default=-1,
                cd=count_distinct(User) default=-1,
                cdIf=count_distinctif(User, Ok) default=-1
              on X from 0 to 4 step 1
            """);

        // KQL strings have no null: the missing User in bin 1 is one distinct empty string.
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "dc", 2, 1, 1, -1);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "dcAcc", 2, 1, 1, -1);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "dcIf", 1, 1, 0, -1);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "cd", 2, 1, 1, -1);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "cdIf", 1, 1, 0, -1);
    }

    #endregion

    #region Statistical, Percentile, Arbitrary, and Bitwise Aggregates

    [Test]
    [Description("make-series - stdev(), stdevp(), stdevif(), variance(), variancep(), varianceif(), and variancepif()")]
    public void MakeSeries_StdevAndVariance_AllVariants()
    {
        Table table = MakeSeriesNeonTestUtils.CreateTable(
            "Events",
            [
                MakeSeriesNeonTestUtils.CreateColumn("X", typeof(long), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Val", typeof(double), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Flag", typeof(bool), isNullable: true)
            ],
            [
                // Bin 0: [10, 20, 30] -> mean=20, sample var=100, sample stdev=10, pop var=200/3, pop stdev=sqrt(200/3)
                new Dictionary<string, object?> { { "X", 0L }, { "Val", 10.0 }, { "Flag", true } },
                new Dictionary<string, object?> { { "X", 0L }, { "Val", 20.0 }, { "Flag", true } },
                new Dictionary<string, object?> { { "X", 0L }, { "Val", 30.0 }, { "Flag", true } },
                new Dictionary<string, object?> { { "X", 0L }, { "Val", 999.0 }, { "Flag", false } },
                // Bin 2: single value [50] -> stdev=0, stdevp=0
                new Dictionary<string, object?> { { "X", 2L }, { "Val", 50.0 }, { "Flag", true } }
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            """
            Events
            | where Flag == true
            | make-series
                sd=stdev(Val) default=-1.0,
                sdp=stdevp(Val) default=-1.0,
                v=variance(Val) default=-1.0,
                vp=variancep(Val) default=-1.0
              on X from 0 to 3 step 1
            """);

        MakeSeriesNeonTestUtils.AssertDoubleSeriesWithin(row, "sd", 1e-6, 10.0, -1.0, 0.0);
        MakeSeriesNeonTestUtils.AssertDoubleSeriesWithin(row, "sdp", 1e-6, Math.Sqrt(200.0 / 3.0), -1.0, 0.0);
        MakeSeriesNeonTestUtils.AssertDoubleSeriesWithin(row, "v", 1e-6, 100.0, -1.0, 0.0);
        MakeSeriesNeonTestUtils.AssertDoubleSeriesWithin(row, "vp", 1e-6, 200.0 / 3.0, -1.0, 0.0);

        IReadOnlyDictionary<string, object?> condRow = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            """
            Events
            | make-series
                sdIf=stdevif(Val, Flag) default=-1.0,
                vIf=varianceif(Val, Flag) default=-1.0,
                vpIf=variancepif(Val, Flag) default=-1.0
              on X from 0 to 3 step 1
            """);

        MakeSeriesNeonTestUtils.AssertDoubleSeriesWithin(condRow, "sdIf", 1e-6, 10.0, -1.0, 0.0);
        MakeSeriesNeonTestUtils.AssertDoubleSeriesWithin(condRow, "vIf", 1e-6, 100.0, -1.0, 0.0);
        MakeSeriesNeonTestUtils.AssertDoubleSeriesWithin(condRow, "vpIf", 1e-6, 200.0 / 3.0, -1.0, 0.0);
    }

    [Test]
    [Description("make-series - covariance(), covariancep(), covarianceif(), and covariancepif() compute per-bin covariance")]
    public void MakeSeries_Covariance_AllVariants()
    {
        Table table = MakeSeriesNeonTestUtils.CreateTable(
            "Events",
            [
                MakeSeriesNeonTestUtils.CreateColumn("T", typeof(long), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("A", typeof(double), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("B", typeof(double), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Flag", typeof(bool), isNullable: true)
            ],
            [
                // Bin 0 (Flag==true): (1,2), (2,4), (3,6) -> sample cov = 2.0, pop cov = 4/3
                new Dictionary<string, object?> { { "T", 0L }, { "A", 1.0 }, { "B", 2.0 }, { "Flag", true } },
                new Dictionary<string, object?> { { "T", 0L }, { "A", 2.0 }, { "B", 4.0 }, { "Flag", true } },
                new Dictionary<string, object?> { { "T", 0L }, { "A", 3.0 }, { "B", 6.0 }, { "Flag", true } },
                new Dictionary<string, object?> { { "T", 0L }, { "A", 100.0 }, { "B", -100.0 }, { "Flag", false } }
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            """
            Events
            | where Flag == true
            | make-series
                cov=covariance(A, B) default=-9.0,
                covp=covariancep(A, B) default=-9.0
              on T from 0 to 2 step 1
            """);

        MakeSeriesNeonTestUtils.AssertDoubleSeriesWithin(row, "cov", 1e-6, 2.0, -9.0);
        MakeSeriesNeonTestUtils.AssertDoubleSeriesWithin(row, "covp", 1e-6, 4.0 / 3.0, -9.0);

        IReadOnlyDictionary<string, object?> condRow = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            """
            Events
            | make-series
                covIf=covarianceif(A, B, Flag) default=-9.0,
                covpIf=covariancepif(A, B, Flag) default=-9.0
              on T from 0 to 2 step 1
            """);

        MakeSeriesNeonTestUtils.AssertDoubleSeriesWithin(condRow, "covIf", 1e-6, 2.0, -9.0);
        MakeSeriesNeonTestUtils.AssertDoubleSeriesWithin(condRow, "covpIf", 1e-6, 4.0 / 3.0, -9.0);
    }

    [Test]
    [Description("make-series - percentile() and percentilew() produce numeric series and replace populated all-null or absent bins with default")]
    public void MakeSeries_PercentileAndPercentileW()
    {
        Table table = MakeSeriesNeonTestUtils.CreateTable(
            "Events",
            [
                MakeSeriesNeonTestUtils.CreateColumn("X", typeof(long), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Val", typeof(long), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Weight", typeof(long), isNullable: true)
            ],
            [
                new Dictionary<string, object?> { { "X", 0L }, { "Val", 10L }, { "Weight", 1L } },
                new Dictionary<string, object?> { { "X", 0L }, { "Val", 20L }, { "Weight", 10L } },
                new Dictionary<string, object?> { { "X", 0L }, { "Val", 30L }, { "Weight", 1L } },
                new Dictionary<string, object?> { { "X", 1L }, { "Val", null }, { "Weight", 1L } },
                new Dictionary<string, object?> { { "X", 2L }, { "Val", 5L }, { "Weight", 3L } },
                new Dictionary<string, object?> { { "X", 2L }, { "Val", 5L }, { "Weight", 3L } }
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            """
            Events
            | make-series
                p50=percentile(Val, 50) default=-1,
                p95=percentile(Val, 95) default=-1,
                pw50=percentilew(Val, Weight, 50) default=-1
              on X from 0 to 4 step 1
            """);

        MakeSeriesNeonTestUtils.AssertLongSeries(row, "p50", 20, -1, 5, -1);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "pw50", 20, -1, 5, -1);

        List<object?> p95 = MakeSeriesNeonTestUtils.ExtractSeries(row["p95"], "p95");
        Assert.That(p95, Has.Count.EqualTo(4));
        Assert.That(Convert.ToDouble(p95[0], System.Globalization.CultureInfo.InvariantCulture), Is.InRange(20.0, 30.0));
        Assert.That(Convert.ToInt64(p95[1], System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo(-1L));
        Assert.That(Convert.ToInt64(p95[2], System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo(5L));
        Assert.That(Convert.ToInt64(p95[3], System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo(-1L));
    }

    [Test]
    [Description("make-series - take_any(), any(), take_anyif(), and anyif() over numeric columns fill empty/null/zero-match bins with default")]
    public void MakeSeries_TakeAny_AndConditionalAliases()
    {
        Table table = MakeSeriesNeonTestUtils.CreateTable(
            "Events",
            [
                MakeSeriesNeonTestUtils.CreateColumn("X", typeof(long), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Val", typeof(int), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Flag", typeof(bool), isNullable: true)
            ],
            [
                new Dictionary<string, object?> { { "X", 0L }, { "Val", 10 }, { "Flag", true } },
                new Dictionary<string, object?> { { "X", 1L }, { "Val", null }, { "Flag", true } },
                new Dictionary<string, object?> { { "X", 2L }, { "Val", 25 }, { "Flag", false } }
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            """
            Events
            | make-series
                ta=take_any(Val) default=-1,
                a=any(Val) default=-1,
                taIf=take_anyif(Val, Flag) default=-1,
                aIf=anyif(Val, Flag) default=-1
              on X from 0 to 4 step 1
            """);

        MakeSeriesNeonTestUtils.AssertLongSeries(row, "ta", 10, -1, 25, -1);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "a", 10, -1, 25, -1);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "taIf", 10, -1, -1, -1);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "aIf", 10, -1, -1, -1);
    }

    [Test]
    [Description("make-series - binary_all_and(), binary_all_or(), and binary_all_xor() aggregate bitmasks per bin with default fallback")]
    public void MakeSeries_BitwiseAggregates()
    {
        Table table = MakeSeriesNeonTestUtils.CreateLongValueTable(
            [
                (0, 0b1100), // 12
                (0, 0b1010), // 10 -> AND=8, OR=14, XOR=6
                (2, 0b0111)  // 7
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            """
            Events
            | make-series
                bAnd=binary_all_and(Val) default=-1,
                bOr=binary_all_or(Val) default=0,
                bXor=binary_all_xor(Val) default=-1
              on X from 0 to 3 step 1
            """);

        MakeSeriesNeonTestUtils.AssertLongSeries(row, "bAnd", 8, -1, 7);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "bOr", 14, 0, 7);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "bXor", 6, -1, 7);
    }

    #endregion

    #region Populated Null / Zero-Match vs Absent Bin Semantics & Default Edge Cases

    [Test]
    [Description("make-series - comprehensive populated all-null / zero-match vs absent bin matrix matches ADX exact fill behavior")]
    public void MakeSeries_PopulatedNullAndZeroMatch_VsAbsentBin_Matrix()
    {
        // Directly mirrors oracle-verified ADX behavior:
        // Bin 0: (10, true); Bin 1: (null, true); Bin 2: (20, false); Bin 3: absent.
        // Aggregates yielding null/NaN (avg, avgif, min, minif, percentile, take_any) replace with default;
        // aggregates yielding 0 (countif, sum, sumif, dcount, stdev) preserve 0.
        Table table = MakeSeriesNeonTestUtils.CreateTable(
            "Events",
            [
                MakeSeriesNeonTestUtils.CreateColumn("X", typeof(long), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Val", typeof(long), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Flag", typeof(bool), isNullable: true)
            ],
            [
                new Dictionary<string, object?> { { "X", 0L }, { "Val", 10L }, { "Flag", true } },
                new Dictionary<string, object?> { { "X", 1L }, { "Val", null }, { "Flag", true } },
                new Dictionary<string, object?> { { "X", 2L }, { "Val", 20L }, { "Flag", false } }
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            """
            Events
            | make-series
                c=count() default=-1,
                cf=countif(Flag) default=-1,
                s=sum(Val) default=-1,
                sf=sumif(Val, Flag) default=-1,
                a=avg(Val) default=-1.0,
                af=avgif(Val, Flag) default=-1.0,
                mn=min(Val) default=-1,
                mnf=minif(Val, Flag) default=-1,
                dc=dcount(Val) default=-1,
                sd=stdev(Val) default=-1.0,
                p50=percentile(Val, 50) default=-1,
                ta=take_any(Val) default=-1
              on X from 0 to 4 step 1
            """);

        MakeSeriesNeonTestUtils.AssertLongSeries(row, "c", 1, 1, 1, -1);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "cf", 1, 1, 0, -1);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "s", 10, 0, 20, -1);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "sf", 10, 0, 0, -1);
        MakeSeriesNeonTestUtils.AssertDoubleSeries(row, "a", 10.0, -1.0, 20.0, -1.0);
        MakeSeriesNeonTestUtils.AssertDoubleSeries(row, "af", 10.0, -1.0, -1.0, -1.0);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "mn", 10, -1, 20, -1);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "mnf", 10, -1, -1, -1);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "dc", 1, 0, 1, -1);
        MakeSeriesNeonTestUtils.AssertDoubleSeries(row, "sd", 0.0, 0.0, 0.0, -1.0);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "p50", 10, -1, 20, -1);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "ta", 10, -1, 20, -1);
    }

    [Test]
    [Description("make-series - explicit null numeric defaults (double(null), real(null), long(null), int(null)) emit nulls in empty bins")]
    public void MakeSeries_ExplicitNullDefaults()
    {
        Table table = MakeSeriesNeonTestUtils.CreateTable(
            "Events",
            [
                MakeSeriesNeonTestUtils.CreateColumn("X", typeof(long), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Vi", typeof(int), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Vr", typeof(double), isNullable: true)
            ],
            [
                new Dictionary<string, object?> { { "X", 0L }, { "Vi", 10 }, { "Vr", 1.5 } },
                new Dictionary<string, object?> { { "X", 2L }, { "Vi", 20 }, { "Vr", 3.5 } }
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            """
            Events
            | make-series
                a1=avg(Vi) default=double(null),
                a2=sum(Vi) default=long(null),
                a3=sum(Vi) default=int(null),
                a4=avg(Vr) default=real(null)
              on X from 0 to 3 step 1
            """);

        MakeSeriesNeonTestUtils.AssertDoubleSeries(row, "a1", 10.0, null, 20.0);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "a2", 10, null, 20);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "a3", 10, null, 20);
        MakeSeriesNeonTestUtils.AssertDoubleSeries(row, "a4", 1.5, null, 3.5);
    }

    [Test]
    [Description("make-series - numeric default coercion: real default on integer aggregate truncates to integer, int default on real promotes to double, decimal default works on decimal sum")]
    public void MakeSeries_DefaultTypeCoercion_AndDecimalDefault()
    {
        // Oracle-verified against ADX: sum(int) default=1.5 produces [10, 1, 20]; sum(decimal) default=decimal(-1.5) produces [2.5, -1.5, 4.5].
        Table table = MakeSeriesNeonTestUtils.CreateTable(
            "Events",
            [
                MakeSeriesNeonTestUtils.CreateColumn("X", typeof(long), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Vi", typeof(int), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Vd", typeof(decimal), isNullable: true)
            ],
            [
                new Dictionary<string, object?> { { "X", 0L }, { "Vi", 10 }, { "Vd", 2.5m } },
                new Dictionary<string, object?> { { "X", 2L }, { "Vi", 20 }, { "Vd", 4.5m } }
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            """
            Events
            | make-series
                intWithRealDef=sum(Vi) default=1.5,
                realWithIntDef=avg(Vi) default=-1,
                decWithDef=sum(Vd) default=decimal(0)
              on X from 0 to 3 step 1
            """);

        MakeSeriesNeonTestUtils.AssertLongSeries(row, "intWithRealDef", 10, 1, 20);
        MakeSeriesNeonTestUtils.AssertDoubleSeries(row, "realWithIntDef", 10.0, -1.0, 20.0);
        MakeSeriesNeonTestUtils.AssertDecimalSeries(row, "decWithDef", 2.5m, 0m, 4.5m);
    }

    [Test]
    [Description("make-series - constant expressions and let variables work as default values, including mixed defaulted and non-defaulted series")]
    public void MakeSeries_DefaultFromLetAndConstantExpression_MixedWithImplicitDefaults()
    {
        Table table = MakeSeriesNeonTestUtils.CreateLongValueTable([(0, 10), (2, 20)]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            """
            let missingVal = -10;
            Events
            | make-series
                s1=sum(Val) default=missingVal,
                s2=sum(Val) default=(10 + 5),
                s3=sum(Val)
              on X from 0 to 3 step 1
            """);

        MakeSeriesNeonTestUtils.AssertLongSeries(row, "s1", 10, -10, 20);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "s2", 10, 15, 20);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "s3", 10, 0, 20);
    }

    #endregion

    #region Compound Expressions, Expression Arguments, Constants, and Axis Inside Aggregate

    [Test]
    [Description("make-series - compound arithmetic expressions combining multiple aggregates evaluate per bin")]
    public void MakeSeries_CompoundAggregateExpressions()
    {
        Table table = MakeSeriesNeonTestUtils.CreateLongValueTable(
            [
                (0, 10),
                (0, 20),
                (2, 6)
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            """
            Events
            | make-series
                scaled=sum(Val) * 2 + 1 default=-1,
                sumPlusCnt=sum(Val) + count() default=-1,
                ratio=todouble(sum(Val)) / todouble(count()) default=-1.0
              on X from 0 to 3 step 1
            """);

        MakeSeriesNeonTestUtils.AssertLongSeries(row, "scaled", 61, -1, 13);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "sumPlusCnt", 32, -1, 7);
        MakeSeriesNeonTestUtils.AssertDoubleSeries(row, "ratio", 15.0, -1.0, 6.0);
    }

    [Test]
    [Description("make-series - constant expression series returns constant in populated bins and default in empty bins")]
    public void MakeSeries_ConstantExpressionSeries()
    {
        // Oracle-verified against ADX: other=-1 default=-2 produces [-1, -2, -1] when bins 0 and 2 are populated and bin 1 is absent.
        Table table = MakeSeriesNeonTestUtils.CreateLongValueTable([(0, 10), (2, 20)]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            "Events | make-series s=sum(Val), marker=-1 default=-2 on X from 0 to 3 step 1");

        MakeSeriesNeonTestUtils.AssertLongSeries(row, "s", 10, 0, 20);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "marker", -1, -2, -1);
    }

    [Test]
    [Description("make-series - expressions inside aggregate arguments and referencing the axis column inside an aggregate")]
    public void MakeSeries_ExpressionsInsideAggregates_AndAxisColumnInsideAggregate()
    {
        Table table = MakeSeriesNeonTestUtils.CreateTable(
            "Orders",
            [
                MakeSeriesNeonTestUtils.CreateColumn("X", typeof(long), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Price", typeof(double), isNullable: true),
                MakeSeriesNeonTestUtils.CreateColumn("Qty", typeof(int), isNullable: true)
            ],
            [
                new Dictionary<string, object?> { { "X", 0L }, { "Price", 10.0 }, { "Qty", 2 } },
                new Dictionary<string, object?> { { "X", 1L }, { "Price", 5.5 }, { "Qty", 4 } },
                new Dictionary<string, object?> { { "X", 1L }, { "Price", 2.0 }, { "Qty", 3 } }
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            """
            Orders
            | make-series
                revenue=sum(Price * Qty),
                axisSum=sum(X),
                highValueCnt=countif(Price * Qty >= 20.0)
              on X from 0 to 3 step 1
            """);

        MakeSeriesNeonTestUtils.AssertDoubleSeries(row, "revenue", 20.0, 28.0, 0.0);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "axisSum", 0, 2, 0);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "highValueCnt", 1, 1, 0);
    }

    [Test]
    [Description("make-series - multiple distinct aggregates computed simultaneously over the same column")]
    public void MakeSeries_MultipleAggregatesOnSameColumn()
    {
        Table table = MakeSeriesNeonTestUtils.CreateLongValueTable(
            [
                (0, 2),
                (0, 4),
                (0, 6),
                (2, 10)
            ]);
        KustoDatabase db = MakeSeriesNeonTestUtils.CreateDatabase(table);

        IReadOnlyDictionary<string, object?> row = MakeSeriesNeonTestUtils.ExecuteAndGetSingleRow(
            db,
            """
            Events
            | make-series
                c=count(),
                s=sum(Val),
                a=avg(Val),
                mn=min(Val),
                mx=max(Val)
              on X from 0 to 3 step 1
            """);

        MakeSeriesNeonTestUtils.AssertLongSeries(row, "c", 3, 0, 1);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "s", 12, 0, 10);
        MakeSeriesNeonTestUtils.AssertDoubleSeries(row, "a", 4.0, 0.0, 10.0);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "mn", 2, 0, 10);
        MakeSeriesNeonTestUtils.AssertLongSeries(row, "mx", 6, 0, 10);
    }

    #endregion
}
