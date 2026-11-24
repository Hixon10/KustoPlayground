namespace KustoPlayground.Core.Tests;

public class SummarizeNeonStatsPercentilesAndBitwiseTests
{
    #region stdev(), stdevif(), stdevp()

    [Test]
    [Description("Summarize - stdev() computes sample standard deviation using Bessel's correction (n-1)")]
    public void Summarize_Stdev_SampleStandardDeviation()
    {
        var kustoDatabase = new KustoDatabase();
        // Matches official Microsoft Learn example: [1, 2, 3, 4, 5] => mean=3, sumSq=10, var=10/4=2.5, stdev=sqrt(2.5)
        Table table = TestUtils.GenerateTableWithColumn(
            new List<int> { 1, 2, 3, 4, 5 },
            tableName: "table1",
            columnName: "x");
        kustoDatabase.AddTable(table);

        var row = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "table1 | summarize stdev(x)");

        Assert.That(SummarizeNeonTestUtils.GetColumnNames(row), Is.EqualTo(new List<string> { "stdev_x" }));
        Assert.That(SummarizeNeonTestUtils.AsDouble(row["stdev_x"]), Is.EqualTo(Math.Sqrt(2.5)).Within(1e-9));
    }

    [Test]
    [Description("Summarize - stdevp() computes population standard deviation (n) and returns 0 for single-row group")]
    public void Summarize_Stdevp_PopulationStandardDeviationAndSingleRow()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWith2Columns(
            new List<(string, int)>
            {
                ("G1", 1), ("G1", 2), ("G1", 3), ("G1", 4), ("G1", 5),
                ("G2", 42),
                ("G3", 7), ("G3", 7), ("G3", 7)
            },
            tableName: "table1",
            columnName1: "Grp",
            columnName2: "x");
        kustoDatabase.AddTable(table);

        var rows = SummarizeNeonTestUtils.ExecuteAndAssertSuccess(
            kustoDatabase,
            "table1 | summarize stdevp(x) by Grp");

        var byGroup = rows.ToDictionary(r => (string)r["Grp"]!, r => SummarizeNeonTestUtils.AsDouble(r["stdevp_x"]));
        Assert.That(byGroup["G1"], Is.EqualTo(Math.Sqrt(2.0)).Within(1e-9));
        Assert.That(byGroup["G2"], Is.EqualTo(0.0).Within(1e-9));
        Assert.That(byGroup["G3"], Is.EqualTo(0.0).Within(1e-9));
    }

    [Test]
    [Description("Summarize - stdev() ignores nulls, returns 0 for constant values, and returns 0 on empty input")]
    public void Summarize_Stdev_NullsConstantAndEmpty()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = SummarizeNeonTestUtils.GenerateNullableTableWith2Columns<string, double?>(
            [
                ("A", 2.0), ("A", null), ("A", 4.0), ("A", 6.0), ("A", null), // [2, 4, 6] => var=8/2=4, stdev=2
                ("Const", 5.0), ("Const", 5.0), ("Const", 5.0)
            ],
            tableName: "table1",
            columnName1: "Grp",
            columnName2: "Val");
        kustoDatabase.AddTable(table);

        var rows = SummarizeNeonTestUtils.ExecuteAndAssertSuccess(
            kustoDatabase,
            "table1 | summarize stdev(Val) by Grp");

        var byGroup = rows.ToDictionary(r => (string)r["Grp"]!, r => SummarizeNeonTestUtils.AsDouble(r["stdev_Val"]));
        Assert.That(byGroup["A"], Is.EqualTo(2.0).Within(1e-9));
        Assert.That(byGroup["Const"], Is.EqualTo(0.0).Within(1e-9));

        Table empty = SummarizeNeonTestUtils.GenerateEmptyTable("EmptyTable", new Column<double>("Val", isNullable: false));
        kustoDatabase.AddTable(empty);

        var emptyRow = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "EmptyTable | summarize stdev(Val)");
        Assert.That(SummarizeNeonTestUtils.AsDouble(emptyRow["stdev_Val"]), Is.EqualTo(0.0).Within(1e-9));
    }

    [Test]
    [Description("Summarize - stdevif() filters rows by predicate and returns 0 when no rows match")]
    public void Summarize_StdevIf_GroupedAndNoMatches()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWith3Columns(
            new List<(string, double, bool)>
            {
                ("G1", 10.0, true),
                ("G1", 20.0, true),
                ("G1", 30.0, true),
                ("G1", 1000.0, false),
                ("G2", 5.0, false),
                ("G2", 15.0, false)
            },
            tableName: "table1",
            columnName1: "Grp",
            columnName2: "Val",
            columnName3: "Keep");
        kustoDatabase.AddTable(table);

        var rows = SummarizeNeonTestUtils.ExecuteAndAssertSuccess(
            kustoDatabase,
            "table1 | summarize stdevif(Val, Keep) by Grp");

        var byGroup = rows.ToDictionary(r => (string)r["Grp"]!, r => SummarizeNeonTestUtils.AsDouble(r["stdevif_Val"]));
        Assert.That(byGroup["G1"], Is.EqualTo(10.0).Within(1e-9)); // sample stdev of [10, 20, 30] = sqrt(200/2) = 10
        Assert.That(byGroup["G2"], Is.EqualTo(0.0).Within(1e-9));
    }

    #endregion

    #region variance(), varianceif(), variancep(), variancepif()

    [Test]
    [Description("Summarize - variance() and variancep() compute sample (n-1) and population (n) variance with exact default column names")]
    public void Summarize_Variance_AndVarianceP()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWithColumn(
            new List<int> { 1, 2, 3, 4, 5 },
            tableName: "table1",
            columnName: "x");
        kustoDatabase.AddTable(table);

        var row = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "table1 | summarize variance(x), variancep(x)");

        Assert.That(SummarizeNeonTestUtils.GetColumnNames(row), Is.EqualTo(new List<string> { "variance_x", "variancep_x" }));
        Assert.That(SummarizeNeonTestUtils.AsDouble(row["variance_x"]), Is.EqualTo(2.5).Within(1e-9));
        Assert.That(SummarizeNeonTestUtils.AsDouble(row["variancep_x"]), Is.EqualTo(2.0).Within(1e-9));
    }

    [Test]
    [Description("Summarize - varianceif() and variancepif() respect predicate, ignore nulls, and return 0 on empty matches")]
    public void Summarize_VarianceIf_AndVariancePIf()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = SummarizeNeonTestUtils.GenerateNullableTableWith3Columns<string, double?, bool>(
            [
                ("G1", 2.0, true),
                ("G1", 4.0, true),
                ("G1", 6.0, true),
                ("G1", null, true),
                ("G1", 500.0, false),
                ("G2", 10.0, false),
                ("G2", 20.0, false)
            ],
            tableName: "table1",
            columnName1: "Grp",
            columnName2: "x",
            columnName3: "Valid");
        kustoDatabase.AddTable(table);

        var rows = SummarizeNeonTestUtils.ExecuteAndAssertSuccess(
            kustoDatabase,
            "table1 | summarize varianceif(x, Valid), variancepif(x, Valid) by Grp");

        var byGroup = rows.ToDictionary(
            r => (string)r["Grp"]!,
            r => (Var: SummarizeNeonTestUtils.AsDouble(r["varianceif_x"]), VarP: SummarizeNeonTestUtils.AsDouble(r["variancepif_x"])));

        Assert.That(byGroup["G1"].Var, Is.EqualTo(4.0).Within(1e-9));       // 8 / 2
        Assert.That(byGroup["G1"].VarP, Is.EqualTo(8.0 / 3.0).Within(1e-9)); // 8 / 3
        Assert.That(byGroup["G2"].Var, Is.EqualTo(0.0).Within(1e-9));
        Assert.That(byGroup["G2"].VarP, Is.EqualTo(0.0).Within(1e-9));
    }

    [Test]
    [Description("Summarize - variance() on empty table without by returns 0")]
    public void Summarize_Variance_EmptyTableReturnsZero()
    {
        var kustoDatabase = new KustoDatabase();
        Table empty = SummarizeNeonTestUtils.GenerateEmptyTable("EmptyTable", new Column<double>("x", isNullable: false));
        kustoDatabase.AddTable(empty);

        var row = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "EmptyTable | summarize variance(x), variancep(x)");

        Assert.That(SummarizeNeonTestUtils.AsDouble(row["variance_x"]), Is.EqualTo(0.0).Within(1e-9));
        Assert.That(SummarizeNeonTestUtils.AsDouble(row["variancep_x"]), Is.EqualTo(0.0).Within(1e-9));
    }

    #endregion

    #region covariance(), covariancep(), covarianceif(), covariancepif()

    [Test]
    [Description("Summarize - covariance() and covariancep() compute sample and population covariance with positive and negative correlation")]
    public void Summarize_Covariance_AndCovarianceP()
    {
        var kustoDatabase = new KustoDatabase();
        // Pos: x=[1,2,3,4], y=[2,4,6,8] => meanX=2.5, meanY=5, sum((x-mx)(y-my))=10 => cov=10/3, covp=10/4=2.5
        // Neg: x=[1,2,3], y=[9,6,3] => meanX=2, meanY=6, sum((x-mx)(y-my))=-6 => cov=-6/2=-3, covp=-6/3=-2
        Table table = TestUtils.GenerateTableWith3Columns(
            new List<(string, double, double)>
            {
                ("Pos", 1.0, 2.0), ("Pos", 2.0, 4.0), ("Pos", 3.0, 6.0), ("Pos", 4.0, 8.0),
                ("Neg", 1.0, 9.0), ("Neg", 2.0, 6.0), ("Neg", 3.0, 3.0)
            },
            tableName: "Pairs",
            columnName1: "Grp",
            columnName2: "X",
            columnName3: "Y");
        kustoDatabase.AddTable(table);

        var rows = SummarizeNeonTestUtils.ExecuteAndAssertSuccess(
            kustoDatabase,
            "Pairs | summarize covariance(X, Y), covariancep(X, Y) by Grp");

        Assert.That(
            SummarizeNeonTestUtils.GetColumnNames(rows[0]),
            Is.EqualTo(new List<string> { "Grp", "covariance_X", "covariancep_X" }));

        var byGroup = rows.ToDictionary(
            r => (string)r["Grp"]!,
            r => (Cov: SummarizeNeonTestUtils.AsDouble(r["covariance_X"]), CovP: SummarizeNeonTestUtils.AsDouble(r["covariancep_X"])));

        Assert.That(byGroup["Pos"].Cov, Is.EqualTo(10.0 / 3.0).Within(1e-9));
        Assert.That(byGroup["Pos"].CovP, Is.EqualTo(2.5).Within(1e-9));
        Assert.That(byGroup["Neg"].Cov, Is.EqualTo(-3.0).Within(1e-9));
        Assert.That(byGroup["Neg"].CovP, Is.EqualTo(-2.0).Within(1e-9));
    }

    [Test]
    [Description("Summarize - covarianceif() and covariancepif() filter pairs by predicate and skip pairs with null in either operand")]
    public void Summarize_CovarianceIf_AndNullPairSkipping()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = SummarizeNeonTestUtils.GenerateTableWithSchema(
            "Pairs",
            [
                new Column<double>("X", isNullable: true),
                new Column<double>("Y", isNullable: true),
                new Column<bool>("Keep", isNullable: false)
            ],
            [
                new() { ["X"] = 1.0,  ["Y"] = 10.0, ["Keep"] = true },
                new() { ["X"] = 2.0,  ["Y"] = 20.0, ["Keep"] = true },
                new() { ["X"] = 3.0,  ["Y"] = 30.0, ["Keep"] = true },
                new() { ["X"] = null, ["Y"] = 999.0, ["Keep"] = true }, // skipped (X is null)
                new() { ["X"] = 999.0, ["Y"] = null, ["Keep"] = true }, // skipped (Y is null)
                new() { ["X"] = 100.0, ["Y"] = -500.0, ["Keep"] = false } // skipped (Keep is false)
            ]);
        kustoDatabase.AddTable(table);

        var row = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "Pairs | summarize covarianceif(X, Y, Keep), covariancepif(X, Y, Keep)");

        Assert.That(
            SummarizeNeonTestUtils.GetColumnNames(row),
            Is.EqualTo(new List<string> { "covarianceif_X", "covariancepif_X" }));
        // Valid pairs: (1,10), (2,20), (3,30) => sum((x-2)(y-20)) = 20 => cov=20/2=10, covp=20/3
        Assert.That(SummarizeNeonTestUtils.AsDouble(row["covarianceif_X"]), Is.EqualTo(10.0).Within(1e-9));
        Assert.That(SummarizeNeonTestUtils.AsDouble(row["covariancepif_X"]), Is.EqualTo(20.0 / 3.0).Within(1e-9));
    }

    #endregion

    #region percentile(), percentiles(), percentiles_array(), percentilew(), percentilesw(), percentilesw_array()

    [Test]
    [Description("Summarize - percentile() boundary values (0 and 100), median (50), and decimal percentile column naming (99_5)")]
    public void Summarize_Percentile_BoundariesMedianAndDecimalName()
    {
        var kustoDatabase = new KustoDatabase();
        List<double> values = Enumerable.Range(1, 100).Select(i => (double)i).ToList();
        Table table = TestUtils.GenerateTableWithColumn(values, tableName: "Metrics", columnName: "Latency");
        kustoDatabase.AddTable(table);

        var row = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "Metrics | summarize percentile(Latency, 0), percentile(Latency, 50), percentile(Latency, 95), percentile(Latency, 99.5), percentile(Latency, 100)");

        Assert.That(
            SummarizeNeonTestUtils.GetColumnNames(row),
            Is.EqualTo(new List<string>
            {
                "percentile_Latency_0",
                "percentile_Latency_50",
                "percentile_Latency_95",
                "percentile_Latency_99_5",
                "percentile_Latency_100"
            }));

        double p0 = SummarizeNeonTestUtils.AsDouble(row["percentile_Latency_0"]);
        double p50 = SummarizeNeonTestUtils.AsDouble(row["percentile_Latency_50"]);
        double p95 = SummarizeNeonTestUtils.AsDouble(row["percentile_Latency_95"]);
        double p995 = SummarizeNeonTestUtils.AsDouble(row["percentile_Latency_99_5"]);
        double p100 = SummarizeNeonTestUtils.AsDouble(row["percentile_Latency_100"]);

        Assert.That(p0, Is.EqualTo(1.0).Within(1e-6));
        Assert.That(p50, Is.InRange(49.0, 51.0));
        Assert.That(p95, Is.InRange(94.0, 96.0));
        Assert.That(p995, Is.InRange(98.5, 100.0));
        Assert.That(p100, Is.EqualTo(100.0).Within(1e-6));
        Assert.That(p0, Is.LessThanOrEqualTo(p50));
        Assert.That(p50, Is.LessThanOrEqualTo(p95));
        Assert.That(p95, Is.LessThanOrEqualTo(p995));
        Assert.That(p995, Is.LessThanOrEqualTo(p100));
    }

    [Test]
    [Description("Summarize - percentile() on constant values returns exact value regardless of percentile rank")]
    public void Summarize_Percentile_ConstantValues()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWithColumn(
            new List<int> { 7, 7, 7, 7, 7 },
            tableName: "table1",
            columnName: "Val");
        kustoDatabase.AddTable(table);

        var row = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "table1 | summarize P0 = percentile(Val, 0), P50 = percentile(Val, 50), P99 = percentile(Val, 99), P100 = percentile(Val, 100)");

        Assert.That(SummarizeNeonTestUtils.AsDouble(row["P0"]), Is.EqualTo(7.0).Within(1e-9));
        Assert.That(SummarizeNeonTestUtils.AsDouble(row["P50"]), Is.EqualTo(7.0).Within(1e-9));
        Assert.That(SummarizeNeonTestUtils.AsDouble(row["P99"]), Is.EqualTo(7.0).Within(1e-9));
        Assert.That(SummarizeNeonTestUtils.AsDouble(row["P100"]), Is.EqualTo(7.0).Within(1e-9));
    }

    [Test]
    [Description("Summarize - percentile() on DateTime and TimeSpan columns")]
    public void Summarize_Percentile_DateTimeAndTimeSpan()
    {
        var kustoDatabase = new KustoDatabase();
        var baseTime = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var rows = Enumerable.Range(0, 11)
            .Select(i => (baseTime.AddMinutes(i * 10), TimeSpan.FromSeconds(i * 10)))
            .ToList();
        Table table = TestUtils.GenerateTableWith2Columns(rows, tableName: "Events", columnName1: "Ts", columnName2: "Dur");
        kustoDatabase.AddTable(table);

        var row = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "Events | summarize MinTs = percentile(Ts, 0), MedTs = percentile(Ts, 50), MaxTs = percentile(Ts, 100), MedDur = percentile(Dur, 50)");

        Assert.That((DateTime)row["MinTs"]!, Is.EqualTo(baseTime));
        Assert.That((DateTime)row["MaxTs"]!, Is.EqualTo(baseTime.AddMinutes(100)));
        Assert.That((DateTime)row["MedTs"]!, Is.InRange(baseTime.AddMinutes(40), baseTime.AddMinutes(60)));
        Assert.That((TimeSpan)row["MedDur"]!, Is.InRange(TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(60)));
    }

    [Test]
    [Description("Summarize - percentile() ignores nulls and returns null on empty table or all-null group")]
    public void Summarize_Percentile_NullsAndEmptyTable()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = SummarizeNeonTestUtils.GenerateNullableTableWith2Columns<string, double?>(
            [
                ("G1", 10.0), ("G1", null), ("G1", 10.0), ("G1", null),
                ("G2", null), ("G2", null)
            ],
            tableName: "table1",
            columnName1: "Grp",
            columnName2: "Val");
        kustoDatabase.AddTable(table);

        var rows = SummarizeNeonTestUtils.ExecuteAndAssertSuccess(
            kustoDatabase,
            "table1 | summarize percentile(Val, 50) by Grp");

        var byGroup = rows.ToDictionary(r => (string)r["Grp"]!, r => r["percentile_Val_50"]);
        Assert.That(SummarizeNeonTestUtils.AsDouble(byGroup["G1"]), Is.EqualTo(10.0).Within(1e-9));
        Assert.That(byGroup["G2"], Is.Null);

        Table empty = SummarizeNeonTestUtils.GenerateEmptyTable("EmptyTable", new Column<double>("Val", isNullable: false));
        kustoDatabase.AddTable(empty);

        var emptyRow = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "EmptyTable | summarize percentile(Val, 50)");
        Assert.That(emptyRow["percentile_Val_50"], Is.Null);
    }

    [Test]
    [Description("Summarize - percentiles() expands multiple output columns with default names, tuple aliases, and partial aliases")]
    public void Summarize_Percentiles_DefaultTupleAndPartialAliases()
    {
        var kustoDatabase = new KustoDatabase();
        List<int> values = Enumerable.Range(1, 100).ToList();
        Table table = TestUtils.GenerateTableWithColumn(values, tableName: "table1", columnName: "Val");
        kustoDatabase.AddTable(table);

        // Default column names
        var defaultRow = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "table1 | summarize percentiles(Val, 50, 95, 99.9)");
        Assert.That(
            SummarizeNeonTestUtils.GetColumnNames(defaultRow),
            Is.EqualTo(new List<string> { "percentile_Val_50", "percentile_Val_95", "percentile_Val_99_9" }));

        // Tuple aliasing
        var tupleRow = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "table1 | summarize (P50, P95) = percentiles(Val, 50, 95)");
        Assert.That(
            SummarizeNeonTestUtils.GetColumnNames(tupleRow),
            Is.EqualTo(new List<string> { "P50", "P95" }));
        Assert.That(SummarizeNeonTestUtils.AsDouble(tupleRow["P50"]), Is.InRange(49.0, 51.0));
        Assert.That(SummarizeNeonTestUtils.AsDouble(tupleRow["P95"]), Is.InRange(94.0, 96.0));

        // Partial single alias names the first column and keeps default name for remaining columns
        var partialRow = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "table1 | summarize Median = percentiles(Val, 50, 95)");
        Assert.That(
            SummarizeNeonTestUtils.GetColumnNames(partialRow),
            Is.EqualTo(new List<string> { "Median", "percentile_Val_95" }));
    }

    [Test]
    [Description("Summarize - percentiles_array() returns dynamic array of percentile values in requested order")]
    public void Summarize_PercentilesArray_DefaultAndGrouped()
    {
        var kustoDatabase = new KustoDatabase();
        var rows = new List<(string, double)>();
        for (int i = 1; i <= 100; i++)
        {
            rows.Add(("Low", i));
            rows.Add(("High", i + 1000));
        }

        Table table = TestUtils.GenerateTableWith2Columns(rows, tableName: "Metrics", columnName1: "Tier", columnName2: "Val");
        kustoDatabase.AddTable(table);

        var resultRows = SummarizeNeonTestUtils.ExecuteAndAssertSuccess(
            kustoDatabase,
            "Metrics | summarize percentiles_array(Val, 0, 50, 100) by Tier");

        Assert.That(
            SummarizeNeonTestUtils.GetColumnNames(resultRows[0]),
            Is.EqualTo(new List<string> { "Tier", "percentiles_Val" }));

        var byTier = resultRows.ToDictionary(
            r => (string)r["Tier"]!,
            r => SummarizeNeonTestUtils.ExtractTypedDynamicArray<double>(r["percentiles_Val"]));

        Assert.That(byTier["Low"], Has.Count.EqualTo(3));
        Assert.That(byTier["Low"][0], Is.EqualTo(1.0).Within(1e-6));
        Assert.That(byTier["Low"][1], Is.InRange(49.0, 51.0));
        Assert.That(byTier["Low"][2], Is.EqualTo(100.0).Within(1e-6));

        Assert.That(byTier["High"], Has.Count.EqualTo(3));
        Assert.That(byTier["High"][0], Is.EqualTo(1001.0).Within(1e-6));
        Assert.That(byTier["High"][1], Is.InRange(1049.0, 1051.0));
        Assert.That(byTier["High"][2], Is.EqualTo(1100.0).Within(1e-6));
    }

    [Test]
    [Description("Summarize - percentilew(), percentilesw(), and percentilesw_array() weight observations by frequency column")]
    public void Summarize_PercentileW_WeightedDistributionAndZeroWeight()
    {
        var kustoDatabase = new KustoDatabase();
        // Value 10 has weight 90, value 100 has weight 10, value 999 has weight 0 (ignored)
        // Unweighted median would be 100, but weighted median must be 10!
        Table table = TestUtils.GenerateTableWith2Columns(
            new List<(int, int)>
            {
                (10, 90),
                (100, 10),
                (999, 0)
            },
            tableName: "Buckets",
            columnName1: "Val",
            columnName2: "Weight");
        kustoDatabase.AddTable(table);

        var row = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "Buckets | summarize percentilew(Val, Weight, 50), (WP50, WP95) = percentilesw(Val, Weight, 50, 95), WArr = percentilesw_array(Val, Weight, 50, 95)");

        Assert.That(SummarizeNeonTestUtils.AsDouble(row["percentile_Val_50"]), Is.EqualTo(10.0).Within(1.0));
        Assert.That(SummarizeNeonTestUtils.AsDouble(row["WP50"]), Is.EqualTo(10.0).Within(1.0));
        Assert.That(SummarizeNeonTestUtils.AsDouble(row["WP95"]), Is.EqualTo(100.0).Within(1.0));

        var arr = SummarizeNeonTestUtils.ExtractTypedDynamicArray<double>(row["WArr"]);
        Assert.That(arr, Has.Count.EqualTo(2));
        Assert.That(arr[0], Is.EqualTo(10.0).Within(1.0));
        Assert.That(arr[1], Is.EqualTo(100.0).Within(1.0));
    }

    #endregion

    #region binary_all_and(), binary_all_or(), binary_all_xor()

    [Test]
    [Description("Summarize - binary_all_and(), binary_all_or(), and binary_all_xor() aggregate bitmasks across int and long columns")]
    public void Summarize_BinaryAll_AndOrXor_IntAndLong()
    {
        var kustoDatabase = new KustoDatabase();
        // 0b1100 (12), 0b1010 (10), 0b1110 (14)
        // AND = 0b1000 (8)
        // OR  = 0b1110 (14)
        // XOR = 12 ^ 10 ^ 14 = 6 ^ 14 = 0b1000 (8)
        Table table = TestUtils.GenerateTableWith2Columns(
            new List<(int, long)>
            {
                (0b1100, 0x00FF_0000_0000_000FL),
                (0b1010, 0x000F_0000_0000_00F0L),
                (0b1110, 0x00F0_0000_0000_00FFL)
            },
            tableName: "Flags",
            columnName1: "Mask",
            columnName2: "BigMask");
        kustoDatabase.AddTable(table);

        var row = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "Flags | summarize AndVal = binary_all_and(Mask), OrVal = binary_all_or(Mask), XorVal = binary_all_xor(Mask), BigOr = binary_all_or(BigMask)");

        Assert.That(SummarizeNeonTestUtils.AsLong(row["AndVal"]), Is.EqualTo(0b1000L));
        Assert.That(SummarizeNeonTestUtils.AsLong(row["OrVal"]), Is.EqualTo(0b1110L));
        Assert.That(SummarizeNeonTestUtils.AsLong(row["XorVal"]), Is.EqualTo(0b1000L));
        Assert.That((long)row["BigOr"]!, Is.EqualTo(0x00FF_0000_0000_00FFL));
    }

    [Test]
    [Description("Summarize - binary_all_* default output column names use source column name with numeric disambiguation")]
    public void Summarize_BinaryAll_DefaultColumnNamesAndGrouped()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWith2Columns(
            new List<(string, int)>
            {
                ("G1", 0b0111),
                ("G1", 0b0011),
                ("G2", 0b1001)
            },
            tableName: "Flags",
            columnName1: "Grp",
            columnName2: "Bits");
        kustoDatabase.AddTable(table);

        var rows = SummarizeNeonTestUtils.ExecuteAndAssertSuccess(
            kustoDatabase,
            "Flags | summarize binary_all_and(Bits), binary_all_or(Bits), binary_all_xor(Bits) by Grp");

        Assert.That(
            SummarizeNeonTestUtils.GetColumnNames(rows[0]),
            Is.EqualTo(new List<string> { "Grp", "Bits", "Bits1", "Bits2" }));

        var byGroup = rows.ToDictionary(
            r => (string)r["Grp"]!,
            r => (
                And: SummarizeNeonTestUtils.AsLong(r["Bits"]),
                Or: SummarizeNeonTestUtils.AsLong(r["Bits1"]),
                Xor: SummarizeNeonTestUtils.AsLong(r["Bits2"])));

        Assert.That(byGroup["G1"], Is.EqualTo((0b0011L, 0b0111L, 0b0100L)));
        Assert.That(byGroup["G2"], Is.EqualTo((0b1001L, 0b1001L, 0b1001L)));
    }

    [Test]
    [Description("Summarize - binary_all_* ignores null values within a group")]
    public void Summarize_BinaryAll_IgnoresNulls()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = SummarizeNeonTestUtils.GenerateNullableTableWithColumn<int?>(
            [0b1100, null, 0b1010, null],
            tableName: "Flags",
            columnName: "Bits");
        kustoDatabase.AddTable(table);

        var row = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "Flags | summarize AndVal = binary_all_and(Bits), OrVal = binary_all_or(Bits), XorVal = binary_all_xor(Bits)");

        Assert.That(SummarizeNeonTestUtils.AsLong(row["AndVal"]), Is.EqualTo(0b1000L));
        Assert.That(SummarizeNeonTestUtils.AsLong(row["OrVal"]), Is.EqualTo(0b1110L));
        Assert.That(SummarizeNeonTestUtils.AsLong(row["XorVal"]), Is.EqualTo(0b0110L));
    }

    #endregion
}
