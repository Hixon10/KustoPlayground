using static KustoPlayground.Core.Tests.NullableNeonTestUtils;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// Azure Kusto null and empty-string semantics for all supported string operators
/// (contains, !contains, startswith, !startswith, endswith, !endswith, matches regex, ==, =~, !=, !~)
/// and string/conversion scalar functions
/// (toupper, tolower, strlen, base64_encode_tostring, base64_decode_tostring, url_encode, url_decode, tostring, toreal, todouble).
/// </summary>
public class NullableStringOperatorsAndFunctionsNeonTests
{
    #region String operators with nullable string column and empty strings

    [Test]
    [Description("contains and !contains on nullable string column: null rows do not match non-empty pattern and are kept by !contains")]
    public void StringContainsAndNotContains_NullableColumn()
    {
        var table = CreateNullableTable2<int?, string?>(
            [(1, "GreenTea"), (2, "RedWine"), (3, ""), (4, null)],
            tableName: "Items",
            columnName1: "Id",
            columnName2: "Text");
        var kustoDatabase = CreateDatabase(table);

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Items | where Text contains \"red\""),
            Is.EquivalentTo(new int?[] { 2 }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Items | where Text !contains \"red\""),
            Is.EquivalentTo(new int?[] { 1, 3, 4 }));

        var projected = ExecuteAndGetNullable3Columns<int?, bool?, bool?>(
            "Id",
            "HasRed",
            "NoRed",
            kustoDatabase,
            "Items | extend HasRed = Text contains \"red\", NoRed = Text !contains \"red\" | project Id, HasRed, NoRed");

        Assert.That(projected, Is.EquivalentTo(new List<(int?, bool?, bool?)>
        {
            (1, false, true),
            (2, true, false),
            (3, false, true),
            (4, false, true)
        }));
    }

    [Test]
    [Description("startswith and !startswith on nullable string column")]
    public void StringStartsWithAndNotStartsWith_NullableColumn()
    {
        var table = CreateNullableTable2<int?, string?>(
            [(1, "AzureStorage"), (2, "azureSQL"), (3, "Kusto"), (4, ""), (5, null)],
            tableName: "Items",
            columnName1: "Id",
            columnName2: "Text");
        var kustoDatabase = CreateDatabase(table);

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Items | where Text startswith \"az\""),
            Is.EquivalentTo(new int?[] { 1, 2 }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Items | where Text !startswith \"az\""),
            Is.EquivalentTo(new int?[] { 3, 4, 5 }));
    }

    [Test]
    [Description("endswith and !endswith on nullable string column")]
    public void StringEndsWithAndNotEndsWith_NullableColumn()
    {
        var table = CreateNullableTable2<int?, string?>(
            [(1, "report.CSV"), (2, "data.csv"), (3, "notes.txt"), (4, ""), (5, null)],
            tableName: "Files",
            columnName1: "Id",
            columnName2: "Name");
        var kustoDatabase = CreateDatabase(table);

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Files | where Name endswith \".csv\""),
            Is.EquivalentTo(new int?[] { 1, 2 }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Files | where Name !endswith \".csv\""),
            Is.EquivalentTo(new int?[] { 3, 4, 5 }));
    }

    [Test]
    [Description("matches regex on nullable string column does not throw on null rows and matches valid strings")]
    public void StringMatchesRegex_NullableColumn()
    {
        var table = CreateNullableTable2<int?, string?>(
            [(1, "err-404"), (2, "ok-200"), (3, ""), (4, null)],
            tableName: "Logs",
            columnName1: "Id",
            columnName2: "Msg");
        var kustoDatabase = CreateDatabase(table);

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Logs | where Msg matches regex \"^err-[0-9]+$\""),
            Is.EquivalentTo(new int?[] { 1 }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Logs | where Msg matches regex \".+\""),
            Is.EquivalentTo(new int?[] { 1, 2 }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Logs | where not(Msg matches regex \"^err\")"),
            Is.EquivalentTo(new int?[] { 2, 3, 4 }));
    }

    [Test]
    [Description("Empty string behavior for contains, startswith, endswith, matches regex, and equality operators")]
    public void StringOperators_EmptyStringEdgeCases()
    {
        AssertPrintBool("print \"\" contains \"\"", true);
        AssertPrintBool("print \"\" !contains \"\"", false);
        AssertPrintBool("print \"abc\" contains \"\"", true);
        AssertPrintBool("print \"\" contains \"a\"", false);
        AssertPrintBool("print \"\" !contains \"a\"", true);
        AssertPrintBool("print \"\" startswith \"\"", true);
        AssertPrintBool("print \"abc\" startswith \"\"", true);
        AssertPrintBool("print \"\" startswith \"a\"", false);
        AssertPrintBool("print \"\" !startswith \"a\"", true);
        AssertPrintBool("print \"\" endswith \"\"", true);
        AssertPrintBool("print \"abc\" endswith \"\"", true);
        AssertPrintBool("print \"\" endswith \"a\"", false);
        AssertPrintBool("print \"\" !endswith \"a\"", true);
        AssertPrintBool("print \"\" matches regex \"^$\"", true);
        AssertPrintBool("print \"\" matches regex \".+\"", false);
        AssertPrintBool("print \"\" == \"\"", true);
        AssertPrintBool("print \"\" =~ \"\"", true);
        AssertPrintBool("print \"\" != \"\"", false);
        AssertPrintBool("print \"\" !~ \"\"", false);
        AssertPrintBool("print \"\" != \"a\"", true);
        AssertPrintBool("print \"\" !~ \"a\"", true);
    }

    [Test]
    [Description("String equality operators ==, !=, =~, !~ on nullable string column against non-empty literals")]
    public void StringEqualityAndCaseInsensitiveEquality_NullableColumn()
    {
        var table = CreateNullableTable2<int?, string?>(
            [(1, "Hello"), (2, "hello"), (3, "World"), (4, ""), (5, null)],
            tableName: "Words",
            columnName1: "Id",
            columnName2: "Text");
        var kustoDatabase = CreateDatabase(table);

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Words | where Text == \"Hello\""),
            Is.EquivalentTo(new int?[] { 1 }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Words | where Text != \"Hello\""),
            Is.EquivalentTo(new int?[] { 2, 3, 4, 5 }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Words | where Text =~ \"HELLO\""),
            Is.EquivalentTo(new int?[] { 1, 2 }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Words | where Text !~ \"HELLO\""),
            Is.EquivalentTo(new int?[] { 3, 4, 5 }));
    }

    [Test]
    [Description("String operators comparing two nullable string columns do not throw when either side is null")]
    public void StringOperators_TwoNullableColumns_DoNotThrow()
    {
        var table = new NullableTableBuilder("Patterns")
            .WithColumn<int>("Id", isNullable: false)
            .WithColumn<string>("Text")
            .WithColumn<string>("Needle")
            .WithRow(1, "HelloWorld", "world")
            .WithRow(2, "HelloWorld", "xyz")
            .WithRow(3, null, "world")
            .WithRow(4, "HelloWorld", null)
            .WithRow(5, null, null)
            .Build();
        var kustoDatabase = CreateDatabase(table);

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Patterns | where Text contains Needle and isnotempty(Needle)"),
            Is.EquivalentTo(new int?[] { 1 }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Patterns | where Text endswith Needle and isnotempty(Needle)"),
            Is.EquivalentTo(new int?[] { 1 }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Patterns | where Text startswith Needle and isnotempty(Needle)"),
            Is.Empty);
    }

    #endregion

    #region String scalar functions (toupper, tolower, strlen, base64_*, url_*)

    [Test]
    [Description("toupper(), tolower(), and strlen() return empty string / 0 for null and empty string cells")]
    public void ToUpperToLowerStrLen_NullableStringColumn()
    {
        var table = CreateNullableTable2<int?, string?>(
            [(1, "AbC"), (2, ""), (3, null), (4, "  ")],
            tableName: "Texts",
            columnName1: "Id",
            columnName2: "Val");
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndAssertSuccess(
            kustoDatabase,
            "Texts | extend U = toupper(Val), L = tolower(Val), Len = strlen(Val)");

        var byId = rows.ToDictionary(r => (int)r["Id"]!);
        Assert.That(byId[1]["U"], Is.EqualTo("ABC"));
        Assert.That(byId[1]["L"], Is.EqualTo("abc"));
        Assert.That(AsNullableLong(byId[1]["Len"]), Is.EqualTo(3L));

        Assert.That(byId[2]["U"], Is.EqualTo(""));
        Assert.That(byId[2]["L"], Is.EqualTo(""));
        Assert.That(AsNullableLong(byId[2]["Len"]), Is.EqualTo(0L));

        Assert.That(byId[3]["U"], Is.EqualTo(""));
        Assert.That(byId[3]["L"], Is.EqualTo(""));
        Assert.That(AsNullableLong(byId[3]["Len"]), Is.EqualTo(0L));

        Assert.That(byId[4]["U"], Is.EqualTo("  "));
        Assert.That(byId[4]["L"], Is.EqualTo("  "));
        Assert.That(AsNullableLong(byId[4]["Len"]), Is.EqualTo(2L));
    }

    [Test]
    [Description("base64_encode_tostring() and base64_decode_tostring() handle null, empty string, valid values, and invalid base64 without throwing")]
    public void Base64EncodeAndDecode_NullableStringColumnAndInvalidInput()
    {
        var table = CreateNullableTable2<int?, string?>(
            [(1, "hello"), (2, ""), (3, null)],
            tableName: "Payloads",
            columnName1: "Id",
            columnName2: "Plain");
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndGetNullable3Columns<int?, string?, string?>(
            "Id",
            "Encoded",
            "Decoded",
            kustoDatabase,
            "Payloads | extend Encoded = base64_encode_tostring(Plain) | extend Decoded = base64_decode_tostring(Encoded) | project Id, Encoded, Decoded");

        Assert.That(rows, Is.EquivalentTo(new List<(int?, string?, string?)>
        {
            (1, "aGVsbG8=", "hello"),
            (2, "", ""),
            (3, "", "")
        }));

        // Direct decode on null column and invalid base64 string (Kusto returns "" instead of throwing)
        var decodeTable = CreateNullableTable2<int?, string?>(
            [(1, "aGVsbG8="), (2, null), (3, ""), (4, "%%%not-valid-base64%%%")],
            tableName: "Encoded",
            columnName1: "Id",
            columnName2: "Raw");
        var decodeDb = CreateDatabase(decodeTable);

        var decoded = ExecuteAndGetNullable2Columns<int?, string?>(
            "Id",
            "Out",
            decodeDb,
            "Encoded | extend Out = base64_decode_tostring(Raw) | project Id, Out");

        Assert.That(decoded, Is.EquivalentTo(new List<(int?, string?)>
        {
            (1, "hello"),
            (2, ""),
            (3, ""),
            (4, "")
        }));
    }

    [Test]
    [Description("url_encode() and url_decode() return empty string for null/empty rows and roundtrip encoded characters")]
    public void UrlEncodeAndDecode_NullableStringColumn()
    {
        var table = CreateNullableTable2<int?, string?>(
            [(1, "a b&c=1"), (2, ""), (3, null)],
            tableName: "Urls",
            columnName1: "Id",
            columnName2: "Raw");
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndGetNullable3Columns<int?, string?, string?>(
            "Id",
            "Encoded",
            "Decoded",
            kustoDatabase,
            "Urls | extend Encoded = url_encode(Raw) | extend Decoded = url_decode(Encoded) | project Id, Encoded, Decoded");

        Assert.That(rows, Is.EquivalentTo(new List<(int?, string?, string?)>
        {
            (1, "a+b%26c%3d1", "a b&c=1"),
            (2, "", ""),
            (3, "", "")
        }));

        var directDecode = ExecuteAndGetNullable2Columns<int?, string?>(
            "Id",
            "Decoded",
            kustoDatabase,
            "Urls | extend Decoded = url_decode(Raw) | project Id, Decoded");

        Assert.That(directDecode, Is.EquivalentTo(new List<(int?, string?)>
        {
            (1, "a b&c=1"),
            (2, ""),
            (3, "")
        }));
    }

    #endregion

    #region tostring, toreal, todouble with nulls and compositions

    [TestCase("print tostring(int(null))")]
    [TestCase("print tostring(long(null))")]
    [TestCase("print tostring(real(null))")]
    [TestCase("print tostring(decimal(null))")]
    [TestCase("print tostring(bool(null))")]
    [TestCase("print tostring(datetime(null))")]
    [TestCase("print tostring(timespan(null))")]
    [TestCase("print tostring(guid(null))")]
    [TestCase("print tostring(dynamic(null))")]
    [Description("tostring() of any typed null literal returns empty string (\"\"), never null")]
    public void ToString_TypedNullLiterals_ReturnsEmptyString(string query)
    {
        object? actual = ExecutePrintSingleValue(query);
        Assert.That(actual, Is.EqualTo(""));
    }

    [Test]
    [Description("tostring() on nullable columns returns empty string for null rows and stringified values for non-null rows")]
    public void ToString_NullableColumns_ComposesWithStringOperatorsAndFunctions()
    {
        var table = CreateNullableTable2<int?, int?>(
            [(1, 42), (2, null), (3, 0)],
            tableName: "Nums",
            columnName1: "Id",
            columnName2: "Val");
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndAssertSuccess(
            kustoDatabase,
            "Nums | extend S = tostring(Val) | extend Len = strlen(S), Has4 = S contains \"4\", NotHas4 = S !contains \"4\", Empty = isempty(S)");

        var byId = rows.ToDictionary(r => (int)r["Id"]!);
        Assert.That(byId[1]["S"], Is.EqualTo("42"));
        Assert.That(AsNullableLong(byId[1]["Len"]), Is.EqualTo(2L));
        Assert.That(byId[1]["Has4"], Is.EqualTo(true));
        Assert.That(byId[1]["NotHas4"], Is.EqualTo(false));
        Assert.That(byId[1]["Empty"], Is.EqualTo(false));

        Assert.That(byId[2]["S"], Is.EqualTo(""));
        Assert.That(AsNullableLong(byId[2]["Len"]), Is.EqualTo(0L));
        Assert.That(byId[2]["Has4"], Is.EqualTo(false));
        Assert.That(byId[2]["NotHas4"], Is.EqualTo(true));
        Assert.That(byId[2]["Empty"], Is.EqualTo(true));

        Assert.That(byId[3]["S"], Is.EqualTo("0"));
        Assert.That(AsNullableLong(byId[3]["Len"]), Is.EqualTo(1L));
        Assert.That(byId[3]["Empty"], Is.EqualTo(false));
    }

    [Test]
    [Description("toreal() and todouble() return null on null, empty string, and unparseable strings, and double on valid inputs")]
    public void ToRealAndToDouble_NullableAndInvalidInputs()
    {
        AssertPrintNull("print toreal(int(null))");
        AssertPrintNull("print todouble(long(null))");
        AssertPrintNull("print toreal(real(null))");
        AssertPrintNull("print toreal(\"\")");
        AssertPrintNull("print todouble(\"not-a-number\")");

        var table = new NullableTableBuilder("Inputs")
            .WithColumn<int>("Id", isNullable: false)
            .WithColumn<string>("Text")
            .WithColumn<int>("Num")
            .WithRow(1, "3.5", 7)
            .WithRow(2, "", null)
            .WithRow(3, null, 0)
            .WithRow(4, "abc", -2)
            .Build();
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndGetNullable3Columns<int?, double?, double?>(
            "Id",
            "FromText",
            "FromNum",
            kustoDatabase,
            "Inputs | extend FromText = toreal(Text), FromNum = todouble(Num) | project Id, FromText, FromNum");

        Assert.That(rows, Is.EquivalentTo(new List<(int?, double?, double?)>
        {
            (1, 3.5, 7.0),
            (2, null, null),
            (3, null, 0.0),
            (4, null, -2.0)
        }));
    }

    #endregion
}
