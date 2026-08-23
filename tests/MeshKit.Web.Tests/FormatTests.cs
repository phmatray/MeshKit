using System.Globalization;
using MeshKit.Web.Components.Shared;

namespace MeshKit.Web.Tests;

public sealed class FormatTests
{
    [Theory]
    [InlineData("fr-BE")]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void Bytes_uses_a_dot_decimal_separator_whatever_the_server_culture(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            Assert.Equal("276.7 MB", Format.Bytes(290_100_000));
            Assert.Equal("1.5 GB", Format.Bytes(1_610_612_736));
            Assert.Equal("12 KB", Format.Bytes(12_288));
            Assert.Equal("42 B", Format.Bytes(42));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(0, "0 models")]
    [InlineData(1, "1 model")]
    [InlineData(8, "8 models")]
    public void Plural_adds_an_s_except_for_one(int n, string expected)
    {
        Assert.Equal(expected, Format.Plural(n, "model"));
    }
}
