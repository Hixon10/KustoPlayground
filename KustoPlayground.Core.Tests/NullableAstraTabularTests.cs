using System.Text.Json;
using NullableTable = KustoPlayground.Core.Tests.SummarizeOperatorOpusTableBuilder;
using static KustoPlayground.Core.Tests.SummarizeOperatorOpusTestUtils;

namespace KustoPlayground.Core.Tests;

public partial class NullableAstraTests
{
    [Test]
    public void ExtendCanOverwriteAValueWithNullAndProjectCanRenameIt()
    {
        var db = Samples<long?>(null, 0, 5);
        var rows = ExecuteAndGetRows(db, "T | extend SourceId=Id | extend Id=V | project SourceId, Replacement=Id");
        foreach (var row in rows)
            AssertColumnsInOrder(row, "SourceId", "Replacement");
        Assert.That(rows.Select(r => (GetValue<int>(r, "SourceId"), GetNullableValue<long>(r, "Replacement"))),
            Is.EquivalentTo(new (int, long?)[] { (1, null), (2, 0), (3, 5) }));
    }

    [TestCase("T | count", 5L)]
    [TestCase("T | where V != 0 | count", 3L)]
    [TestCase("T | where V > 3 | count", 0L)]
    [TestCase("T | distinct V | count", 3L)]
    public void TabularCountCountsRowsNotNonNullCells(string query, long expected)
    {
        var counts = TestUtils.ExecuteAndGetDataForOneColumn<long>("Count", Samples<long?>(null, 0, 3, null, 0), query);
        Assert.That(counts, Is.EqualTo(new[] { expected }));
    }

    [TestCase(0, 0)]
    [TestCase(2, 2)]
    [TestCase(20, 3)]
    public void TakeDoesNotSkipRowsWithAllNullProjectedCells(int limit, int expectedCount)
    {
        var values = TestUtils.ExecuteAndGetDataForOneColumn<long?>("V", Samples<long?>(null, null, null),
            $"T | project V | take {limit}");
        Assert.That(values, Is.EqualTo(new long?[expectedCount]));
    }

    [Test]
    public void DistinctAllNullInputIsOneNullButEmptyInputHasNoDistinctValues()
    {
        const string query = "T | distinct V";
        var allNull = TestUtils.ExecuteAndGetDataForOneColumn<long?>("V", Samples<long?>(null, null), query);
        var empty = TestUtils.ExecuteAndGetDataForOneColumn<long?>("V", Samples<long?>(), query);
        Assert.That(allNull, Is.EqualTo(new long?[] { null }));
        Assert.That(empty, Is.Empty);
    }

    [TestCaseSource(nameof(NonNullControls))]
    public void DistinctRetainsOneNullAndOneOrdinaryKeyOfTheDeclaredType<T>(T control) where T : struct
    {
        var db = Samples<T?>(null, control, null, control);
        var values = TestUtils.ExecuteAndGetDataForOneColumn<T?>("V", db, "T | distinct V");
        Assert.That(values, Is.EquivalentTo(new T?[] { null, control }));
    }

    [TestCase("distinct A, B")]
    [TestCase("distinct *")]
    public void DistinctPreservesThePositionOfEveryMissingKeyComponent(string distinct)
    {
        var db = CreateDatabase(new NullableTable("T").WithColumn<long>("A").WithColumn<long>("B")
            .WithRow(null, null).WithRow(null, null).WithRow(0L, 0L)
            .WithRow(0L, null).WithRow(0L, null).WithRow(null, 0L).WithRow(null, 0L).Build());
        var keys = TestUtils.ExecuteAndGetDataFor2Columns<long?, long?>("A", "B", db, $"T | {distinct}");
        Assert.That(keys, Is.EquivalentTo(new (long?, long?)[] { (null, null), (0, 0), (0, null), (null, 0) }));
    }

    [Test]
    public void DistinctStringDoesNotCreateAnotherKeyForMissingText()
    {
        var values = TestUtils.ExecuteAndGetDataForOneColumn<string>("V",
            Samples<string?>(null, "", "null", "", " ", null), "T | distinct V");
        Assert.That(values, Is.EquivalentTo(new[] { "", "null", " " }));
    }

    [TestCase("V",                 5, 2, 6, 3, 1, 4)]
    [TestCase("V asc",             1, 4, 3, 2, 6, 5)]
    [TestCase("V desc",            5, 2, 6, 3, 1, 4)]
    [TestCase("V asc nulls first", 1, 4, 3, 2, 6, 5)]
    [TestCase("V asc nulls last",  3, 2, 6, 5, 1, 4)]
    [TestCase("V desc nulls first", 1, 4, 5, 2, 6, 3)]
    [TestCase("V desc nulls last", 5, 2, 6, 3, 1, 4)]
    public void SortUsesDirectionSpecificDefaultsAndHonorsExplicitNullPlacement(string key, params int[] expectedIds)
    {
        var ids = TestUtils.ExecuteAndGetDataForOneColumn<int>("Id", Samples<long?>(null, 0, -2, null, 3, 0),
            $"T | sort by {key}, Id asc");
        Assert.That(ids, Is.EqualTo(expectedIds));
    }

    [Test]
    public void SecondarySortKeyHasItsOwnNullPlacementEvenWhenPrimaryKeyIsNull()
    {
        var ids = TestUtils.ExecuteAndGetDataForOneColumn<int>("Id", NumberPairs(),
            "T | sort by A asc nulls last, B desc nulls first, Id asc");
        Assert.That(ids, Is.EqualTo(new[] { 5, 3, 4, 6, 1, 2 }));
    }

    // https://learn.microsoft.com/en-us/kusto/query/sort-operator#using-special-floating-point-values
    [TestCase("asc nulls first",  1, 4, 2, 3, 5)]
    [TestCase("asc nulls last",   2, 3, 5, 4, 1)]
    [TestCase("desc nulls first", 1, 4, 5, 3, 2)]
    [TestCase("desc nulls last",  5, 3, 2, 4, 1)]
    public void RealSortDistinguishesNullFromNanAndInfinities(string order, params int[] expectedIds)
    {
        var db = Samples<double?>(null, double.NegativeInfinity, 0, double.NaN, double.PositiveInfinity);
        var ids = TestUtils.ExecuteAndGetDataForOneColumn<int>("Id", db, $"T | sort by V {order}");
        Assert.That(ids, Is.EqualTo(expectedIds));
    }

    [Test]
    public void MissingBucketSurvivesFilteringDeduplicationAndOrderedTake()
    {
        var db = Samples<long?>(null, 0, 9, null, 1, 9, -2);
        var buckets = TestUtils.ExecuteAndGetDataForOneColumn<long?>("Bucket", db, """
            T
            | where V != 1
            | extend Bucket=bin(V, 5)
            | project Bucket
            | distinct Bucket
            | sort by Bucket asc nulls first
            | take 3
            """);
        Assert.That(buckets, Is.EqualTo(new long?[] { null, -5, 0 }));
    }

    [TestCase("""{"Id":1}""")]
    [TestCase("""{"Id":1,"V":null,"S":null}""")]
    public void JsonMissingAndExplicitNullEnterQueriesAccordingToColumnType(string missingRow)
    {
        var json = $$"""
            {"Name":"T","Columns":[{"Name":"Id","Type":"int"},{"Name":"V","Type":"long","Nullable":true},
                                  {"Name":"S","Type":"string","Nullable":true}],
             "Rows":[{{missingRow}},{"Id":2,"V":0,"S":""},{"Id":3,"V":7,"S":"null"}]}
            """;
        var definition = JsonSerializer.Deserialize(json, TableDefJsonContext.Default.TableDef);
        Assert.That(definition, Is.Not.Null);
        var rows = TestUtils.ExecuteAndGetDataFor3Columns<int, long?, string>("Id", "V", "S",
            CreateDatabase(TableBuilderFromJson.Build(definition)), "T | project Id, V, S");
        Assert.That(rows, Is.EquivalentTo(new (int, long?, string)[] { (1, null, ""), (2, 0, ""), (3, 7, "null") }));
    }

    [Test]
    public void CsvEmptyFieldsAreNullForNumbersButEmptyForStrings()
    {
        var table = TableBuilderFromCsv.Build(new TableDef
        {
            Name = "T",
            Columns = [new() { Name = "Id" }, new() { Name = "V" }, new() { Name = "S" }],
            Rows =
            [
                new() { ["Id"] = "1", ["V"] = "",  ["S"] = "" },
                new() { ["Id"] = "2", ["V"] = "0", ["S"] = " " },
                new() { ["Id"] = "3", ["V"] = "7", ["S"] = "null" }
            ]
        });
        var rows = TestUtils.ExecuteAndGetDataFor3Columns<int, int?, string>("Id", "V", "S",
            CreateDatabase(table), "T | project Id, V, S");
        Assert.That(rows, Is.EquivalentTo(new (int, int?, string)[] { (1, null, ""), (2, 0, " "), (3, 7, "null") }));
    }
}
