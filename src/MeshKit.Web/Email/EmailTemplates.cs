using System.Net;

namespace MeshKit.Web.Email;

/// <summary>Brand-consistent, inline-styled mail (no external CSS survives mail clients). Every HTML mail has a text twin.</summary>
public static class EmailTemplates
{
    public static EmailMessage PurchaseConfirmation(string to, string packName, string baseUrl, string packSlug, long amount, string currency, string? licenseName)
    {
        var library = $"{baseUrl}/library";
        var download = $"{baseUrl}/library/{packSlug}/download";
        var licence = $"{baseUrl}/packs/{packSlug}/licence";
        var price = Components.Shared.Money.Format(amount, currency);
        var subject = $"Your pack is ready: {packName}";
        var html = Layout(subject, $"""
            <p style="margin:0 0 16px">Thanks for your purchase. <strong>{E(packName)}</strong> ({E(price)}) is in your library — download it as often as you like.</p>
            {Button(download, "Download the pack (.zip)")}
            <p style="margin:16px 0 0;color:#555">The zip contains every model as GLB, FBX, OBJ and USDZ with PBR texture maps, plus <code>LICENSE.txt</code>{(licenseName is null ? "" : $" ({E(licenseName)})")}. You can re-download from <a href="{library}" style="color:#e27a5f">your library</a> any time.</p>
            <p style="margin:16px 0 0;color:#555">Licence terms: <a href="{licence}" style="color:#e27a5f">{licence}</a></p>
            """);
        var text = $"""
            Thanks for your purchase. {packName} ({price}) is in your library.

            Download: {download}
            Your library: {library}
            Licence: {licence}

            The zip contains every model as GLB, FBX, OBJ and USDZ with PBR texture maps, plus LICENSE.txt.
            """;
        return new EmailMessage(to, null, subject, html, Footer(text));
    }

    public static EmailMessage SampleFollowUp(string to, string packName, string modelName, string baseUrl, string packSlug, string promotionCode, string discountLabel)
    {
        var packUrl = $"{baseUrl}/packs/{packSlug}";
        var subject = $"Did the {modelName} hold up? {discountLabel} on {packName}";
        var html = Layout(subject, $"""
            <p style="margin:0 0 16px">A couple of days ago you downloaded the free <strong>{E(modelName)}</strong> from <strong>{E(packName)}</strong>. If it held up in your engine — topology, UVs, scale, pivot — the other models in the pack were made the same way.</p>
            <p style="margin:0 0 16px">As promised, here is <strong>{E(discountLabel)}</strong> on the full pack. Enter the code on the Stripe checkout page:</p>
            <p style="margin:0 0 16px;font-size:22px;letter-spacing:2px;font-family:monospace"><strong>{E(promotionCode)}</strong></p>
            {Button(packUrl, $"Get {E(packName)}")}
            <p style="margin:16px 0 0;color:#555">You asked for this one email when you downloaded the sample; there is no list and nothing else will follow.</p>
            """);
        var text = $"""
            A couple of days ago you downloaded the free {modelName} from {packName}. If it held up in your engine, the other models in the pack were made the same way.

            As promised, here is {discountLabel} on the full pack. Enter this code on the Stripe checkout page:

                {promotionCode}

            Get the pack: {packUrl}

            You asked for this one email when you downloaded the sample; there is no list and nothing else will follow.
            """;
        return new EmailMessage(to, null, subject, html, Footer(text));
    }

    public static EmailMessage NewRelease(string to, Core.Catalog.PackManifest pack, string baseUrl, string unsubscribeUrl)
    {
        var packUrl = $"{baseUrl}/packs/{pack.Slug}";
        var price = Components.Shared.Money.Format(pack.Price.Amount, pack.Price.Currency);
        var sample = pack.SampleModel;
        var subject = $"New pack: {pack.Name}";
        var html = Layout(subject, $"""
            <p style="margin:0 0 16px"><strong>{E(pack.Name)}</strong> is out — {pack.Models.Count} game-ready models for {E(price)}.</p>
            <p style="margin:0 0 16px;color:#555">{E(pack.Description)}</p>
            {Button(packUrl, $"See {E(pack.Name)}")}
            {(sample is null ? "" : $"""<p style="margin:16px 0 0;color:#555">Try it first: the <strong>{E(sample.Name)}</strong> is free to download from the pack page.</p>""")}
            <p style="margin:24px 0 0;font-size:12px;color:#888">You get this because you asked to hear about new packs. <a href="{unsubscribeUrl}" style="color:#888">Stop these emails</a> — one click, no login.</p>
            """);
        var text = $"""
            {pack.Name} is out — {pack.Models.Count} game-ready models for {price}.

            {pack.Description}

            {packUrl}
            {(sample is null ? "" : $"Try it first: the {sample.Name} is free to download from the pack page.")}

            You get this because you asked to hear about new packs. Stop these emails (one click, no login): {unsubscribeUrl}
            """;
        return new EmailMessage(to, null, subject, html, Footer(text));
    }

    public static EmailMessage PasswordReset(string to, string resetUrl)
    {
        const string subject = "Reset your MeshKit password";
        var html = Layout(subject, $"""
            <p style="margin:0 0 16px">Someone asked to reset the password of the MeshKit account for this address. If that was you, choose a new password here — the link works for one hour:</p>
            {Button(resetUrl, "Choose a new password")}
            <p style="margin:16px 0 0;color:#555">If it wasn't you, ignore this email; your password stays as it is.</p>
            """);
        var text = $"""
            Someone asked to reset the password of the MeshKit account for this address.
            If that was you, choose a new password here (valid one hour):

            {resetUrl}

            If it wasn't you, ignore this email.
            """;
        return new EmailMessage(to, null, subject, html, Footer(text));
    }

    public static EmailMessage ConfirmEmail(string to, string confirmUrl)
    {
        const string subject = "Confirm your MeshKit email address";
        var html = Layout(subject, $"""
            <p style="margin:0 0 16px">Welcome to MeshKit. Confirm this address so we can send you purchase receipts and download links:</p>
            {Button(confirmUrl, "Confirm my email")}
            <p style="margin:16px 0 0;color:#555">You can buy and download without confirming; this only makes sure our emails reach you.</p>
            """);
        var text = $"""
            Welcome to MeshKit. Confirm this address so we can send you purchase receipts and download links:

            {confirmUrl}
            """;
        return new EmailMessage(to, null, subject, html, Footer(text));
    }

    private static string Layout(string title, string body) => $"""
        <!doctype html><html><body style="margin:0;background:#fcfaf4;font-family:-apple-system,Segoe UI,Helvetica,Arial,sans-serif;color:#1b1f24">
        <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#fcfaf4"><tr><td align="center" style="padding:32px 16px">
        <table role="presentation" width="560" cellspacing="0" cellpadding="0" style="max-width:560px;width:100%">
        <tr><td style="padding:0 0 20px;font-size:20px;font-weight:700">Mesh<span style="color:#e27a5f">Kit</span></td></tr>
        <tr><td style="background:#ffffff;border:1px solid rgba(27,31,36,.1);border-radius:16px;padding:28px;font-size:16px;line-height:1.55">
        <h1 style="margin:0 0 16px;font-size:22px">{E(title)}</h1>
        {body}
        </td></tr>
        <tr><td style="padding:20px 4px 0;font-size:12px;color:#777;line-height:1.5">MeshKit by Atypical Consulting · VAT BE 0744.517.956 · Belgium<br>Transactional message about your MeshKit account or order — no marketing, no tracking.</td></tr>
        </table></td></tr></table></body></html>
        """;

    private static string Button(string url, string label) =>
        $"""<p style="margin:0"><a href="{url}" style="display:inline-block;background:#fd9b83;color:#1b1f24;text-decoration:none;font-weight:600;padding:12px 22px;border-radius:999px">{E(label)}</a></p>""";

    private static string Footer(string text) => text.Trim() + "\n\n--\nMeshKit by Atypical Consulting · VAT BE 0744.517.956 · Belgium\n";

    private static string E(string value) => WebUtility.HtmlEncode(value);
}
