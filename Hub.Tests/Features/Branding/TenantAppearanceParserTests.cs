using Hub.Features.Branding;

namespace Hub.Tests.Features.Branding;

public class TenantAppearanceParserTests
{
    [Fact]
    public void Parse_KeepsValidFieldsAndLowercasesColours()
    {
        var result = TenantAppearanceParser.Parse("""{"contrast":"high","primary":"#2A4EFF","secondary":"#13B964","textLight":"#222222","textDark":"#EEEEEE"}""");

        Assert.Equal(new TenantAppearance("high", "#2a4eff", "#13b964", "#222222", "#eeeeee"), result);
    }

    [Fact]
    public void Parse_DropsInvalidFieldsOneByOne()
    {
        var result = TenantAppearanceParser.Parse("""{"contrast":"loud","primary":"red","secondary":"#abc","textLight":"#12345g","textDark":"#000000","bogus":1}""");

        Assert.Equal(new TenantAppearance(null, null, null, null, "#000000"), result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{not json")]
    [InlineData("\"high\"")]
    [InlineData("[1,2]")]
    [InlineData("{}")]
    [InlineData("""{"primary":123,"contrast":null}""")]
    [InlineData("""{"textDark":"#abc"}""")]
    public void Parse_ReturnsNullWhenNothingSurvives(string? raw) => Assert.Null(TenantAppearanceParser.Parse(raw));

    [Theory]
    [InlineData("auto")]
    [InlineData("standard")]
    [InlineData("high")]
    public void Parse_AcceptsEachContrastValue(string contrast) =>
        Assert.Equal(contrast, TenantAppearanceParser.Parse($$"""{"contrast":"{{contrast}}"}""")!.Contrast);

    [Fact]
    public void Serialises_CamelCaseWithNullsOmitted()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new TenantAppearance("auto", null, "#13b964", null, null));

        Assert.Equal("""{"contrast":"auto","secondary":"#13b964"}""", json);
    }
}
