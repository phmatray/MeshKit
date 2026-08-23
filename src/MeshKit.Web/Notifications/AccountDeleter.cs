using System.Security.Cryptography;
using System.Text;
using MeshKit.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MeshKit.Web.Notifications;

/// <summary>
/// What the privacy policy promises: the account (email, password, consents, sample history) goes immediately;
/// order records stay for accounting law, pseudonymised — the user id becomes a stable hash nobody can reverse.
/// </summary>
public sealed class AccountDeleter(ApplicationDbContext db, UserManager<ApplicationUser> users, ILogger<AccountDeleter> logger)
{
    public static string Pseudonym(string userId) => "deleted:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(userId)))[..16];

    public async Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var pseudonym = Pseudonym(user.Id);
        await db.Orders.Where(o => o.UserId == user.Id).ExecuteUpdateAsync(s => s.SetProperty(o => o.UserId, pseudonym), cancellationToken);
        await db.Entitlements.Where(e => e.UserId == user.Id).ExecuteDeleteAsync(cancellationToken);
        await db.SampleDownloads.Where(d => d.UserId == user.Id).ExecuteDeleteAsync(cancellationToken);

        var result = await users.DeleteAsync(user);
        if (result.Succeeded)
        {
            logger.LogInformation("Account deleted; orders kept under {Pseudonym}", pseudonym);
        }

        return result;
    }
}
