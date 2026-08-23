using MeshKit.Web.Catalog;
using MeshKit.Web.Data;
using MeshKit.Web.Email;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MeshKit.Web.Notifications;

/// <summary>
/// Emails every opted-in account when a pack is released — once per pack, whatever happens to it afterwards.
/// Every email carries a signed one-click unsubscribe link that needs no login.
/// </summary>
public sealed class ReleaseAnnouncer(
    ApplicationDbContext db,
    ICatalogService catalog,
    IEmailQueue emails,
    IDataProtectionProvider protection,
    IOptions<MeshKitOptions> options,
    ILogger<ReleaseAnnouncer> logger,
    TimeProvider? timeProvider = null)
{
    public const string UnsubscribePurpose = "MeshKit.ReleaseUnsubscribe";

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    /// <summary>Returns the number of recipients, 0 when already announced or nothing to send.</summary>
    public async Task<int> AnnounceAsync(string packSlug, CancellationToken cancellationToken)
    {
        if (await db.PackAnnouncements.AnyAsync(a => a.PackSlug == packSlug, cancellationToken))
        {
            return 0;
        }

        var pack = catalog.Find(packSlug);
        if (pack is not { IsSellable: true })
        {
            return 0;
        }

        var recipients = await db.Users
            .Where(u => u.NewReleaseOptIn && u.Email != null)
            .Select(u => new { u.Id, Email = u.Email! })
            .ToListAsync(cancellationToken);

        var baseUrl = options.Value.PublicBaseUrl.TrimEnd('/');
        var protector = protection.CreateProtector(UnsubscribePurpose);
        foreach (var user in recipients)
        {
            var unsubscribe = $"{baseUrl}/account/notifications/unsubscribe?token={Uri.EscapeDataString(protector.Protect(user.Id))}";
            emails.Enqueue(EmailTemplates.NewRelease(user.Email, pack, baseUrl, unsubscribe));
        }

        db.PackAnnouncements.Add(new PackAnnouncement { PackSlug = packSlug, SentAt = _time.GetUtcNow(), Recipients = recipients.Count });
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Announced pack {Slug} to {Count} subscriber(s)", packSlug, recipients.Count);
        return recipients.Count;
    }

    /// <summary>Turns the token from an email link back into a user id, or null if it was forged or corrupted.</summary>
    public string? UserIdFromUnsubscribeToken(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        try
        {
            return protection.CreateProtector(UnsubscribePurpose).Unprotect(token);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }
}
