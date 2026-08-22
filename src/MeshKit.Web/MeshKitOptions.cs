namespace MeshKit.Web;

public sealed class MeshKitOptions
{
    public const string Section = "MeshKit";

    /// <summary>Origin the public sees (scheme + host); Stripe redirects back here after checkout.</summary>
    public string PublicBaseUrl { get; set; } = "http://localhost:5080";
}
