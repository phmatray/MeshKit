using System.Globalization;

namespace MeshKit.Web.Components.Shared;

public static class Money
{
    private static readonly Dictionary<string, string> Symbols = new(StringComparer.OrdinalIgnoreCase)
    {
        ["eur"] = "€",
        ["usd"] = "$",
        ["gbp"] = "£",
        ["chf"] = "CHF ",
    };

    /// <summary>Minor units + ISO code → "€19.00" (known symbols) or "19.00 SEK".</summary>
    public static string Format(long amount, string currency)
    {
        var value = (amount / 100m).ToString("0.00", CultureInfo.InvariantCulture);
        return Symbols.TryGetValue(currency, out var symbol) ? $"{symbol}{value}" : $"{value} {currency.ToUpperInvariant()}";
    }
}
