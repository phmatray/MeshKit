using System.Globalization;

namespace MeshKit.Web.Components.Shared;

/// <summary>
/// Number formatting for the store. Everything is culture-invariant on purpose: the server's
/// culture leaked a French decimal comma ("276,7 MB") next to invariant counts ("6,022") on the pack page.
/// </summary>
public static class Format
{
    public static string Count(int n) => n.ToString("N0", CultureInfo.InvariantCulture);

    public static string Bytes(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{(bytes / (double)(1L << 30)).ToString("0.0", CultureInfo.InvariantCulture)} GB",
        >= 1L << 20 => $"{(bytes / (double)(1L << 20)).ToString("0.0", CultureInfo.InvariantCulture)} MB",
        >= 1L << 10 => $"{(bytes / (double)(1L << 10)).ToString("0", CultureInfo.InvariantCulture)} KB",
        _ => $"{bytes} B",
    };

    public static string Metres(double m) => m >= 10 ? m.ToString("0", CultureInfo.InvariantCulture) : m.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>"1 model", "8 models" — regular English plural only.</summary>
    public static string Plural(int n, string noun) => $"{Count(n)} {noun}{(n == 1 ? "" : "s")}";
}
