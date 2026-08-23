namespace MeshKit.Web;

public sealed class MeshKitOptions
{
    public const string Section = "MeshKit";

    /// <summary>Origin the public sees (scheme + host); Stripe redirects back here after checkout.</summary>
    public string PublicBaseUrl { get; set; } = "http://localhost:5080";

    public SampleFollowUpOptions SampleFollowUp { get; set; } = new();
}

/// <summary>
/// The one email sent after a free-sample download, only to people who ticked the box for it. Off until a
/// promotion code exists: the checkbox promises a discount, so it is not shown when there is none to give.
/// </summary>
public sealed class SampleFollowUpOptions
{
    /// <summary>A Stripe promotion code created in the Dashboard (customer-facing code, e.g. <c>SAMPLE15</c>).</summary>
    public string? PromotionCode { get; set; }

    /// <summary>How the discount reads in the email and on the checkbox, e.g. "15% off".</summary>
    public string DiscountLabel { get; set; } = "15% off";

    /// <summary>Hours between the download and the email — long enough to have actually opened the model.</summary>
    public int DelayHours { get; set; } = 48;

    public bool Enabled => !string.IsNullOrWhiteSpace(PromotionCode);
}
