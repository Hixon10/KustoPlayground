using System.Text.Json.Nodes;

namespace KustoPlayground.Core.Tests;

public class ToIntToLongToRealToStringToBoolAstraTests
{
    [TestCase("'0042'", 42)]
    [TestCase("9.875", 9)]
    [TestCase("-9.875", -9)]
    [TestCase("-0.75", 0)]
    [TestCase("'2147483647'", int.MaxValue)]
    [TestCase("'-2147483648'", int.MinValue)]
    [TestCase("'2147483648'", null)]
    [TestCase("'-2147483649'", null)]
    [TestCase("'many'", null)]
    [TestCase("''", null)]
    [TestCase("long(null)", null)]
    public void ToInt_TruncatesTowardZeroAndChecksInt32Range(string operand, int? expected)
    {
        Assert.That(AstraTestUtils.Print<int?>($"toint({operand})"), Is.EqualTo(expected));
    }

    [TestCase("int(42)", 42L)]
    [TestCase("-7.875", -7L)]
    [TestCase("'4294967296'", 4294967296L)]
    [TestCase("'9007199254740993'", 9007199254740993L)]
    [TestCase("'9223372036854775807'", long.MaxValue)]
    [TestCase("'-9223372036854775808'", long.MinValue)]
    [TestCase("'9223372036854775808'", null)]
    [TestCase("'-9223372036854775809'", null)]
    [TestCase("'x12'", null)]
    [TestCase("dynamic(null)", null)]
    public void ToLong_PreservesInt64PrecisionAndReturnsNullOnFailure(string operand, long? expected)
    {
        Assert.That(AstraTestUtils.Print<long?>($"tolong({operand})"), Is.EqualTo(expected));
    }

    [TestCase("'-0.125'", -0.125)]
    [TestCase("'-1.25e2'", -125.0)]
    [TestCase("int(3)", 3.0)]
    [TestCase("dynamic(2.5)", 2.5)]
    [TestCase("real(nan)", double.NaN)]
    [TestCase("'not a number'", null)]
    [TestCase("''", null)]
    [TestCase("real(null)", null)]
    public void ToReal_ProducesDoubleOrNull(string operand, double? expected)
    {
        Assert.That(AstraTestUtils.Print<double?>($"toreal({operand})"), Is.EqualTo(expected));
    }

    [TestCase("-17", "-17")]
    [TestCase("0.125", "0.125")]
    [TestCase("'  text  '", "  text  ")]
    [TestCase("dynamic('abc')", "abc")]
    [TestCase("guid(23ba600a-1e4c-44cb-ad8c-436447d46991)", "23ba600a-1e4c-44cb-ad8c-436447d46991")]
    [TestCase("long(null)", "")]
    [TestCase("dynamic(null)", "")]
    public void ToString_FormatsScalarsAndMapsNullToEmpty(string operand, string expected)
    {
        Assert.That(AstraTestUtils.Print<string>($"tostring({operand})"), Is.EqualTo(expected));
    }

    [Test]
    public void ToString_DynamicBagProducesJsonWithoutReorderingArrayElements()
    {
        var text = AstraTestUtils.Print<string>("""tostring(dynamic({"items":[2,null,1],"ok":true}))""");
        AstraTestUtils.AssertJsonValue(JsonNode.Parse(text), """{"ok":true,"items":[2,null,1]}""");
    }

    [TestCase("'true'", true)]
    [TestCase("'false'", false)]
    [TestCase("'TrUe'", true)]
    [TestCase("false", false)]
    [TestCase("0", false)]
    [TestCase("123", true)]
    [TestCase("-2", true)]
    [TestCase("'maybe'", null)]
    [TestCase("''", null)]
    [TestCase("bool(null)", null)]
    public void ToBool_DistinguishesFalseFromAnUnconvertibleValue(string operand, bool? expected)
    {
        Assert.That(AstraTestUtils.Print<bool?>($"tobool({operand})"), Is.EqualTo(expected));
    }

    [Test]
    public void Conversions_FailedConversionIsLocalToItsRowAndCanBeFiltered()
    {
        var database = new KustoDatabase();
        database.AddTable(TestUtils.GenerateTableWithColumn(new[] { "008", "bad", "0", "-2", "8" }, "Text", "Readings"));

        var rows = TestUtils.ExecuteAndGetDataFor3Columns<string, int, string>("Text", "N", "Canonical", database,
            """
            Readings
            | extend N = toint(Text)
            | where N > 0
            | extend Canonical = tostring(N)
            """);

        Assert.That(rows, Is.EquivalentTo(new[] { ("008", 8, "8"), ("8", 8, "8") }));
    }
}
