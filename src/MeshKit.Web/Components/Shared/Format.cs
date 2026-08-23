using System.Globalization;

namespace MeshKit.Web.Components.Shared;

public static class Format
{
    public static string Count(int n) => n.ToString("N0", CultureInfo.InvariantCulture);

    public static string Bytes(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):0} KB",
        _ => $"{bytes} B",
    };

    public static string Metres(double m) => m >= 10 ? m.ToString("0", CultureInfo.InvariantCulture) : m.ToString("0.##", CultureInfo.InvariantCulture);
}
