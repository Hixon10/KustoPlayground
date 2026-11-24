using static KustoPlayground.Core.Tests.JoinOperatorOpusTestUtils;
using static KustoPlayground.Core.Tests.NullableOpusTestUtils;
using static KustoPlayground.Core.Tests.SummarizeOperatorOpusTestUtils;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// Every join kind on the same inputs, as in Azure Data Explorer.
/// - L(LeftId, Key) = 1 a, 2 a, 3 b, 4 c and R(Key, RightId) = a 10, a 20, a 30, b 40, d 50: the key a is repeated on
///   both sides (2 x 3 pairs), b matches once, c is only on the left and d only on the right;
/// - inner returns the 7 pairs, innerunique 4 (one of the two left rows of a with the 3 right rows of a, and the pair
///   of b), leftouter adds the left-only row c (8), rightouter the right-only row d (8) and fullouter both (9), with
///   the columns of the missing side null (empty for a string column);
/// - leftsemi returns the 3 left rows with a match, leftanti (alias anti, leftantisemi) the left row c, rightsemi the
///   4 right rows with a match and rightanti (alias rightantisemi) the right row d, each with the columns of its side;
/// - with the sides swapped the left and right kinds trade places and innerunique keeps one of the three rows of a;
/// - an empty side gives no row for the inner and semi kinds and every row of the other side for the outer and anti
///   kinds that keep that side; a self join matches every row with at least itself;
/// - the generated tables BigLeft (100 rows, 10 per key 0..9) and BigRight (50 rows, 10 per key 0..4) check the same
///   rules on more rows: 100 pairs per matching key and 50 left rows without a match.
/// The rows of a join come in any order, so the results are compared in any order.
/// The expected values were verified on the help cluster (https://help.kusto.windows.net).
/// </summary>
public class JoinOperatorOpusKindMatrixTests
{
    #region row counts

    [TestCase("", 4L)]
    [TestCase("kind=innerunique", 4L)]
    [TestCase("kind=inner", 7L)]
    [TestCase("kind=leftouter", 8L)]
    [TestCase("kind=rightouter", 8L)]
    [TestCase("kind=fullouter", 9L)]
    [TestCase("kind=leftsemi", 3L)]
    [TestCase("kind=leftanti", 1L)]
    [TestCase("kind=anti", 1L)]
    [TestCase("kind=leftantisemi", 1L)]
    [TestCase("kind=rightsemi", 4L)]
    [TestCase("kind=rightanti", 1L)]
    [TestCase("kind=rightantisemi", 1L)]
    [Description("The row count of every kind and alias on L and R")]
    public void Join_RowCountPerKind(string kindClause, long expectedRows)
    {
        var kustoDatabase = CreateKindMatrixDatabase();

        Assert.That(ExecuteAndGetCount(kustoDatabase, $"L | join {kindClause} R on Key | count"),
            Is.EqualTo(expectedRows));
    }

    [TestCase("", 3L)]
    [TestCase("kind=inner", 7L)]
    [TestCase("kind=leftouter", 8L)]
    [TestCase("kind=rightouter", 8L)]
    [TestCase("kind=fullouter", 9L)]
    [TestCase("kind=leftsemi", 4L)]
    [TestCase("kind=leftanti", 1L)]
    [TestCase("kind=rightsemi", 3L)]
    [TestCase("kind=rightanti", 1L)]
    [Description("The row count of every kind with the sides swapped (R | join L): innerunique keeps one of the three " +
                 "rows of a with the two rows of a of L (2 + 1), and the left and right kinds trade places")]
    public void Join_RowCountPerKind_WithTheSidesSwapped(string kindClause, long expectedRows)
    {
        var kustoDatabase = CreateKindMatrixDatabase();

        Assert.That(ExecuteAndGetCount(kustoDatabase, $"R | join {kindClause} L on Key | count"),
            Is.EqualTo(expectedRows));
    }

    [TestCase("", 4L)]
    [TestCase("kind=inner", 6L)]
    [TestCase("kind=leftouter", 6L)]
    [TestCase("kind=rightouter", 6L)]
    [TestCase("kind=fullouter", 6L)]
    [TestCase("kind=leftsemi", 4L)]
    [TestCase("kind=leftanti", 0L)]
    [TestCase("kind=rightsemi", 4L)]
    [TestCase("kind=rightanti", 0L)]
    [Description("A self join of L: every row matches at least itself, so inner and the outer kinds return the same " +
                 "2 x 2 + 1 + 1 pairs, innerunique one row of a with both rows of a (2 + 1 + 1), the semi kinds every " +
                 "row and the anti kinds none")]
    public void Join_SelfJoin_RowCountPerKind(string kindClause, long expectedRows)
    {
        var kustoDatabase = CreateKindMatrixDatabase();

        Assert.That(ExecuteAndGetCount(kustoDatabase, $"L | join {kindClause} L on Key | count"),
            Is.EqualTo(expectedRows));
    }

    [TestCase("", 50L)]
    [TestCase("kind=inner", 500L)]
    [TestCase("kind=leftouter", 550L)]
    [TestCase("kind=rightouter", 500L)]
    [TestCase("kind=fullouter", 550L)]
    [TestCase("kind=leftsemi", 50L)]
    [TestCase("kind=leftanti", 50L)]
    [TestCase("kind=rightsemi", 50L)]
    [TestCase("kind=rightanti", 0L)]
    [Description("The row count of every kind on the generated tables: the keys 0..4 pair 10 left rows with 10 right " +
                 "rows (100 pairs each), the 50 left rows of the keys 5..9 have no match and every right row has one")]
    public void Join_RowCountPerKind_OnGeneratedTables(string kindClause, long expectedRows)
    {
        var kustoDatabase = CreateGeneratedDatabase();

        Assert.That(ExecuteAndGetCount(kustoDatabase, $"BigLeft | join {kindClause} BigRight on Key | count"),
            Is.EqualTo(expectedRows));
    }

    [TestCase("", 50L)]
    [TestCase("kind=inner", 500L)]
    [TestCase("kind=leftouter", 500L)]
    [TestCase("kind=rightouter", 550L)]
    [TestCase("kind=fullouter", 550L)]
    [TestCase("kind=leftsemi", 50L)]
    [TestCase("kind=leftanti", 0L)]
    [TestCase("kind=rightsemi", 50L)]
    [TestCase("kind=rightanti", 50L)]
    [Description("The row count of every kind on the generated tables with the sides swapped (BigRight | join " +
                 "BigLeft): innerunique keeps one right row per key with its 10 left rows (5 x 10)")]
    public void Join_RowCountPerKind_OnGeneratedTablesWithTheSidesSwapped(string kindClause, long expectedRows)
    {
        var kustoDatabase = CreateGeneratedDatabase();

        Assert.That(ExecuteAndGetCount(kustoDatabase, $"BigRight | join {kindClause} BigLeft on Key | count"),
            Is.EqualTo(expectedRows));
    }

    #endregion

    #region rows

    [TestCase("kind=inner",
        new[] { "1:a:a:10", "1:a:a:20", "1:a:a:30", "2:a:a:10", "2:a:a:20", "2:a:a:30", "3:b:b:40" })]
    [TestCase("kind=leftouter",
        new[] { "1:a:a:10", "1:a:a:20", "1:a:a:30", "2:a:a:10", "2:a:a:20", "2:a:a:30", "3:b:b:40", "4:c::" })]
    [TestCase("kind=rightouter",
        new[] { "1:a:a:10", "1:a:a:20", "1:a:a:30", "2:a:a:10", "2:a:a:20", "2:a:a:30", "3:b:b:40", "::d:50" })]
    [TestCase("kind=fullouter",
        new[]
        {
            "1:a:a:10", "1:a:a:20", "1:a:a:30", "2:a:a:10", "2:a:a:20", "2:a:a:30", "3:b:b:40", "4:c::", "::d:50"
        })]
    [Description("The rows (LeftId:Key:Key1:RightId) of inner and the outer kinds: every pair of a (2 x 3) and of b, " +
                 "plus the left-only row c for leftouter and fullouter and the right-only row d for rightouter and " +
                 "fullouter, with the missing side empty")]
    public void Join_RowsPerKind(string kindClause, string[] expectedRows)
    {
        var kustoDatabase = CreateKindMatrixDatabase();

        Assert.That(
            ExecuteAndGetRowTexts(kustoDatabase, $"L | join {kindClause} R on Key", "LeftId", "Key", "Key1", "RightId"),
            Is.EquivalentTo(expectedRows));
    }

    [Test]
    [Description("innerunique keeps one of the two left rows of a (which one is not guaranteed) with the three right " +
                 "rows of a, and the single pair of b")]
    public void Join_InnerUnique_KeepsOneLeftRowPerKey()
    {
        var kustoDatabase = CreateKindMatrixDatabase();

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows =
            ExecuteAndGetRows(kustoDatabase, "L | join kind=innerunique R on Key");

        Assert.That(rows, Has.Count.EqualTo(4));
        AssertOneLeftRowPairedWithEveryRightRow(GetRowsWhere(rows, "Key", "a"),
            new[] { "LeftId" }, new[] { "1", "2" },
            new[] { "Key1", "RightId" }, new[] { "a:10", "a:20", "a:30" });
        Assert.That(GetRowsWhere(rows, "Key", "b").Select(row => FormatRow(row, "LeftId", "Key", "Key1", "RightId")),
            Is.EqualTo(new[] { "3:b:b:40" }));
    }

    [Test]
    [Description("With the sides swapped, innerunique keeps one of the three rows of a of R (which one is not " +
                 "guaranteed) with the two rows of a of L, and the single pair of b")]
    public void Join_InnerUnique_WithTheSidesSwapped_KeepsOneOfTheThreeRowsOfA()
    {
        var kustoDatabase = CreateKindMatrixDatabase();

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = ExecuteAndGetRows(kustoDatabase, "R | join L on Key");

        Assert.That(rows, Has.Count.EqualTo(3));
        AssertOneLeftRowPairedWithEveryRightRow(GetRowsWhere(rows, "Key", "a"),
            new[] { "RightId" }, new[] { "10", "20", "30" },
            new[] { "Key1", "LeftId" }, new[] { "a:1", "a:2" });
        Assert.That(GetRowsWhere(rows, "Key", "b").Select(row => FormatRow(row, "Key", "RightId", "Key1", "LeftId")),
            Is.EqualTo(new[] { "b:40:b:3" }));
    }

    [TestCase("kind=leftsemi", new[] { "1:a", "2:a", "3:b" })]
    [TestCase("kind=leftanti", new[] { "4:c" })]
    [TestCase("kind=anti", new[] { "4:c" })]
    [TestCase("kind=leftantisemi", new[] { "4:c" })]
    [TestCase("kind=rightsemi", new[] { "a:10", "a:20", "a:30", "b:40" })]
    [TestCase("kind=rightanti", new[] { "d:50" })]
    [TestCase("kind=rightantisemi", new[] { "d:50" })]
    [Description("The semi and anti kinds return whole rows of one side with its columns only: leftsemi the left rows " +
                 "with a match (each once, although a has three right rows), leftanti the left row without a match, " +
                 "rightsemi the right rows with a match (each once, although a has two left rows) and rightanti the " +
                 "right row without a match")]
    public void Join_SemiAndAntiRowsPerKind(string kindClause, string[] expectedRows)
    {
        var kustoDatabase = CreateKindMatrixDatabase();

        Assert.That(ExecuteAndGetWholeRowTexts(kustoDatabase, $"L | join {kindClause} R on Key"),
            Is.EquivalentTo(expectedRows));
    }

    [TestCase("kind=inner", "kind=inner")]
    [TestCase("kind=leftouter", "kind=rightouter")]
    [TestCase("kind=rightouter", "kind=leftouter")]
    [TestCase("kind=fullouter", "kind=fullouter")]
    [Description("Swapping the sides and the kind returns the same pairs: L | join kind=leftouter R pairs the same " +
                 "left and right rows as R | join kind=rightouter L, although its columns come in another order")]
    public void Join_SwappingTheSidesAndTheKind_ReturnsTheSamePairs(string kindClause, string swappedKindClause)
    {
        var kustoDatabase = CreateKindMatrixDatabase();

        List<string> pairs =
            ExecuteAndGetRowTexts(kustoDatabase, $"L | join {kindClause} R on Key", "LeftId", "RightId");

        Assert.That(pairs, Is.Not.Empty);
        Assert.That(
            ExecuteAndGetRowTexts(kustoDatabase, $"R | join {swappedKindClause} L on Key", "LeftId", "RightId"),
            Is.EquivalentTo(pairs));
    }

    [Test]
    [Description("fullouter pads the missing side of the unmatched rows: the left-only row c has a null RightId and " +
                 "an empty Key1, the right-only row d a null LeftId and an empty Key")]
    public void Join_FullOuter_PadsTheMissingSideOfTheUnmatchedRows()
    {
        var kustoDatabase = CreateKindMatrixDatabase();
        const string query = "L | join kind=fullouter R on Key";

        Assert.That(
            ExecuteAndGetRowTexts(kustoDatabase,
                query + " | where isnull(RightId) | extend Key1IsEmpty = isempty(Key1)", "LeftId", "Key",
                "Key1IsEmpty"),
            Is.EqualTo(new[] { "4:c:True" }));
        Assert.That(
            ExecuteAndGetRowTexts(kustoDatabase,
                query + " | where isnull(LeftId) | extend KeyIsEmpty = isempty(Key)", "KeyIsEmpty", "Key1",
                "RightId"),
            Is.EqualTo(new[] { "True:d:50" }));
    }

    #endregion

    #region empty sides

    [TestCase("", 0L)]
    [TestCase("kind=inner", 0L)]
    [TestCase("kind=leftouter", 4L)]
    [TestCase("kind=rightouter", 0L)]
    [TestCase("kind=fullouter", 4L)]
    [TestCase("kind=leftsemi", 0L)]
    [TestCase("kind=leftanti", 4L)]
    [TestCase("kind=rightsemi", 0L)]
    [TestCase("kind=rightanti", 0L)]
    [Description("An empty right side: no pair, so leftouter, fullouter and leftanti return the 4 left rows and the " +
                 "other kinds no row")]
    public void Join_WithAnEmptyRightSide_RowCountPerKind(string kindClause, long expectedRows)
    {
        var kustoDatabase = CreateKindMatrixDatabase();

        Assert.That(
            ExecuteAndGetCount(kustoDatabase, $"L | join {kindClause} (R | where RightId < 0) on Key | count"),
            Is.EqualTo(expectedRows));
    }

    [TestCase("", 0L)]
    [TestCase("kind=inner", 0L)]
    [TestCase("kind=leftouter", 0L)]
    [TestCase("kind=rightouter", 5L)]
    [TestCase("kind=fullouter", 5L)]
    [TestCase("kind=leftsemi", 0L)]
    [TestCase("kind=leftanti", 0L)]
    [TestCase("kind=rightsemi", 0L)]
    [TestCase("kind=rightanti", 5L)]
    [Description("An empty left side: no pair, so rightouter, fullouter and rightanti return the 5 right rows and the " +
                 "other kinds no row")]
    public void Join_WithAnEmptyLeftSide_RowCountPerKind(string kindClause, long expectedRows)
    {
        var kustoDatabase = CreateKindMatrixDatabase();

        Assert.That(
            ExecuteAndGetCount(kustoDatabase, $"L | where LeftId < 0 | join {kindClause} R on Key | count"),
            Is.EqualTo(expectedRows));
    }

    [TestCase("")]
    [TestCase("kind=inner")]
    [TestCase("kind=leftouter")]
    [TestCase("kind=rightouter")]
    [TestCase("kind=fullouter")]
    [TestCase("kind=leftsemi")]
    [TestCase("kind=leftanti")]
    [TestCase("kind=rightsemi")]
    [TestCase("kind=rightanti")]
    [Description("Both sides empty: no row for any kind")]
    public void Join_WithBothSidesEmpty_ReturnsNoRows(string kindClause)
    {
        var kustoDatabase = CreateKindMatrixDatabase();

        Assert.That(
            ExecuteAndGetCount(kustoDatabase,
                $"L | where LeftId < 0 | join {kindClause} (R | where RightId < 0) on Key | count"),
            Is.EqualTo(0L));
    }

    [TestCase("kind=leftouter")]
    [TestCase("kind=fullouter")]
    [Description("With an empty right side, leftouter and fullouter return every left row with a null RightId and an " +
                 "empty Key1")]
    public void Join_WithAnEmptyRightSide_PadsEveryLeftRow(string kindClause)
    {
        var kustoDatabase = CreateKindMatrixDatabase();
        string query = $"L | join {kindClause} (R | where RightId < 0) on Key " +
                       "| extend RightIdIsNull = isnull(RightId), Key1IsEmpty = isempty(Key1)";

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "LeftId", "Key", "RightIdIsNull", "Key1IsEmpty"),
            Is.EquivalentTo(new[] { "1:a:True:True", "2:a:True:True", "3:b:True:True", "4:c:True:True" }));
    }

    [TestCase("kind=rightouter")]
    [TestCase("kind=fullouter")]
    [Description("With an empty left side, rightouter and fullouter return every right row with a null LeftId and an " +
                 "empty Key")]
    public void Join_WithAnEmptyLeftSide_PadsEveryRightRow(string kindClause)
    {
        var kustoDatabase = CreateKindMatrixDatabase();
        string query = $"L | where LeftId < 0 | join {kindClause} R on Key " +
                       "| extend LeftIdIsNull = isnull(LeftId), KeyIsEmpty = isempty(Key)";

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "Key1", "RightId", "LeftIdIsNull", "KeyIsEmpty"),
            Is.EquivalentTo(new[]
            {
                "a:10:True:True", "a:20:True:True", "a:30:True:True", "b:40:True:True", "d:50:True:True"
            }));
    }

    #endregion

    #region generated tables

    [Test]
    [Description("inner on the generated tables pairs each of the 10 left rows of a matching key with each of its 10 " +
                 "right rows: 100 pairs for each of the keys 0..4")]
    public void Join_Inner_OnGeneratedTables_Returns100PairsPerMatchingKey()
    {
        var kustoDatabase = CreateGeneratedDatabase();
        const string query = "BigLeft | join kind=inner BigRight on Key " +
                             "| summarize Pairs = count(), LeftRows = dcount(LeftId), RightRows = dcount(RightId) by Key";

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "Key", "Pairs", "LeftRows", "RightRows"),
            Is.EquivalentTo(new[] { "0:100:10:10", "1:100:10:10", "2:100:10:10", "3:100:10:10", "4:100:10:10" }));
    }

    [TestCase("kind=leftouter", "500:50:0")]
    [TestCase("kind=rightouter", "500:0:0")]
    [TestCase("kind=fullouter", "500:50:0")]
    [Description("The outer kinds on the generated tables split into the 500 pairs, the left rows without a right row " +
                 "(the 50 rows of the keys 5..9, kept by leftouter and fullouter) and the right rows without a left " +
                 "row (none)")]
    public void Join_OuterKinds_OnGeneratedTables_SplitIntoPairsAndUnmatchedRows(string kindClause, string expected)
    {
        var kustoDatabase = CreateGeneratedDatabase();
        string query = $"BigLeft | join {kindClause} BigRight on Key " +
                       "| summarize Pairs = countif(isnotnull(LeftId) and isnotnull(RightId)), " +
                       "LeftOnly = countif(isnull(RightId)), RightOnly = countif(isnull(LeftId))";

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "Pairs", "LeftOnly", "RightOnly"),
            Is.EqualTo(new[] { expected }));
    }

    [TestCase("kind=leftsemi", "50:5:0:4")]
    [TestCase("kind=leftanti", "50:5:5:9")]
    [TestCase("kind=rightsemi", "50:5:0:4")]
    [Description("The semi and anti kinds on the generated tables: leftsemi keeps the 50 left rows of the keys 0..4, " +
                 "leftanti the 50 left rows of the keys 5..9 and rightsemi all the 50 right rows (keys 0..4)")]
    public void Join_SemiAndAntiKinds_OnGeneratedTables_KeepTheRowsOfTheirKeys(string kindClause, string expected)
    {
        var kustoDatabase = CreateGeneratedDatabase();
        string query = $"BigLeft | join {kindClause} BigRight on Key " +
                       "| summarize Rows = count(), Keys = dcount(Key), MinKey = min(Key), MaxKey = max(Key)";

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "Rows", "Keys", "MinKey", "MaxKey"),
            Is.EqualTo(new[] { expected }));
    }

    #endregion
}
